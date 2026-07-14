using System;
using System.Reflection;
using MelonLoader;

// The COM SAPI calls are Windows-only; so is the game and MelonLoader, so this whole mod is.
#pragma warning disable CA1416

namespace AccessibilityMod
{
    /// <summary>
    /// A second voice, separate from the screen reader.
    ///
    /// The screen reader is a single channel, and the mod floods it: every navigation
    /// announcement, every keypress echo, every menu move interrupts the one before. Orb
    /// text - the atmospheric speech bubbles over the radio and the skills - was going out
    /// on that same channel and getting shredded by the next input before a word of it
    /// landed. So it moves to its own channel: Windows' built-in SAPI voice, which plays in
    /// parallel with NVDA/JAWS instead of fighting them for the one output. (This is the same
    /// separation the mod's own developer TTS relies on.)
    ///
    /// Late-bound COM (SAPI.SpVoice) on purpose: it needs no extra DLL shipped with the mod
    /// and no COM reference, and SAPI is present on every Windows install. Speech is async so
    /// it never blocks the game's frame, and lines queue on this voice rather than cutting
    /// each other off - orb text is ambient, not urgent.
    /// </summary>
    public static class SapiVoice
    {
        // ISpeechVoice::Speak flags.
        private const int SVSFlagsAsync = 1;
        private const int SVSFPurgeBeforeSpeak = 2;

        private static object voice;
        private static Type voiceType;
        private static bool tried;
        private static bool available;

        private static bool EnsureVoice()
        {
            if (tried) return available;
            tried = true;

            try
            {
                voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (voiceType == null)
                {
                    MelonLogger.Warning("[SAPI] SAPI.SpVoice not registered - orb text falls back to the screen reader.");
                    return false;
                }

                voice = Activator.CreateInstance(voiceType);
                available = voice != null;
                if (available) MelonLogger.Msg("[SAPI] Separate voice ready for orb text.");
                return available;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SAPI] Could not start a separate voice ({ex.Message}) - orb text falls back to the screen reader.");
                available = false;
                return false;
            }
        }

        /// <summary>
        /// Pushes the configured orb volume onto the voice before it speaks, so moving the
        /// slider in the configurator takes effect on the very next line without a restart.
        /// SpVoice.Volume is 0-100, same range as our setting.
        /// </summary>
        private static void ApplyVolume()
        {
            try
            {
                int volume = Settings.AccessibilityPreferences.GetOrbVolume();
                voiceType.InvokeMember("Volume", BindingFlags.SetProperty, null, voice,
                    new object[] { volume });
            }
            catch
            {
                // Non-fatal: a voice that ignores volume still speaks.
            }
        }

        /// <summary>
        /// Speaks on the separate SAPI voice. Returns false if SAPI is unavailable, so the
        /// caller can fall back to the screen reader rather than dropping the line.
        /// </summary>
        /// <param name="interrupt">Cut off whatever this voice is currently saying first.</param>
        public static bool Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!EnsureVoice()) return false;

            try
            {
                ApplyVolume();
                int flags = SVSFlagsAsync | (interrupt ? SVSFPurgeBeforeSpeak : 0);
                voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice,
                    new object[] { text, flags });
                return true;
            }
            catch (Exception ex)
            {
                // A COM hiccup must never take orb text - or the game - down; report once and
                // let the caller fall back.
                MelonLogger.Warning($"[SAPI] Speak failed ({ex.Message}) - orb text falls back to the screen reader.");
                available = false;
                return false;
            }
        }
    }
}
