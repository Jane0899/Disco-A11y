namespace DiscoA11y.Fixes;

/// <summary>
/// Catalog of known installation repairs, shared between the installer (applies them
/// automatically on every install) and the configurator (offers them in the repair
/// window and via --repair). A fix is engine-only: it detects and repairs, but carries
/// no user-facing text - each tool localizes by <see cref="ModFix.Id"/> ("FixName_" + Id).
///
/// Adding a future fix = one new ModFix subclass + one entry in <see cref="ModFixCatalog.All"/>
/// + a "FixName_&lt;Id&gt;" string in both tools' Strings.cs.
/// </summary>
public abstract class ModFix
{
    /// <summary>Stable identifier; the localization key is "FixName_" + Id.</summary>
    public abstract string Id { get; }

    /// <summary>True when the problem is present in this game folder and Apply would change something.</summary>
    public abstract bool IsNeeded(string gamePath);

    /// <summary>Repairs the problem. Only called when IsNeeded is true. Throws on failure.</summary>
    public abstract void Apply(string gamePath);
}

public enum FixOutcome
{
    NotNeeded,
    Applied,
    Failed,
}

public sealed record FixResult(ModFix Fix, FixOutcome Outcome, string? Error = null);

public static class ModFixCatalog
{
    public static readonly IReadOnlyList<ModFix> All = new ModFix[]
    {
        new MelonLoaderProxyFix(),
    };

    /// <summary>The fixes modify files the running game holds open - callers refuse while it runs.</summary>
    public static bool IsGameRunning() =>
        System.Diagnostics.Process.GetProcessesByName("disco").Length > 0;

    public static List<FixResult> ApplyAll(string gamePath)
    {
        var results = new List<FixResult>();
        foreach (var fix in All)
        {
            try
            {
                results.Add(fix.IsNeeded(gamePath)
                    ? Run(fix, gamePath)
                    : new FixResult(fix, FixOutcome.NotNeeded));
            }
            catch (Exception ex)
            {
                // IsNeeded itself failed - report as a failure rather than skipping silently.
                results.Add(new FixResult(fix, FixOutcome.Failed, ex.Message));
            }
        }
        return results;
    }

    private static FixResult Run(ModFix fix, string gamePath)
    {
        try
        {
            fix.Apply(gamePath);
            return new FixResult(fix, FixOutcome.Applied);
        }
        catch (Exception ex)
        {
            return new FixResult(fix, FixOutcome.Failed, ex.Message);
        }
    }
}

/// <summary>
/// The July 2026 Windows update (KB5101650) makes the loader resolve version.dll from
/// System32 even when a copy sits next to the game exe - the classic MelonLoader proxy
/// is silently never loaded and the game starts unmodded. MelonLoader's bootstrap
/// supports several proxy names; winmm.dll is still resolved from the game folder, so
/// the repair is renaming the proxy. Verified live on a broken installation on 17.07.2026:
/// with version.dll the game loaded C:\Windows\System32\version.dll and no MelonLoader
/// log was written; renamed to winmm.dll the bootstrap came up normally.
/// </summary>
public sealed class MelonLoaderProxyFix : ModFix
{
    public override string Id => "MelonLoaderProxy";

    // Only a version.dll that sits next to a MelonLoader folder is the MelonLoader
    // proxy - a bare version.dll without MelonLoader is not ours to touch.
    public override bool IsNeeded(string gamePath) =>
        File.Exists(Path.Combine(gamePath, "version.dll")) &&
        Directory.Exists(Path.Combine(gamePath, "MelonLoader"));

    public override void Apply(string gamePath)
    {
        var versionDll = Path.Combine(gamePath, "version.dll");
        var winmmDll = Path.Combine(gamePath, "winmm.dll");

        if (!File.Exists(winmmDll))
        {
            File.Move(versionDll, winmmDll);
            return;
        }

        // A winmm.dll proxy is already in place (e.g. a fresh MelonLoader zip was
        // extracted over a repaired install) - just retire the version.dll so the
        // proxy can never load twice should Windows revert the search behavior.
        var disabled = Path.Combine(gamePath, "version.dll.disabled");
        if (File.Exists(disabled)) File.Delete(disabled);
        File.Move(versionDll, disabled);
    }
}
