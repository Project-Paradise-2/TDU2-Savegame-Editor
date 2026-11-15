using System.IO;
using System.Text.Json;

namespace TDU2SaveEditor.Services;

public sealed class CarInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Version { get; set; } = "";
    public int VehicleCategory { get; set; }
    public long Price { get; set; }
    public int PowerBhp { get; set; }
    public double TopSpeed { get; set; }
    public double Acceleration { get; set; }

    public override string ToString() => Name;
}

/// <summary>Vehicle lookup built from the game's database.json (DBCarPhysicsData + DBBrands).</summary>
public sealed class CarsDatabase
{
    private readonly Dictionary<int, CarInfo> _byId = new();
    public IReadOnlyCollection<CarInfo> All => _byId.Values;

    public CarInfo? Get(int archetype) => _byId.TryGetValue(archetype, out var c) ? c : null;

    public CarInfo? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var c in _byId.Values)
            if (string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public IReadOnlyList<string> Names
    {
        get
        {
            var list = _byId.Values.Select(c => c.Name).Distinct().ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }

    public string NameOf(int archetype)
        => _byId.TryGetValue(archetype, out var c) ? c.Name : $"Unbekannt ({archetype})";

    internal void Add(CarInfo car)
    {
        if (car.Id != 0 && !string.IsNullOrEmpty(car.Name))
            _byId[car.Id] = car;
    }
}

public sealed class HouseInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SpotName { get; set; } = "";
    public string Island { get; set; } = "";
    public int Level { get; set; }
    public int Slots { get; set; }
    public ulong HouseHc { get; set; }

    public override string ToString() => $"{Name} ({Island}, {Slots} Plätze)";
}

public sealed class HousesDatabase
{
    private readonly Dictionary<ulong, HouseInfo> _byHc = new();
    private readonly Dictionary<int, HouseInfo> _byId = new();
    public IReadOnlyCollection<HouseInfo> All => _byId.Values;

    public HouseInfo? GetByHc(ulong hc) => _byHc.TryGetValue(hc, out var h) ? h : null;
    public HouseInfo? GetById(int id) => _byId.TryGetValue(id, out var h) ? h : null;

    public HouseInfo? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var h in _byId.Values)
            if (string.Equals(h.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return h;
        return null;
    }

    public IReadOnlyList<string> Names
    {
        get
        {
            var list = _byId.Values.Select(h => h.Name).Distinct().ToList();
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }

    public string NameOfHouse(ulong hc)
    {
        if (hc == ulong.MaxValue) return "—";
        return _byHc.TryGetValue(hc, out var h) ? h.Name : $"Haus {hc}";
    }

    public static HousesDatabase Load(string path)
    {
        var db = new HousesDatabase();
        if (!File.Exists(path)) return db;
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("houses", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return db;

        foreach (var e in arr.EnumerateArray())
        {
            var h = new HouseInfo
            {
                Id = e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
                Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                SpotName = e.TryGetProperty("spot_name", out var s) ? s.GetString() ?? "" : "",
                Island = e.TryGetProperty("island", out var il) ? il.GetString() ?? "" : "",
                Level = e.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0,
                Slots = e.TryGetProperty("slots", out var sl) && sl.ValueKind == JsonValueKind.Number ? sl.GetInt32() : 0,
                HouseHc = e.TryGetProperty("house_hc", out var hc) && hc.ValueKind == JsonValueKind.Number ? hc.GetUInt64() : 0
            };
            if (h.HouseHc != 0) db._byHc[h.HouseHc] = h;
            if (h.Id != 0) db._byId[h.Id] = h;
        }
        return db;
    }
}

/// <summary>
/// Parses the game's big database.json (embedded as a resource). Only the tables
/// that are actually needed are materialized, so the 25 MB file is streamed and
/// unrelated tables are skipped without allocating objects for them.
/// </summary>
public sealed class GameDatabase
{
    public static GameDatabase Empty { get; } = new();

    private readonly Dictionary<int, string> _brandNames = new();
    private readonly List<RawCar> _rawCars = new();

    public CarsDatabase Cars { get; } = new();

    public IReadOnlyDictionary<int, string> BrandNames => _brandNames;

    private GameDatabase() { }

    public static GameDatabase LoadEmbedded()
    {
        var asm = typeof(GameDatabase).Assembly;
        string? res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("database.json", StringComparison.OrdinalIgnoreCase));
        if (res == null) return new GameDatabase();
        using var s = asm.GetManifestResourceStream(res);
        return s == null ? new GameDatabase() : Parse(s);
    }

    public static GameDatabase LoadFile(string path)
    {
        if (!File.Exists(path)) return new GameDatabase();
        using var s = File.OpenRead(path);
        return Parse(s);
    }

    public static GameDatabase Parse(Stream stream)
    {
        var db = new GameDatabase();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return db;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) break;
            string name = reader.GetString() ?? "";
            reader.Read(); // move to value start
            switch (name)
            {
                case "DBBrands":
                    db.ReadBrands(ref reader);
                    break;
                case "DBCarPhysicsData":
                    db.ReadCars(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        db.BuildCars();
        return db;
    }

    private void ReadBrands(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartArray) { r.Skip(); return; }
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (r.TokenType != JsonTokenType.StartObject) { r.Skip(); continue; }
            int hc = 0;
            string name = "";
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) continue;
                string key = r.GetString() ?? "";
                r.Read();
                switch (key)
                {
                    case "_hashcode": hc = (int)ReadLong(ref r); break;
                    case "_manufacturer_name": name = ReadString(ref r); break;
                    default: r.Skip(); break;
                }
            }
            _brandNames[hc] = name;
        }
    }

    private void ReadCars(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartArray) { r.Skip(); return; }
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (r.TokenType != JsonTokenType.StartObject) { r.Skip(); continue; }
            var car = new RawCar();
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) continue;
                string key = r.GetString() ?? "";
                r.Read();
                switch (key)
                {
                    case "_id_car": car.Id = (int)ReadLong(ref r); break;
                    case "_car_brand": car.Brand = (int)ReadLong(ref r); break;
                    case "_vehiclecategory": car.Category = (int)ReadLong(ref r); break;
                    case "_power_bhp": car.PowerBhp = (int)ReadLong(ref r); break;
                    case "_max_theorical_speed": car.TopSpeed = ReadDouble(ref r); break;
                    case "_acceleration_0_100_kmh": car.Acceleration = ReadDouble(ref r); break;
                    case "_price": car.Price = ReadLong(ref r); break;
                    case "_car_model": car.Model = ReadString(ref r); break;
                    case "_car_version": car.Version = ReadString(ref r); break;
                    case "_car_realname": car.RealName = ReadString(ref r); break;
                    default: r.Skip(); break;
                }
            }
            _rawCars.Add(car);
        }
    }

    private void BuildCars()
    {
        foreach (var rc in _rawCars)
        {
            string brand = _brandNames.TryGetValue(rc.Brand, out var b) ? Clean(b) : "";
            var info = new CarInfo
            {
                Id = rc.Id,
                Brand = brand,
                Model = Clean(rc.Model),
                Version = Clean(rc.Version),
                VehicleCategory = rc.Category,
                Price = rc.Price,
                PowerBhp = rc.PowerBhp,
                TopSpeed = rc.TopSpeed,
                Acceleration = rc.Acceleration
            };
            info.Name = ComposeName(brand, info.Model, info.Version, Clean(rc.RealName), rc.Id);
            Cars.Add(info);
        }
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "##") return "";
        return s.Trim().Replace("®", "").Replace("™", "").Trim();
    }

    private static string ComposeName(string brand, string model, string version, string realname, int id)
    {
        var parts = new List<string>();
        void Add(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (var p in parts)
                if (string.Equals(p, s, StringComparison.OrdinalIgnoreCase)) return;
            parts.Add(s);
        }

        Add(brand);
        Add(model);
        Add(version);
        Add(realname);

        string name = string.Join(" ", parts).Trim();
        return name.Length == 0 ? $"Fahrzeug {id}" : name;
    }

    private sealed class RawCar
    {
        public int Id;
        public int Brand;
        public int Category;
        public int PowerBhp;
        public double TopSpeed;
        public double Acceleration;
        public long Price;
        public string Model = "";
        public string Version = "";
        public string RealName = "";
    }

    // ---- primitive readers (handle the {"_PrimitiveType":..,"Value":..} wrapper) ----

    private static long ReadLong(ref Utf8JsonReader r)
    {
        if (r.TokenType == JsonTokenType.StartObject)
        {
            long v = 0;
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) continue;
                string k = r.GetString() ?? "";
                r.Read();
                if (k == "Value") v = ReadLong(ref r);
                else r.Skip();
            }
            return v;
        }
        if (r.TokenType == JsonTokenType.Number)
            return r.TryGetInt64(out var l) ? l : (long)r.GetDouble();
        if (r.TokenType == JsonTokenType.String)
            return long.TryParse(r.GetString(), out var p) ? p : 0;
        if (r.TokenType == JsonTokenType.True) return 1;
        if (r.TokenType == JsonTokenType.False) return 0;
        r.Skip();
        return 0;
    }

    private static double ReadDouble(ref Utf8JsonReader r)
    {
        if (r.TokenType == JsonTokenType.StartObject)
        {
            double v = 0;
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) continue;
                string k = r.GetString() ?? "";
                r.Read();
                if (k == "Value") v = ReadDouble(ref r);
                else r.Skip();
            }
            return v;
        }
        if (r.TokenType == JsonTokenType.Number) return r.GetDouble();
        if (r.TokenType == JsonTokenType.String)
            return double.TryParse(r.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;
        r.Skip();
        return 0;
    }

    private static string ReadString(ref Utf8JsonReader r)
    {
        if (r.TokenType == JsonTokenType.StartObject)
        {
            string v = "";
            while (r.Read() && r.TokenType != JsonTokenType.EndObject)
            {
                if (r.TokenType != JsonTokenType.PropertyName) continue;
                string k = r.GetString() ?? "";
                r.Read();
                if (k == "Value") v = ReadString(ref r);
                else r.Skip();
            }
            return v;
        }
        if (r.TokenType == JsonTokenType.String) return r.GetString() ?? "";
        if (r.TokenType == JsonTokenType.Null) return "";
        r.Skip();
        return "";
    }
}
