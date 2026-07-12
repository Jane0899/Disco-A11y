using System.Diagnostics;

namespace Installer;

/// <summary>
/// MelonLoader hosts the mod on the system-wide .NET 6 runtime - without it the game
/// fails on first modded launch with a cryptic error. The installer checks for it and
/// can download and silently run Microsoft's official runtime installer.
/// </summary>
public static class DotNetRuntime
{
    private const string DownloadUrl = "https://aka.ms/dotnet/6.0/dotnet-runtime-win-x64.exe";

    public static bool IsModRuntimePresent()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            var shared = Path.Combine(root, "dotnet", "shared", "Microsoft.NETCore.App");
            if (Directory.Exists(shared) && Directory.GetDirectories(shared, "6.*").Length > 0)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task<bool> InstallAsync(Action<string> log)
    {
        var temp = Path.Combine(Path.GetTempPath(), "dotnet-runtime-6-win-x64.exe");

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
