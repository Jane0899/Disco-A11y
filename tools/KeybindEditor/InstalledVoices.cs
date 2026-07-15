using System.Diagnostics;

namespace KeybindEditor;

/// <summary>
/// The list of voices the orb text can speak with, for the configurator's voice dropdown.
///
/// The names are fetched from the TTS server itself (tools/TtsServer, "--voices"), not
/// enumerated here: the server is the one that will actually speak, so asking it guarantees
/// the dropdown shows exactly the voices it can use - including the natural/neural voices,
/// which only the server's WinRT engine can see. It also keeps the heavy 25 MB Windows
/// projection out of this small editor. If the server is not installed yet (the mod has not
/// been installed for this game folder), the list comes back empty and the dropdown offers
/// only the system default.
/// </summary>
public static class InstalledVoices
{
    private const string ExeName = "DiscoElysiumTtsServer.exe";

    public static List<string> ForGame(string gamePath)
    {
        var voices = new List<string>();
        var exe = FindServerExe(gamePath);
        if (exe == null) return voices;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--voices",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return voices;

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { /* best effort */ }
                return voices;
            }

            foreach (var line in output.Split('\n'))
            {
                // Each line is "DisplayName\tLanguage"; we show the display name.
                var name = line.Split('\t')[0].Trim();
                if (name.Length > 0) voices.Add(name);
            }
        }
        catch
        {
            // A missing or misbehaving server just means no voice list; the dropdown falls
            // back to the system default. Never let it break opening the configurator.
        }

        return voices;
    }

    /// <summary>Locates the TTS server exe under a game folder, or null if the mod is not installed there.</summary>
    public static string? FindServerExe(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) return null;
        string[] candidates =
        {
            Path.Combine(gamePath, "Mods", "TtsServer", ExeName),
            Path.Combine(gamePath, "TtsServer", ExeName),
            Path.Combine(gamePath, "Mods", ExeName),
            Path.Combine(gamePath, ExeName),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
