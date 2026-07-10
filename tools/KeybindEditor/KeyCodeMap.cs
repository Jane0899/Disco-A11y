using System.Windows.Forms;

namespace KeybindEditor;

/// <summary>
/// Translates between System.Windows.Forms.Keys (what this WinForms app receives from
/// KeyDown) and the UnityEngine.KeyCode name strings the mod stores in its cfg file.
/// Covers letters, digits, numpad, function keys, common punctuation, and navigation
/// keys - the practical range for remapping hotkeys. Layout-position keys like the
/// digits and punctuation deliberately keep the same name in both directions since
/// that's what actually differs between US and non-US keyboards.
/// </summary>
public static class KeyCodeMap
{
    private static readonly Dictionary<Keys, string> FormsToUnity = new()
    {
        [Keys.D0] = "Alpha0", [Keys.D1] = "Alpha1", [Keys.D2] = "Alpha2", [Keys.D3] = "Alpha3",
        [Keys.D4] = "Alpha4", [Keys.D5] = "Alpha5", [Keys.D6] = "Alpha6", [Keys.D7] = "Alpha7",
        [Keys.D8] = "Alpha8", [Keys.D9] = "Alpha9",

        [Keys.NumPad0] = "Keypad0", [Keys.NumPad1] = "Keypad1", [Keys.NumPad2] = "Keypad2",
        [Keys.NumPad3] = "Keypad3", [Keys.NumPad4] = "Keypad4", [Keys.NumPad5] = "Keypad5",
        [Keys.NumPad6] = "Keypad6", [Keys.NumPad7] = "Keypad7", [Keys.NumPad8] = "Keypad8",
        [Keys.NumPad9] = "Keypad9",
        [Keys.Decimal] = "KeypadPeriod",
        [Keys.Divide] = "KeypadDivide",
        [Keys.Multiply] = "KeypadMultiply",
        [Keys.Subtract] = "KeypadMinus",
        [Keys.Add] = "KeypadPlus",

        [Keys.Oemtilde] = "BackQuote",
        [Keys.OemOpenBrackets] = "LeftBracket",
        [Keys.OemCloseBrackets] = "RightBracket",
        [Keys.OemPipe] = "Backslash",
        [Keys.OemSemicolon] = "Semicolon",
        [Keys.OemQuotes] = "Quote",
        [Keys.Oemplus] = "Equals",
        [Keys.OemMinus] = "Minus",
        [Keys.OemPeriod] = "Period",
        [Keys.Oemcomma] = "Comma",
        [Keys.OemQuestion] = "Slash",

        [Keys.Space] = "Space",
        [Keys.Tab] = "Tab",
        [Keys.Return] = "Return",
        [Keys.Escape] = "Escape",
        [Keys.Back] = "Backspace",
        [Keys.Left] = "LeftArrow",
        [Keys.Right] = "RightArrow",
        [Keys.Up] = "UpArrow",
        [Keys.Down] = "DownArrow",
        [Keys.PageUp] = "PageUp",
        [Keys.PageDown] = "PageDown",
        [Keys.Home] = "Home",
        [Keys.End] = "End",
        [Keys.Insert] = "Insert",
        [Keys.Delete] = "Delete",
    };

    static KeyCodeMap()
    {
        // Letters (A-Z) and function keys (F1-F24) share identical names in both enums.
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var key = (Keys)Enum.Parse(typeof(Keys), c.ToString());
            FormsToUnity[key] = c.ToString();
        }
        for (var i = 1; i <= 24; i++)
        {
            var name = $"F{i}";
            if (Enum.TryParse<Keys>(name, out var key))
            {
                FormsToUnity[key] = name;
            }
        }
    }

    private static readonly Dictionary<string, string> UnityToFriendly = new()
    {
        ["BackQuote"] = "`", ["LeftBracket"] = "[", ["RightBracket"] = "]", ["Backslash"] = "\\",
        ["Semicolon"] = ";", ["Quote"] = "'", ["Equals"] = "=", ["Minus"] = "-",
        ["Period"] = ".", ["Comma"] = ",", ["Slash"] = "/",
    };

    private static readonly Dictionary<string, string> KeypadSuffix = new()
    {
        ["KeypadPeriod"] = ".", ["KeypadDivide"] = "/", ["KeypadMultiply"] = "*",
        ["KeypadMinus"] = "-", ["KeypadPlus"] = "+",
    };

    /// <summary>Returns the Unity KeyCode name for a pressed Forms key, or null if unsupported.</summary>
    public static string? ToUnityName(Keys key) => FormsToUnity.TryGetValue(key, out var name) ? name : null;

    /// <summary>Human-readable, localized label for a Unity KeyCode name.</summary>
    public static string ToFriendly(string unityKeyName)
    {
        if (UnityToFriendly.TryGetValue(unityKeyName, out var friendly))
        {
            return friendly;
        }

        if (KeypadSuffix.TryGetValue(unityKeyName, out var suffix))
        {
            return Strings.Get("NumpadPrefix") + suffix;
        }

        if (unityKeyName.StartsWith("Alpha") && unityKeyName.Length == 6)
        {
            return unityKeyName[5..];
        }

        if (unityKeyName.StartsWith("Keypad") && unityKeyName.Length == 7)
        {
            return Strings.Get("NumpadPrefix") + unityKeyName[6..];
        }

        return unityKeyName;
    }
}
