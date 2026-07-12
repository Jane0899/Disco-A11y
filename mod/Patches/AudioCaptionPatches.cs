using System;
using System.Collections.Generic;
using HarmonyLib;
using AccessibilityMod.Settings;
using AccessibilityMod.Utils;
using MelonLoader;

namespace AccessibilityMod.Patches
{
    /// <summary>
    /// Speaks the game's own audio-accessibility captions. Disco Elysium ships a small
    /// built-in Accessibility namespace (aimed at deaf players) that renders short text
    /// captions for notable sound effects. Those caption strings are exactly the kind of
    /// "what was that sound?" context a blind player otherwise has to guess at, so this
    /// forwards them to the screen reader as well. The game only produces captions while
    /// AudioCaptionsManager.UseCaptions is on, so we force it on while the mod setting
    /// is enabled (the visual captions it also enables don't hurt).
    /// </summary>
    [HarmonyPatch]
    public static class AudioCaptionPatches
    {
        private static readonly Dictionary<string, float> recentCaptions = new Dictionary<string, float>();
        private const float DUPLICATE_COOLDOWN = 2.0f; // identical caption within 2s = same sound burst, skip

        /// <summary>Called from OnSceneWasLoaded so the game actually generates captions.</summary>
        public static void EnsureCaptionsEnabled()
        {
            try
            {
                if (AccessibilityPreferences.GetSpeakAudioCaptions())
                {
                    Il2CppAccessibility.AudioCaptionsManager.UseCaptions = true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[AUDIO CAPTIONS] Could not enable the game's caption system: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Il2Cpp.AudioCaptionsElement), "ShowCaption")]
        [HarmonyPostfix]
        public static void AudioCaptionsElement_ShowCaption_Postfix(string captionText)
        {
            try
            {
                if (!AccessibilityPreferences.GetSpeakAudioCaptions() || string.IsNullOrWhiteSpace(captionText))
                {
                    return;
                }

                float now = UnityEngine.Time.unscaledTime;
                if (recentCaptions.TryGetValue(captionText, out var lastSpoken) && now - lastSpoken < DUPLICATE_COOLDOWN)
                {
                    return;
                }
                recentCaptions[captionText] = now;
                CleanupOldEntries(now);

                // Never interrupt: sound captions are ambient context and must not cut off
                // dialogue or navigation announcements.
                TolkScreenReader.Instance.Speak(RTLHelper.FixForScreenReader(captionText), false);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error speaking audio caption: {ex}");
            }
        }

        private static void CleanupOldEntries(float now)
        {
            if (recentCaptions.Count < 32) return;

            var stale = new List<string>();
            foreach (var kvp in recentCaptions)
            {
                if (now - kvp.Value > DUPLICATE_COOLDOWN) stale.Add(kvp.Key);
            }
            foreach (var key in stale) recentCaptions.Remove(key);
        }
    }
}
