using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace AccessibilityMod.Settings
{
    public readonly struct KeyBinding
    {
        public readonly KeyCode Key;
        public readonly bool RequireCtrl;
        public readonly bool RequireAlt;
        public readonly bool RequireShift;

        public KeyBinding(KeyCode key, bool requireCtrl = false, bool requireAlt = false, bool requireShift = false)
        {
            Key = key;
            RequireCtrl = requireCtrl;
            RequireAlt = requireAlt;
            RequireShift = requireShift;
        }

        public string Serialize() => $"{Key}|{RequireCtrl}|{RequireAlt}|{RequireShift}";

        public static KeyBinding Deserialize(string value, KeyBinding fallback)
        {
            var parts = value?.Split('|');
            if (parts == null || parts.Length != 4
                || !Enum.TryParse<KeyCode>(parts[0], out var key)
                || !bool.TryParse(parts[1], out var ctrl)
                || !bool.TryParse(parts[2], out var alt)
                || !bool.TryParse(parts[3], out var shift))
            {
                return fallback;
            }

            return new KeyBinding(key, ctrl, alt, shift);
        }

        public string Describe()
        {
            var mods = "";
            if (RequireCtrl) mods += "Ctrl+";
            if (RequireAlt) mods += "Alt+";
            if (RequireShift) mods += "Shift+";
            return mods + Key;
        }
    }

    /// <summary>
    /// Configurable keyboard bindings for every accessibility hotkey. Defaults reproduce
    /// the mod's original hardcoded US-QWERTY punctuation bindings (LeftBracket, Backslash,
    /// etc.), which are bound to physical key *position* on US layouts and land on
    /// different / AltGr-gated keys on non-US layouts (e.g. German QWERTZ). The "NumpadSafe"
    /// preset rebinds the layout-sensitive actions onto the numeric keypad, whose layout is
    /// standardized worldwide, so it works regardless of the active keyboard layout.
    ///
    /// Bindings are persisted via MelonPreferences to UserData/AccessibilityMod.cfg, in the
    /// same file as the mod's other settings, under the "KeyBindings" category. An external
    /// keybind editor can rewrite that file directly (same key/value format written here);
    /// the mod re-reads it on next launch.
    /// </summary>
    public static class KeyBindings
    {
        private static MelonPreferences_Category category;
        private static readonly Dictionary<GameKey, MelonPreferences_Entry<string>> entries = new();
        private static readonly Dictionary<GameKey, KeyBinding> current = new();

        public static IReadOnlyDictionary<GameKey, KeyBinding> Defaults { get; } = new Dictionary<GameKey, KeyBinding>
        {
            [GameKey.AnnounceCurrentSelection] = new KeyBinding(KeyCode.BackQuote),
            [GameKey.ToggleSortingMode] = new KeyBinding(KeyCode.Semicolon),
            [GameKey.ScanSceneByDistance] = new KeyBinding(KeyCode.Quote),

            [GameKey.SelectNpcs] = new KeyBinding(KeyCode.LeftBracket),
            [GameKey.SelectLocations] = new KeyBinding(KeyCode.RightBracket),
            [GameKey.SelectLoot] = new KeyBinding(KeyCode.Backslash),
            [GameKey.SelectEverything] = new KeyBinding(KeyCode.Equals),

            [GameKey.FocusWaypoints] = new KeyBinding(KeyCode.LeftBracket, requireCtrl: true),
            [GameKey.CreateWaypoint] = new KeyBinding(KeyCode.LeftBracket, requireAlt: true),
            [GameKey.DeleteWaypoint] = new KeyBinding(KeyCode.RightBracket, requireAlt: true),

            [GameKey.CycleForward] = new KeyBinding(KeyCode.Period),
            [GameKey.CycleBackward] = new KeyBinding(KeyCode.Period, requireShift: true),
            [GameKey.NavigateToSelected] = new KeyBinding(KeyCode.Comma),
            [GameKey.StopMovement] = new KeyBinding(KeyCode.Slash),

            [GameKey.ToggleDialogReading] = new KeyBinding(KeyCode.Minus),
            [GameKey.RepeatDialogue] = new KeyBinding(KeyCode.R),
            [GameKey.ToggleOrbAnnouncements] = new KeyBinding(KeyCode.Alpha0),
            [GameKey.ToggleSpeechInterrupt] = new KeyBinding(KeyCode.Alpha8),
            [GameKey.ToggleDiagnostics] = new KeyBinding(KeyCode.Alpha9, requireCtrl: true),

            [GameKey.AnnounceStatus] = new KeyBinding(KeyCode.H),
            [GameKey.AnnounceStats] = new KeyBinding(KeyCode.X),
            [GameKey.AnnounceOfficerProfile] = new KeyBinding(KeyCode.O),
            [GameKey.ReadSkillDescription] = new KeyBinding(KeyCode.N),
            [GameKey.AnnounceKimStatus] = new KeyBinding(KeyCode.K),
        };

        /// <summary>
        /// Layout-independent alternative: moves every punctuation-bound action onto the
        /// numeric keypad, whose physical layout does not change between keyboard layouts
        /// (unlike '[', ']', '\', ';', '\'', '=', which move or require AltGr outside US
        /// layouts). Letter/digit bindings (R, H, X, O, N, K, Alpha0/8/9) are already
        /// layout-safe and are left unchanged. Where stardew-access
        /// (github.com/stardew-access/stardew-access, docs/keybindings.md) has an
        /// equivalent action, this preset reuses its convention instead of the numpad, since
        /// those keys (Escape, Page Up/Down, Ctrl+Home) are both layout-safe *and* already
        /// familiar to players of that mod, and also work on keyboards without a numpad.
        /// </summary>
        public static IReadOnlyDictionary<GameKey, KeyBinding> NumpadSafePreset { get; } = new Dictionary<GameKey, KeyBinding>
        {
            [GameKey.AnnounceCurrentSelection] = new KeyBinding(KeyCode.Keypad6),
            [GameKey.ToggleSortingMode] = new KeyBinding(KeyCode.Keypad7),
            [GameKey.ScanSceneByDistance] = new KeyBinding(KeyCode.Keypad8),

            [GameKey.SelectNpcs] = new KeyBinding(KeyCode.Keypad1),
            [GameKey.SelectLocations] = new KeyBinding(KeyCode.Keypad2),
            [GameKey.SelectLoot] = new KeyBinding(KeyCode.Keypad3),
            [GameKey.SelectEverything] = new KeyBinding(KeyCode.Keypad0),

            [GameKey.FocusWaypoints] = new KeyBinding(KeyCode.Keypad1, requireCtrl: true),
            [GameKey.CreateWaypoint] = new KeyBinding(KeyCode.Keypad1, requireAlt: true),
            [GameKey.DeleteWaypoint] = new KeyBinding(KeyCode.Keypad2, requireAlt: true),

            // stardew-access: pageDown/pageUp = next/previous object.
            [GameKey.CycleForward] = new KeyBinding(KeyCode.PageDown),
            [GameKey.CycleBackward] = new KeyBinding(KeyCode.PageUp),
            // stardew-access: left ctrl + home = move to selected object.
            [GameKey.NavigateToSelected] = new KeyBinding(KeyCode.Home, requireCtrl: true),
            // stardew-access: esc = stop walking to a selected tile/object.
            [GameKey.StopMovement] = new KeyBinding(KeyCode.Escape),

            [GameKey.ToggleDialogReading] = new KeyBinding(KeyCode.KeypadMinus),
            [GameKey.RepeatDialogue] = new KeyBinding(KeyCode.R),
            [GameKey.ToggleOrbAnnouncements] = new KeyBinding(KeyCode.Alpha0),
            [GameKey.ToggleSpeechInterrupt] = new KeyBinding(KeyCode.Alpha8),
            [GameKey.ToggleDiagnostics] = new KeyBinding(KeyCode.Keypad9, requireCtrl: true),

            [GameKey.AnnounceStatus] = new KeyBinding(KeyCode.H),
            [GameKey.AnnounceStats] = new KeyBinding(KeyCode.X),
            [GameKey.AnnounceOfficerProfile] = new KeyBinding(KeyCode.O),
            [GameKey.ReadSkillDescription] = new KeyBinding(KeyCode.N),
            [GameKey.AnnounceKimStatus] = new KeyBinding(KeyCode.K),
        };

        public static void Initialize()
        {
            category = MelonPreferences.CreateCategory("KeyBindings");
            category.SetFilePath("UserData/AccessibilityMod.cfg");

            foreach (var kvp in Defaults)
            {
                var entry = category.CreateEntry<string>(kvp.Key.ToString(), kvp.Value.Serialize());
                entries[kvp.Key] = entry;
                current[kvp.Key] = KeyBinding.Deserialize(entry.Value, kvp.Value);
            }

            // MelonPreferences only writes the file to disk on an explicit save (or once a
            // value changes) - force one now so the config always exists with the current
            // bindings after startup, even before the player rebinds anything. An external
            // keybind editor can then rely on the file being present.
            category.SaveToFile();

            MelonLogger.Msg("[KEYBINDINGS] Loaded from UserData/AccessibilityMod.cfg");
        }

        public static KeyBinding Get(GameKey action) => current[action];

        public static void Set(GameKey action, KeyBinding binding)
        {
            current[action] = binding;
            entries[action].Value = binding.Serialize();
            category.SaveToFile();
        }

        public static void ApplyPreset(IReadOnlyDictionary<GameKey, KeyBinding> preset)
        {
            foreach (var kvp in preset)
            {
                current[kvp.Key] = kvp.Value;
                entries[kvp.Key].Value = kvp.Value.Serialize();
            }
            category.SaveToFile();
        }

        /// <summary>
        /// True on the frame the action's bound key was pressed, with at least the
        /// binding's own required modifiers held - an unrelated extra modifier held for
        /// some other reason (resting a hand on Ctrl, Sticky Keys, etc.) doesn't block an
        /// otherwise-unmodified hotkey, matching how these hotkeys behaved before they
        /// were made remappable. Where several GameKeys share a physical key with
        /// different modifier requirements (e.g. SelectNpcs / FocusWaypoints /
        /// CreateWaypoint can all sit on LeftBracket), this alone doesn't disambiguate
        /// them when more modifiers are held than the least-specific binding needs - the
        /// caller's if/else-if chain must check the more specific (more required
        /// modifiers) binding first so ties resolve to the more specific action, exactly
        /// as the original hardcoded chain did. See InputManager.HandleInput.
        /// </summary>
        public static bool IsPressed(GameKey action)
        {
            var binding = current[action];
            if (!UnityEngine.Input.GetKeyDown(binding.Key))
            {
                return false;
            }

            bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            bool alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);

            return (!binding.RequireCtrl || ctrl) && (!binding.RequireAlt || alt) && (!binding.RequireShift || shift);
        }
    }
}
