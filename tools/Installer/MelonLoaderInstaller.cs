using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Installer;

/// <summary>
/// Installs MelonLoader into the game folder if it isn't already present, downloading
/// whichever architecture (x86/x64) matches the game's executable. Mirrors the approach
/// verified against LavaGang's own MelonLoader.Installer (MLVersion.ReadFromPE /
/// GameManager.GetGameArchitecture): read the PE header's Machine field, falling back to
/// UnityPlayer.dll next to the exe if that's inconclusive.
/// </summary>
public static class MelonLoaderInstaller
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/LavaGang/MelonLoader/releases/latest";
    private const string FallbackTag = "v0.7.3";
    private const string FallbackBaseUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.3/";

    public static bool IsInstalled(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "version.dll")) && Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

    public static async Task InstallAsync(string gamePath, string gameExePath, Action<string>? statusCallback = null)
    {
        var assetName = DetectAssetName(gamePath, gameExePath);
        var (downloadUrl, tag) = await ResolveDownloadUrlAsync(assetName, statusCallback);

        statusCallback?.Invoke(Strings.Get("StepMelonLoaderInstalling", tag, assetName));

        var tempZip = Path.Combine(Path.GetTempPath(), assetName);
        try
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await contentStream.CopyToAsync(fileStream);
            }

            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var destPath = Path.Combine(gamePath, entry.FullName);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* ignore cleanup errors */ }
        }
    }

    private static string DetectAssetName(string gamePath, string gameExePath)
    {
        var machine = ReadPeMachine(gameExePath);
        if (machine == Machine.Unknown)
        {
            var unityPlayer = Path.Combine(gamePath, "UnityPlayer.dll");
            if (File.Exists(unityPlayer)) machine = ReadPeMachine(unityPlayer);
        }

        return machine switch
        {
            Machine.I386 => "MelonLoader.x86.zip",
            Machine.Amd64 => "MelonLoader.x64.zip",
            _ => "MelonLoader.x64.zip",
        };
    }

    private static Machine ReadPeMachine(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            using var pe = new PEReader(fs);
            return pe.PEHeaders.CoffHeader.Machine;
        }
        catch
        {
            return Machine.Unknown;
        }
    }

    private static async Task<(string Url, string Tag)> ResolveDownloadUrlAsync(string assetName, Action<string>? statusCallback)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumInstaller/1.0");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var response = await httpClient.GetAsync(LatestReleaseApiUrl);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString();
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == assetName)
                {
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(tag))
                    {
                        return (url, tag);
                    }
                }
            }

            throw new Exception($"Latest release did not contain a '{assetName}' asset.");
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke(Strings.Get("StepMelonLoaderFallback", ex.Message, FallbackTag));
            return (FallbackBaseUrl + assetName, FallbackTag);
        }
    }
}
