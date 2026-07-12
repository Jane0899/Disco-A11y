using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace AccessibilityMod.Settings
{
    /// <summary>
    /// Warns when a mod hotkey collides with one of the game's own keyboard bindings.
    /// Reads the live InControl action set (CrossPlatformInputManager.InputActions) at
    /// runtime, so it sees the player's in-game rebinds too, not just the defaults.
    /// Runs once shortly after the first scene loads; on any interop failure it only
    /// logs and stays silent - a missed warning is better than a wrong one.
    ///
    /// Only unmodified mod bindings are checked: the game ignores our Ctrl/Alt
    /// requirements, but a mod action on plain X genuinely shares the key press with a
    /// game action on X, which is the everyday collision worth surfacing.
    /// </summary>
    public static class GameKeybindConflictChecker
    {
        private const int MAX_ATTEMPTS = 5;
        private static int attempts;
        private static bool done;

        /// <summary>
        /// Called on every scene load; retries until the game's input actions exist
        /// (they may not on the very first scenes), then never runs again.
        /// </summary>
        public static void RunOnce()
        {
            if (done || attempts >= MAX_ATTEMPTS) return;
            attempts++;

            try
            {
                var gameBindings = ReadGameKeyboardBindings();
                if (gameBindings.Count == 0)
                {
                    MelonLogger.Msg($"[KEYBIND CONFLICTS] Game bindings not readable yet (attempt {attempts}/{MAX_ATTEMPTS})");
                    return;
                }

                done = true;
                DumpGameBindings(gameBindings);
                AnnounceConflicts(gameBindings);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[KEYBIND CONFLICTS] Check failed: {ex.Message}");
            }
        }

        /// <summary>Map of normalized key name -> game action names bound to it.</summary>
        private static Dictionary<string, List<string>> ReadGameKeyboardBindings()
        {
            var result = new Dictionary<string, List<string>>();

            var inputManager = UnityEngine.Object.FindObjectOfType<Il2Cpp.CrossPlatformInputManager>();
            var actionSet = inputManager?.InputActions;
            if (actionSet?.actions == null) return result;

            foreach (var action in actionSet.actions)
            {
                if (action == null) continue;

                // regularBindings is the live list behind the read-only Bindings wrapper
                // (which Il2Cpp interop can't foreach over) and includes in-game rebinds.
                var bindings = action.regularBindings;
                if (bindings == null) continue;

                for (int i = 0; i < bindings.Count; i++)
                {
                    var binding = bindings[i];
                    if (binding == null) continue;
                    // Only keyboard bindings matter; controller bindings can't collide
                    // with our keyboard hotkeys.
                    if (binding.BindingSourceType != Il2CppInControl.BindingSourceType.KeyBindingSource) continue;

                    var keyName = Normalize(binding.Name);
                    if (keyName.Length == 0) continue;

                    if (!result.TryGetValue(keyName, out var actions))
                    {
                        actions = new List<string>();
                        result[keyName] = actions;
                    }
                    actions.Add(action.Name);
                }
            }

            return result;
        }

        private static void DumpGameBindings(Dictionary<string, List<string>> gameBindings)
        {
            var sb = new StringBuilder("[KEYBIND CONFLICTS] Game keyboard bindings: ");
            foreach (var kvp in gameBindings)
            {
                sb.Append($"{kvp.Key}=({string.Join("/", kvp.Value)}) ");
            }
            MelonLogger.Msg(sb.ToString());
        }

        private static void AnnounceConflicts(Dictionary<string, List<string>> gameBindings)
        {
            var conflicts = new List<string>();

            foreach (var kvp in KeyBindings.Defaults)
            {
                var binding = KeyBindings.Get(kvp.Key);
                if (binding.RequireCtrl || binding.RequireAlt || binding.RequireShift) continue;

                var keyName = Normalize(binding.Key.ToString());
                if (gameBindings.TryGetValue(keyName, out var gameActions))
                {
                    conflicts.Add($"{binding.Key} ({kvp.Key} vs game: {string.Join(", ", gameActions)})");
                }
            }

            if (conflicts.Count == 0)
            {
                MelonLogger.Msg("[KEYBIND CONFLICTS] No collisions between mod hotkeys and game keys");
                return;
            }

            MelonLogger.Warning("[KEYBIND CONFLICTS] Mod hotkeys sharing a key with game controls: " + string.Join("; ", conflicts));
            // Spoken summary stays short - reading all collisions aloud on every launch
            // would take ages; the full list is in the MelonLoader log.
            TolkScreenReader.Instance.Speak(
                $"Note: {conflicts.Count} mod hotkeys share a key with game controls. See the MelonLoader log for details.",
                false);
        }

        /// <summary>
        /// InControl names keys like "Left Bracket" while Unity's KeyCode says
        /// "LeftBracket" - compare on lowercase alphanumerics only. Beyond spacing, the
        /// two also use different vocabulary for whole key families (verified against a
        /// live binding dump): InControl says "pad1"/"padminus" where Unity says
        /// "Keypad1"/"KeypadMinus", and "num8" where Unity says "Alpha8".
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }

            var result = sb.ToString();
            if (result.StartsWith("keypad")) return "pad" + result.Substring("keypad".Length);
            if (result.StartsWith("alpha")) return "num" + result.Substring("alpha".Length);
            return result;
        }
    }
}
