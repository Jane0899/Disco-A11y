using System.Runtime.InteropServices;

namespace KeybindEditor;

internal static class Program
{
    private const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Disco Elysium";

    // A WinExe has no console of its own - attach to the caller's so --cli output
    // actually shows up when run from a terminal.
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--cli")
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            Environment.ExitCode = RunCli(args.Skip(1).ToArray());
            return;
        }

        ApplicationConfiguration.Initialize();
        var initialGamePath = args.Length >= 1 && Directory.Exists(args[0]) ? args[0] : null;
        Application.Run(new MainForm(initialGamePath));
    }

    private static int RunRepair(string gamePath)
    {
        if (DiscoA11y.Fixes.ModFixCatalog.IsGameRunning())
        {
            Console.WriteLine("FAILED: Disco Elysium is running - close the game first, then repair.");
            return 1;
        }

        var exitCode = 0;
        foreach (var result in DiscoA11y.Fixes.ModFixCatalog.ApplyAll(gamePath))
        {
            var name = Strings.Get("FixName_" + result.Fix.Id);
            switch (result.Outcome)
            {
                case DiscoA11y.Fixes.FixOutcome.Applied:
                    Console.WriteLine($"Repaired: {name}");
                    break;
                case DiscoA11y.Fixes.FixOutcome.Failed:
                    Console.WriteLine($"FAILED: {name} - {result.Error}");
                    exitCode = 1;
                    break;
                default:
                    Console.WriteLine($"OK (nothing to do): {name}");
                    break;
            }
        }
        return exitCode;
    }

    /// <summary>
    /// Non-interactive config maintenance:
    ///   DiscoElysiumKeybindEditor.exe --cli [gamePath] [--preset default|numpad|stardew] [--list]
    ///   DiscoElysiumKeybindEditor.exe --cli [gamePath] --repair   (Disco Doctor, unattended)
    ///
    /// Without --preset this performs a "sync": actions the mod gained since the config
    /// was written are added with their default binding, every existing binding and
    /// setting is left untouched. With --preset all bindings are replaced by that
    /// preset. --list prints the resulting bindings. Meant for updating a config after
    /// a mod update without opening the GUI.
    /// </summary>
    private static int RunCli(string[] args)
    {
        var gamePath = args.FirstOrDefault(a => !a.StartsWith("--"));
        string? preset = null;
        var list = args.Contains("--list");

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--preset" && i + 1 < args.Length)
            {
                preset = args[i + 1].ToLowerInvariant();
            }
        }

        gamePath ??= Directory.Exists(DefaultGamePath) ? DefaultGamePath : null;
        if (gamePath == null || !Directory.Exists(gamePath))
        {
            Console.WriteLine("FAILED: game folder not found. Pass it explicitly: --cli \"<path>\"");
            return 1;
        }

        // --repair: the Disco Doctor, unattended - run the shared fix catalog and stop.
        // Single-purpose on purpose: repairing and config maintenance stay separate steps.
        if (args.Contains("--repair"))
        {
            return RunRepair(gamePath);
        }

        var cfgPath = Path.Combine(gamePath, "UserData", "AccessibilityMod.cfg");
        var existed = File.Exists(cfgPath);
        var config = ModConfig.LoadOrDefault(cfgPath);

        Console.WriteLine(existed
            ? $"Loaded config: {cfgPath}"
            : $"No config found at {cfgPath} - creating a fresh one with defaults.");

        if (preset != null)
        {
            if (preset is not ("default" or "numpad" or "stardew"))
            {
                Console.WriteLine($"FAILED: unknown preset '{preset}'. Valid: default, numpad, stardew.");
                return 1;
            }

            foreach (var action in GameKeyCatalog.Actions)
            {
                config.KeyBindings[action.Name] = preset switch
                {
                    "numpad" => action.SafeBinding,
                    "stardew" => action.StardewBinding,
                    _ => action.DefaultBinding,
                };
            }
            Console.WriteLine($"Applied preset: {preset} (all bindings replaced; general settings kept).");
        }
        else if (existed)
        {
            Console.WriteLine(config.AddedActions.Count == 0
                ? "Config already knows every action - nothing to add."
                : $"Added {config.AddedActions.Count} new action(s) with default bindings: {string.Join(", ", config.AddedActions)}");
        }

        try
        {
            config.Save(cfgPath);
            Console.WriteLine("Saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED to save: {ex.Message}");
            return 1;
        }

        if (list)
        {
            Console.WriteLine();
            foreach (var action in GameKeyCatalog.Actions)
            {
                Console.WriteLine($"{action.Name} = {config.KeyBindings[action.Name]}");
            }

            var gameControls = GameKeybindReference.Load(gamePath);
            Console.WriteLine();
            if (gameControls.Count == 0)
            {
                Console.WriteLine("The game's own controls are unknown - play once with the mod installed and they will be listed here.");
            }
            else
            {
                Console.WriteLine("The game's own controls (reference only, set in the game's options):");
                foreach (var entry in gameControls)
                {
                    Console.WriteLine($"  {entry.Action} = {entry.Keys}");
                }
            }
        }

        return 0;
    }
}
