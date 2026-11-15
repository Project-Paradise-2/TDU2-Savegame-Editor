using System.IO;
using TDU2SaveEditor.Core;

namespace TDU2SaveEditor.Services;

/// <summary>
/// A sidecar asset (photo, licence image, avatar) that lives next to the
/// PLAYERSAVE container inside the profile folder. The payload is decrypted
/// lazily on first access so listing a large gallery stays instant.
/// </summary>
public sealed class SaveAsset
{
    public required string Category { get; init; }
    public required string ContainerName { get; init; }
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required long EncryptedSize { get; init; }
    public required IReadOnlyList<string> Profiles { get; init; }

    private DecryptedSave? _dec;
    private bool _decodeFailed;

    /// <summary>Profile name that successfully decrypted this asset.</summary>
    public string? WorkingProfile { get; private set; }

    public byte[]? Footer => _dec?.Footer;
    public string? ContainerFileName => _dec?.FileName;

    private bool EnsureDecoded()
    {
        if (_dec != null) return true;
        if (_decodeFailed) return false;

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(FilePath);
        }
        catch
        {
            _decodeFailed = true;
            return false;
        }

        foreach (var profile in Profiles)
        {
            try
            {
                _dec = SaveCrypto.Decrypt(raw, profile, null, ContainerName);
                WorkingProfile = profile;
                return true;
            }
            catch
            {
                // try the next candidate
            }
        }

        _decodeFailed = true;
        return false;
    }

    /// <summary>
    /// Decrypts and returns the raw payload, or null if it cannot be read with
    /// any of the candidate profile names. <paramref name="isImage"/> reports
    /// whether the payload is a JPEG image.
    /// </summary>
    public byte[]? TryDecode(out bool isImage)
    {
        isImage = false;
        if (!EnsureDecoded())
            return null;

        var bytes = _dec!.Xmbf;
        isImage = bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        return bytes;
    }

    /// <summary>
    /// Parses the decrypted payload as a .2DB/BMAP texture, or null if it is not
    /// one (e.g. a JPEG photo) or cannot be read.
    /// </summary>
    public StickerImage? TryDecodeSticker()
    {
        if (!EnsureDecoded())
            return null;
        var bytes = _dec!.Xmbf;
        return Sticker2Db.IsSticker(bytes) && Sticker2Db.TryParse(bytes, out var img) ? img : null;
    }

    public override string ToString() => Name;

    public string Display => $"{Name}  ({EncryptedSize / 1024:N0} KB)";
}

/// <summary>
/// Reads the auxiliary encrypted files (PHOTOS, LICENCES, AVATAR, STICKERS) that
/// share the TDU2 container format. Only the key context differs: instead of the
/// literal "PLAYERSAVE" token, the physical folder name is used as the key middle part.
/// PHOTOS/LICENCES/AVATAR payloads are JPEGs; STICKERS payloads are .2DB/BMAP
/// textures (decoded via <see cref="TDU2SaveEditor.Core.Sticker2Db"/>).
/// </summary>
public static class AssetsService
{
    private static readonly string[] ImageContainers = { "PHOTOS", "LICENCES", "AVATAR", "STICKERS" };

    public static List<SaveAsset> Scan(string playerSaveDir, string profileName)
    {
        var result = new List<SaveAsset>();

        string full = Path.GetFullPath(playerSaveDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? profileDir = Path.GetDirectoryName(full);
        if (profileDir == null || !Directory.Exists(profileDir))
            return result;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(profileName))
            candidates.Add(profileName);
        string folder = Path.GetFileName(profileDir);
        if (!string.IsNullOrWhiteSpace(folder) && !candidates.Contains(folder))
            candidates.Add(folder);

        foreach (var container in ImageContainers)
        {
            string dir = Path.Combine(profileDir, container);
            if (!Directory.Exists(dir))
                continue;

            var info = new DirectoryInfo(dir);
            foreach (var f in info.GetFiles())
            {
                if (IsBackup(f.Name))
                    continue;

                result.Add(new SaveAsset
                {
                    Category = container,
                    ContainerName = info.Name,
                    Name = f.Name,
                    FilePath = f.FullName,
                    EncryptedSize = f.Length,
                    Profiles = candidates
                });
            }
        }

        result.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Category, b.Category);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        return result;
    }

    private static bool IsBackup(string name) =>
        name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(".backup", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dec", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dec2", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".oth", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase);
}
