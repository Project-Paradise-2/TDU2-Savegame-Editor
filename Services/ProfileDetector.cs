using System.IO;
using System.Text;

namespace TDU2SaveEditor.Services;

internal static class ProfileDetector
{
    /// <summary>
    /// Detects the profile name from the folder hierarchy, ProfileList.dat, and
    /// finally by cryptographic trial against the encrypted DATA/KEYMAP/OPTIONS.
    /// </summary>
    public static string Detect(string playerSaveDir, byte[]? probeFile = null, string? probeName = null)
    {
        var candidates = new List<string>();

        try
        {
            var dir = new DirectoryInfo(playerSaveDir);
            // .../<ProfileName>/PLAYERSAVE  -> profile is the parent of PLAYERSAVE
            if (dir.Name.Equals("PLAYERSAVE", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
            {
                string name = dir.Parent.Name;
                if (!IsGeneric(name)) candidates.Add(name);
            }
        }
        catch { }

        // ProfileList.dat in ancestor folders
        try
        {
            var search = new DirectoryInfo(playerSaveDir);
            for (int i = 0; i < 4 && search != null; i++, search = search.Parent)
            {
                string pl = Path.Combine(search.FullName, "ProfileList.dat");
                if (File.Exists(pl))
                {
                    foreach (var p in ReadProfileList(pl))
                        if (!candidates.Contains(p)) candidates.Add(p);
                }
            }
        }
        catch { }

        if (probeFile != null && probeName != null)
        {
            foreach (var c in candidates)
                if (Core.SaveCrypto.MatchesProfile(probeFile, probeName, c))
                    return c;
        }

        if (candidates.Count > 0)
            return candidates[0];

        return "Player";
    }

    public static List<string> ReadProfileList(string path)
    {
        var result = new List<string>();
        try
        {
            byte[] data = File.ReadAllBytes(path);
            const int recordSize = 257;
            int count = Math.Max(0, (data.Length - 10) / recordSize);
            for (int r = 0; r < count; r++)
            {
                int off = 10 + r * recordSize;
                int end = off;
                int max = off + 256;
                while (end < max && end < data.Length && data[end] != 0) end++;
                string name = Encoding.Latin1.GetString(data, off, end - off);
                if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name))
                    result.Add(name);
            }
        }
        catch { }
        return result;
    }

    private static bool IsGeneric(string s)
        => s.Equals("savegame", StringComparison.OrdinalIgnoreCase)
           || s.Equals("Test Drive Unlimited 2", StringComparison.OrdinalIgnoreCase)
           || s.Equals("Eden Games", StringComparison.OrdinalIgnoreCase);
}
