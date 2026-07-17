using System.Windows.Forms;
using DiscoA11y.Fixes;

namespace KeybindEditor;

/// <summary>
/// The repair window: lists every known installation fix from the shared
/// <see cref="ModFixCatalog"/> with its current state (single column - screen readers
/// read lines, not grids) and applies the needed ones on request. The same catalog runs
/// automatically inside the installer; this window exists so a broken installation can
/// be repaired without re-running a full install.
/// </summary>
public sealed class RepairForm : Form
{
    private readonly string gamePath;
    private readonly ListView fixList;
    private readonly Button repairButton;
    private readonly Button closeButton;
    private readonly Label statusLabel;

    public RepairForm(string gamePath)
    {
        this.gamePath = gamePath;

        Width = 640;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        Text = Strings.Get("RepairWindowTitle");

        fixList = new ListView
        {
            Left = 12,
            Top = 12,
            Width = 600,
            Height = 260,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            AccessibleName = Strings.Get("RepairListAccessible"),
        };
        fixList.Columns.Add(Strings.Get("RepairColumnHeader"), 580);

        repairButton = new Button { Left = 12, Top = 285, Width = 220, Height = 32, Text = Strings.Get("RepairRun") };
        repairButton.Click += RepairButton_Click;

        closeButton = new Button { Left = 240, Top = 285, Width = 120, Height = 32, Text = Strings.Get("RepairClose") };
        closeButton.Click += (_, _) => Close();
        CancelButton = closeButton;

        statusLabel = new Label { Left = 12, Top = 330, Width = 600, AccessibleName = Strings.Get("StatusAccessible") };

        Controls.AddRange(new Control[] { fixList, repairButton, closeButton, statusLabel });

        Load += (_, _) => RefreshStates(announceSummary: true);
    }

    /// <summary>
    /// One row per known fix: "name — state". Announces the summary so the answer to
    /// "is anything broken?" is spoken the moment the window opens.
    /// </summary>
    private void RefreshStates(bool announceSummary)
    {
        fixList.Items.Clear();
        var needed = 0;
        foreach (var fix in ModFixCatalog.All)
        {
            string state;
            try
            {
                var isNeeded = fix.IsNeeded(gamePath);
                if (isNeeded) needed++;
                state = isNeeded ? Strings.Get("RepairStateNeeded") : Strings.Get("RepairStateOk");
            }
            catch (Exception ex)
            {
                state = Strings.Get("RepairStateError", ex.Message);
            }
            fixList.Items.Add(new ListViewItem($"{Strings.Get("FixName_" + fix.Id)} — {state}"));
        }

        repairButton.Enabled = needed > 0;
        if (announceSummary)
        {
            SetStatus(needed > 0 ? Strings.Get("RepairSummaryNeeded", needed) : Strings.Get("RepairNoneNeeded"));
        }
    }

    private void RepairButton_Click(object? sender, EventArgs e)
    {
        // The fixes rename files in the game folder - a running game holds them open.
        if (ModFixCatalog.IsGameRunning())
        {
            SetStatus(Strings.Get("RepairGameRunning"));
            return;
        }

        var results = ModFixCatalog.ApplyAll(gamePath);
        RefreshStates(announceSummary: false);

        var applied = results.Count(r => r.Outcome == FixOutcome.Applied);
        var failed = results.Where(r => r.Outcome == FixOutcome.Failed).ToList();
        if (failed.Count > 0)
        {
            var first = failed[0];
            SetStatus(Strings.Get("RepairFailed", Strings.Get("FixName_" + first.Fix.Id), first.Error ?? ""));
        }
        else if (applied > 0)
        {
            SetStatus(Strings.Get("RepairDone", applied));
        }
        else
        {
            SetStatus(Strings.Get("RepairNoneNeeded"));
        }
    }

    private void SetStatus(string message)
    {
        statusLabel.Text = message;
        Tolk.Speak(message);
    }
}
