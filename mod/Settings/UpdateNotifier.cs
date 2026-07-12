using System;
using System.Net.Http;
using System.Threading.Tasks;
using MelonLoader;

namespace AccessibilityMod.Settings
{
    /// <summary>
    /// Checks GitHub once at startup (background thread) whether a newer mod build
    /// exists and announces it once the speech system is ready. Stable builds
    /// (vX.Y.Z) compare against the latest release tag; nightly builds against the
    /// nightly channel's mod-version.txt. Dev builds skip the check. Failures stay
    /// silent - an update hint is a courtesy, never an error.
    /// </summary>
    public static class UpdateNotifier
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/danijel1124/Disco-A11y/releases/latest";
        private const string NightlyModVersionUrl = "https://github.com/danijel1124/Disco-A11y/releases/download/nightly/mod-version.txt";

        private static volatile string announcement;
        private static bool announced;

        public static void Initialize(string currentVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || currentVersion == "dev") return;
            Task.Run(() => CheckAsync(currentVersion));
        }

        private static async Task CheckAsync(string currentVersion)
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscoElysiumAccessibilityMod/1.0");
                http.Timeout = TimeSpan.FromSeconds(15);

                string latest;
                if (currentVersion.StartsWith("nightly-"))
                {
                    latest = (await http.GetStringAsync(NightlyModVersionUrl)).Trim();
                }
                else
                {
                    var json = await http.GetStringAsync(LatestReleaseUrl);
                    var marker = "\"tag_name\":\"";
                    var start = json.IndexOf(marker, StringComparison.Ordinal);
                    if (start < 0) return;
                    start += marker.Length;
                    latest = json.Substring(start, json.IndexOf('"', start) - start);
                }

                if (IsNewer(latest, currentVersion))
                {
                    announcement = Loc.Get("UpdateAvailable", latest);
                    MelonLogger.Msg($"[UPDATE] Newer build available: {latest} (running {currentVersion})");
                }
                else
                {
                    MelonLogger.Msg($"[UPDATE] Up to date (running {currentVersion}, published {latest})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[UPDATE] Check skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// True only when the published build is actually newer than the running one.
        /// Comparing for mere inequality announced an "update" to anyone running a build
        /// newer than the channel's - which every local dev build is. Nightly versions are
        /// "nightly-yyyyMMddHHmmss" timestamps, so they order correctly as plain strings;
        /// for stable tags an unequal tag is treated as newer (releases only move forward).
        /// </summary>
        private static bool IsNewer(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest) || latest == current) return false;

            const string prefix = "nightly-";
            if (latest.StartsWith(prefix) && current.StartsWith(prefix))
            {
                return string.CompareOrdinal(latest, current) > 0;
            }

            return true;
        }

        /// <summary>Called from OnUpdate; speaks the pending hint once, queued.</summary>
        public static void Update()
        {
            if (announced || announcement == null) return;
            announced = true;
            TolkScreenReader.Instance.Speak(announcement, false, AnnouncementCategory.Queueable);
        }
    }
}
