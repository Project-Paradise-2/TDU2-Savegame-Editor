using System.IO;
using System.Globalization;
using TDU2SaveEditor.Core;

namespace TDU2SaveEditor.Services;

public sealed class SaveFileEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required byte[] OriginalEncrypted { get; init; }
    public required byte[] Footer { get; init; }
    public required XmbfDocument Doc { get; init; }
}

public sealed class SaveGame
{
    public required string ProfileName { get; set; }
    public required string PlayerSaveDir { get; init; }
    public required Dictionary<string, SaveFileEntry> Files { get; init; }

    public SaveFileEntry Data => Files["DATA"];
    public SaveFileEntry? Keymap => Files.TryGetValue("KEYMAP", out var f) ? f : null;
    public SaveFileEntry? Options => Files.TryGetValue("OPTIONS", out var f) ? f : null;
    public XObject DataRoot => Data.Doc.Root;
}

public static class SaveGameService
{
    public static readonly string[] KnownFiles = { "DATA", "KEYMAP", "OPTIONS" };

    /// <summary>
    /// Resolves a path that may point to a PLAYERSAVE folder, a profile folder,
    /// or a savegame root, into the actual PLAYERSAVE directory (if found).
    /// </summary>
    public static string ResolvePlayerSaveDir(string path)
    {
        if (File.Exists(path) && KnownFiles.Contains(System.IO.Path.GetFileName(path).ToUpperInvariant()))
            path = System.IO.Path.GetDirectoryName(path)!;

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        if (System.IO.Path.GetFileName(path).Equals("PLAYERSAVE", StringComparison.OrdinalIgnoreCase))
            return path;

        string sub = System.IO.Path.Combine(path, "PLAYERSAVE");
        if (Directory.Exists(sub))
            return sub;

        // Maybe a savegame root containing profile subfolders
        foreach (var d in Directory.GetDirectories(path))
        {
            string ps = System.IO.Path.Combine(d, "PLAYERSAVE");
            if (Directory.Exists(ps))
                return ps;
        }

        return path;
    }

    public static string? FindDefaultSavegameDir()
    {
        try
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string p = System.IO.Path.Combine(docs, "Eden Games", "Test Drive Unlimited 2", "savegame");
            return Directory.Exists(p) ? p : null;
        }
        catch
        {
            return null;
        }
    }

    public static SaveGame Load(string path, string? profileOverride = null)
    {
        string dir = ResolvePlayerSaveDir(path);

        string? profile = profileOverride;

        // Peek at DATA to detect the profile cryptographically.
        string dataPath = System.IO.Path.Combine(dir, "DATA");
        byte[]? probe = File.Exists(dataPath) ? File.ReadAllBytes(dataPath) : null;
        if (string.IsNullOrWhiteSpace(profile))
        {
            // First try folder/ProfileList candidates, then fall back to reading the name from the decrypted driver.
            profile = ProfileDetector.Detect(dir, probe, "DATA");
        }

        var files = new Dictionary<string, SaveFileEntry>(StringComparer.Ordinal);
        foreach (var name in KnownFiles)
        {
            string fp = System.IO.Path.Combine(dir, name);
            if (!File.Exists(fp)) continue;

            byte[] raw = File.ReadAllBytes(fp);
            DecryptedSave dec = SafeDecrypt(raw, profile, name);
            XmbfDocument doc = Xmbf.Decode(dec.Xmbf);

            files[name] = new SaveFileEntry
            {
                Name = name,
                Path = fp,
                OriginalEncrypted = raw,
                Footer = dec.Footer,
                Doc = doc
            };

            if (name == "DATA" && string.IsNullOrWhiteSpace(profileOverride))
            {
                // Autoritative: the driver name inside the container.
                var driver = doc.Root["Driver"] as XObject;
                var nameVal = (driver?["Name"] as XPrim)?.Value as string;
                if (!string.IsNullOrWhiteSpace(nameVal))
                    profile = nameVal;
            }
        }

        if (!files.ContainsKey("DATA"))
            throw new InvalidDataException("Keine DATA-Savegame-Datei gefunden.");

        return new SaveGame
        {
            ProfileName = profile ?? "Player",
            PlayerSaveDir = dir,
            Files = files
        };
    }

    private static DecryptedSave SafeDecrypt(byte[] raw, string? profile, string name)
    {
        // Try the supplied profile first; if that fails, brute force against footer ID.
        try
        {
            return SaveCrypto.Decrypt(raw, profile, name);
        }
        catch when (profile != null)
        {
            return SaveCrypto.Decrypt(raw, null, name);
        }
    }

    public static void Save(SaveGame save, bool createBackup = true)
    {
        string? backupDir = null;
        if (createBackup)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            backupDir = System.IO.Path.Combine(save.PlayerSaveDir, "Backups", save.ProfileName);
            Directory.CreateDirectory(backupDir);
            foreach (var entry in save.Files.Values)
                File.WriteAllBytes(System.IO.Path.Combine(backupDir, $"{entry.Name}.{stamp}.bak"), entry.OriginalEncrypted);
        }

        foreach (var entry in save.Files.Values)
        {
            byte[] newXmbf = Xmbf.Encode(entry.Doc);
            byte[] encrypted = SaveCrypto.Encrypt(newXmbf, save.ProfileName, entry.Name, entry.Footer);

            // Atomic write.
            string tmp = entry.Path + ".tmp";
            File.WriteAllBytes(tmp, encrypted);
            if (File.Exists(entry.Path)) File.Delete(entry.Path);
            File.Move(tmp, entry.Path);
        }

        // Refresh the originals so an unchanged re-save stays identical.
        foreach (var entry in save.Files.Values)
        {
            byte[] raw = File.ReadAllBytes(entry.Path);
            DecryptedSave dec = SaveCrypto.Decrypt(raw, save.ProfileName, entry.Name);
            save.Files[entry.Name] = new SaveFileEntry
            {
                Name = entry.Name,
                Path = entry.Path,
                OriginalEncrypted = raw,
                Footer = dec.Footer,
                Doc = entry.Doc
            };
        }

        _ = backupDir;
    }
}
