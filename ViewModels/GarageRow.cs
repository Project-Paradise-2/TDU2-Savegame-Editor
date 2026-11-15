using TDU2SaveEditor.Core;
using TDU2SaveEditor.Services;

namespace TDU2SaveEditor.ViewModels;

public sealed class GarageRow : ObservableBase
{
    private readonly Action _onChanged;

    public GarageRow(int slot, XObject entry, Action onChanged)
    {
        Slot = slot;
        Entry = entry;
        _onChanged = onChanged;
        Car = entry["Car"] as XObject ?? new XObject();
    }

    public int Slot { get; }
    public XObject Entry { get; }
    public XObject Car { get; }

    public int Archetype
    {
        get => (int)Editing.GetLong(Car, "Archetype");
        set
        {
            Editing.SetLong(Car, "Archetype", value);
            Raise();
            Raise(nameof(CarName));
            Raise(nameof(IsEmpty));
            _onChanged();
        }
    }

    public ulong CarId => Editing.GetULong(Car, "ID");

    public bool IsEmpty => CarId == 0xFFFFFFFFUL || CarId == 0UL || Archetype == 0;

    public string CarName
    {
        get => IsEmpty ? Services.Loc.T("empty") : App.Cars.NameOf(Archetype);
        set
        {
            var car = App.Cars.FindByName(value);
            if (car != null && car.Id != Archetype)
                Archetype = car.Id;
        }
    }

    public string Brand => IsEmpty ? "" : (App.Cars.Get(Archetype)?.Brand ?? "");

    public ulong HouseHc => Editing.GetULong(Entry, "House");

    public string HouseName
    {
        get => App.Houses.NameOfHouse(HouseHc);
        set
        {
            var house = App.Houses.FindByName(value);
            if (house != null && house.HouseHc != HouseHc)
            {
                Editing.SetULong(Entry, "House", house.HouseHc);
                Raise();
                _onChanged();
            }
        }
    }

    public int IndexInGarage
    {
        get => (int)Editing.GetLong(Entry, "IndexInGarage");
        set { Editing.SetLong(Entry, "IndexInGarage", value); Raise(); _onChanged(); }
    }

    private XObject Upgrades => Car["Car Upgrades"] as XObject ?? new XObject();

    public int Acc
    {
        get => (int)Editing.GetLong(Upgrades, "Acceleration");
        set { Editing.SetLong(Upgrades, "Acceleration", value); Raise(); _onChanged(); }
    }

    public int Top
    {
        get => (int)Editing.GetLong(Upgrades, "Top Speed");
        set { Editing.SetLong(Upgrades, "Top Speed", value); Raise(); _onChanged(); }
    }

    public int Brk
    {
        get => (int)Editing.GetLong(Upgrades, "Braking");
        set { Editing.SetLong(Upgrades, "Braking", value); Raise(); _onChanged(); }
    }

    public ulong Vin
    {
        get => Editing.GetULong(Car, "VIN");
        set { Editing.SetULong(Car, "VIN", value); Raise(); _onChanged(); }
    }

    public int Status
    {
        get => (int)Editing.GetLong(Car, "Status");
        set { Editing.SetLong(Car, "Status", value); Raise(); _onChanged(); }
    }

    public int Paint
    {
        get => (int)Editing.GetLong(Car, "PaintIndice");
        set { Editing.SetLong(Car, "PaintIndice", value); Raise(); _onChanged(); }
    }

    public double Odometer
    {
        get => (Car["OdometerKms"] as XPrim)?.AsDouble() ?? 0;
        set
        {
            Editing.SetFloat(Car, "OdometerKms", (float)value);
            Raise();
            Raise(nameof(OdometerText));
            _onChanged();
        }
    }

    public string OdometerText => IsEmpty ? "" : Odometer.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";

    public int Stage => IsEmpty ? 0 : Math.Max(Acc, Math.Max(Top, Brk));

    public bool HasSticker => Editing.GetBool(Car, "HasSticker");

    public string StickerText => !IsEmpty && HasSticker ? "Ja" : "";
}
