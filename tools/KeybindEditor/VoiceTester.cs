using System.Diagnostics;

namespace KeybindEditor;

/// <summary>
/// Plays a one-off sample so the user can hear a voice/volume before saving. Runs the TTS
/// server's "--say" mode, which speaks one line with the exact settings passed on the command
/// line, ignoring the saved config - so it previews the selection currently in the UI, not
/// whatever is on disk, and never touches the player's real config.
/// </summary>
public static class VoiceTester
{
    public static async Task<bool> PlayAsync(string gamePath, string voice, int volume, int rate, string sampleText)
    {
        var exe = InstalledVoices.FindServerExe(gamePath);
        if (exe == null) return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe),
        };
        // ArgumentList quotes each entry itself, so voice names and the sample text keep their
        // spaces; an empty voice is passed through as the server's "system default".
        psi.ArgumentList.Add("--say");
        psi.ArgumentList.Add(volume.ToString());
        psi.ArgumentList.Add(rate.ToString());
        psi.ArgumentList.Add(voice);
        psi.ArgumentList.Add(sampleText);

        using var process = Process.Start(psi);
        if (process == null) return false;
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
