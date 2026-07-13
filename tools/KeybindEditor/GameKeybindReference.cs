namespace KeybindEditor;

/// <summary>
/// The game's own keyboard controls, for looking up - not for changing. They live in
/// the game's options, not in our config.
///
/// The list cannot be hard-coded: the player may have rebound them in-game, and a
/// hard-coded list would then quietly lie. Instead the mod writes what it reads from the
/// live game (GameKeybindConflictChecker) to UserData/GameKeybinds.txt, and we show that.
/// Until the game has run once with the mod, there is nothing to show.
/// </summary>
public static class GameKeybindReference
{
    public sealed record Entry(string Action, string Keys);

    public static List<Entry> Load(string gamePath)
    {
        var entries = new List<Entry>();
        var path = Path.Combine(gamePath, "UserData", "GameKeybinds.txt");
        if (!File.Exists(path)) return entries;

        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split('\t');
            if (parts.Length != 2) continue;

            entries.Add(new Entry(parts[0].Trim(), Prettify(parts[1].Trim())));
        }

        return entries;
    }

    /// <summary>
    /// The names come from the game's input system in its own shorthand ("pad1", "num8"),
    /// which reads badly aloud. Spell them the way the keys are labelled.
    /// </summary>
    private static string Prettify(string keys) =>
        string.Join(", ", keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key switch
            {
                _ when key.StartsWith("pad") => Strings.Get("KeyNumpad") + " " + key["pad".Length..],
                _ when key.StartsWith("num") => key["num".Length..],
                _ => key,
            }));
}
