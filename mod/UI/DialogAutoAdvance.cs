using System;
using AccessibilityMod.Patches;
using AccessibilityMod.Settings;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace AccessibilityMod.UI
{
    /// <summary>
    /// "Autoread": automatically presses the game's continue button once the screen
    /// reader has finished the current dialogue line, so conversations flow like an
    /// audiobook instead of requiring a keypress per line. Drives the game's own
    /// SunshineContinueButton singleton, so it inherits all of the game's rules about
    /// when continuing is allowed (ContState.DISABLED while checks run, hidden while
    /// response options are up - auto-advance simply pauses there and the player picks
    /// a response as usual).
    /// </summary>
    public static class DialogAutoAdvance
    {
        // Grace period after speech ends before continuing, so consecutive
        // announcements (dialogue + orb + notification) don't get chopped up.
        private const float POST_SPEECH_DELAY = 0.6f;
        // Hard floor between two auto-continues, even if speech state misbehaves.
        private const float MIN_CLICK_INTERVAL = 1.0f;

        private static bool enabled;
        private static bool loadedFromPrefs;
        private static int lastContinuedLine = -1;
        private static float lastSpeechTime;
        private static float lastClickTime;

        public static bool Enabled
        {
            get
            {
                if (!loadedFromPrefs)
                {
                    enabled = AccessibilityPreferences.GetDialogAutoAdvance();
                    loadedFromPrefs = true;
                }
                return enabled;
            }
        }

        public static void Toggle()
        {
            var newValue = !Enabled;
            enabled = newValue;
            AccessibilityPreferences.SetDialogAutoAdvance(newValue);

            string announcement;
            if (!newValue)
            {
                announcement = "Auto advance disabled";
            }
            else if (!DialogStateManager.ShouldReadFullDialog)
            {
                announcement = "Auto advance enabled. Note: it only runs while dialog reading is set to full text.";
            }
            else
            {
                announcement = "Auto advance enabled";
            }

            TolkScreenReader.Instance.Speak(announcement, true);
            MelonLogger.Msg($"[AUTO ADVANCE] {(newValue ? "Enabled" : "Disabled")}");
        }

        /// <summary>Called every frame from AccessibilityMod.OnUpdate.</summary>
        public static void Update()
        {
            try
            {
                if (!Enabled) return;
                // Only meaningful when full lines are being read - in speaker-only or
                // disabled mode auto-advancing would silently skip unread text.
                if (!DialogStateManager.ShouldReadFullDialog) return;
                if (!DialogStateManager.IsInConversation()) return;

                // Never continue the same line twice; wait for the next one to render.
                if (DialogSystemPatches.LineCounter == lastContinuedLine) return;

                if (TolkScreenReader.Instance.IsSpeaking())
                {
                    lastSpeechTime = Time.unscaledTime;
                    return;
                }
                if (Time.unscaledTime - lastSpeechTime < POST_SPEECH_DELAY) return;
                if (Time.unscaledTime - lastClickTime < MIN_CLICK_INTERVAL) return;

                var continueButton = SunshineContinueButton.instance;
                var button = continueButton?.buttonComponent;
                if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) return;
                if (continueButton.State == ContState.DISABLED) return;

                lastContinuedLine = DialogSystemPatches.LineCounter;
                lastClickTime = Time.unscaledTime;
                button.onClick.Invoke();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[AUTO ADVANCE] Error: {ex}");
            }
        }
    }
}
