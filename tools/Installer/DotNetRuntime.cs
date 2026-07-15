using System.Diagnostics;

namespace Installer;

/// <summary>
/// One runtime covers everything the mod ships. MelonLoader hosts the mod on the system-wide
/// .NET 6 base runtime; our own WinForms tools (installer, configurator, debugger) need the
/// .NET 6 *Desktop* runtime, and the orb TTS server is net6 too. The Desktop runtime is a
/// superset of the base runtime, so installing it once satisfies all three - which is why the
/// check and the download both target the Desktop runtime, not the plain base runtime. Without
/// it the modded game fails on first launch with a cryptic error and the tools do not start.
/// </summary>
public static class DotNetRuntime
{
    private const string DownloadUrl = "https://aka.ms/dotnet/6.0/windowsdesktop-runtime-win-x64.exe";

    public static bool IsModRuntimePresent()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            // The Desktop runtime is the gate: it implies the base runtime the mod needs, and
            // it is what the WinForms tools require. A machine with only the base runtime (mod
            // works, tools do not) correctly reads as "missing" and gets the Desktop runtime.
            var shared = Path.Combine(root, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (Directory.Exists(shared) && Directory.GetDirectories(shared, "6.*").Length > 0)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task<bool> InstallAsync(Action<string> log)
    {
        var temp = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-6-win-x64.exe");

        log(Strings.Get("DotNetDownloading"));
        using (var http = new HttpClient())
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var content = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(temp);
            await content.CopyToAsync(file);
        }

        log(Strings.Get("DotNetInstalling"));
        // Official installer, silent; elevates via UAC on its own.
        using var process = Process.Start(new ProcessStartInfo(temp, "/install /quiet /norestart")
        {
            UseShellExecute = true,
        });
        if (process != null)
        {
            await process.WaitForExitAsync();
        }

        try { File.Delete(temp); } catch { }

        var ok = IsModRuntimePresent();
        log(ok ? Strings.Get("DotNetInstalled") : Strings.Get("DotNetFailed"));
        return ok;
    }
}
