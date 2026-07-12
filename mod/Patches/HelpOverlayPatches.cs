using System;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using AccessibilityMod.Utils;

namespace AccessibilityMod.Patches
{
    /// <summary>
    /// Reads the game's control-help overlay (PauseHelp) aloud when it is shown. The
    /// overlay is a purely visual panel of key hints with no focusable elements, so the
    /// generic UI announcement path never sees it - without this patch it is completely
    /// silent. All TextMeshPro labels under the currently active panel are collected and
    /// spoken as one queued announcement.
    /// </summary>
    [HarmonyPatch]
    public static class HelpOverlayPatches
    {
        [HarmonyPatch(typeof(PauseHelp), "Toggle")]
        [HarmonyPostfix]
        public static void PauseHelp_Toggle_Postfix(PauseHelp __instance)
        {
            AnnounceHelpState(__instance);
        }

        [HarmonyPatch(typeof(PauseHelp), "Show")]
        [HarmonyPostfix]
        public static void PauseHelp_Show_Postfix(PauseHelp __instance)
        {
            AnnounceHelpState(__instance);
        }

        private static bool lastAnnouncedVisible;

        private static void AnnounceHelpState(PauseHelp instance)
        {
            try
            {
                if (instance == null) return;

                bool visible = instance.visible;
                if (visible == lastAnnouncedVisible) return;
                lastAnnouncedVisible = visible;

                if (!visible)
                {
                    TolkScreenReader.Instance.Speak("Help closed", true);
                    return;
                }

                var text = CollectPanelText(instance);
                TolkScreenReader.Instance.Speak(
                    string.IsNullOrWhiteSpace(text) ? "Help opened" : $"Help. {text}",
                    true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error announcing help overlay: {ex}");
            }
        }

        private static string CollectPanelText(PauseHelp instance)
        {
            var sb = new StringBuilder();
            AppendTexts(sb, instance.keyboardPanel);
            AppendTexts(sb, instance.tipsPanel);
            return sb.ToString();
        }

        private static void AppendTexts(StringBuilder sb, UnityEngine.GameObject panel)
        {
            if (panel == null || !panel.activeInHierarchy) return;

            var labels = panel.GetComponentsInChildren<Il2CppTMPro.TextMeshProUGUI>();
            if (labels == null) return;

            foreach (var label in labels)
            {
                if (label == null || string.IsNullOrWhiteSpace(label.text)) continue;
                sb.Append(RTLHelper.FixForScreenReader(label.text.Trim()));
                sb.Append(". ");
            }
        }
    }
}
