namespace Installer;

/// <summary>
/// The whole self-update, visible: it announces the new version and asks, shows the
/// download progressing, and then lets the user decide when the restart happens.
///
/// A progress bar alone tells a blind user nothing, so every step is spoken through the
/// screen reader as well, and the percentage is announced in steps of ten - often enough
/// to know it is alive, rarely enough not to become a chatter machine.
/// </summary>
public sealed class UpdateForm : Form
{
    public enum Outcome { Updated, Declined, Failed }

    private readonly string[] originalArgs;
    private readonly string newVersion;

    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;
    private readonly Button updateButton;
    private readonly Button cancelButton;
    private readonly Button restartNowButton;
    private readonly Button restartLaterButton;

    private int lastSpokenPercent = -1;

    public Outcome Result { get; private set; } = Outcome.Declined;

    /// <summary>Set when the user chose to restart the installer themselves.</summary>
    public bool UserWillRestart { get; private set; }

    public string Error { get; private set; }

    public UpdateForm(string newVersion, string[] originalArgs)
    {
        this.newVersion = newVersion;
        this.originalArgs = originalArgs;

        Text = Strings.Get("WindowTitle");
        Width = 620;
        Height = 260;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        statusLabel = new Label
        {
            Left = 16,
            Top = 16,
            Width = 570,
            Height = 90,
            // The self-contained build is retired. Someone still running one is about to be
            // updated into a build that needs the .NET desktop runtime - say so before it
            // happens, not afterwards when the window simply fails to come back.
            Text = SelfUpdater.IsRetiredStandalone
                ? Strings.Get("UpdateConfirm", newVersion) + " " + Strings.Get("StandaloneRetired")
                : Strings.Get("UpdateConfirm", newVersion),
        };

        progressBar = new ProgressBar
        {
            Left = 16,
            Top = 115,
            Width = 570,
            Height = 24,
            Minimum = 0,
            Maximum = 100,
            Visible = false,
            AccessibleName = Strings.Get("UpdateProgressAccessible"),
        };

        updateButton = new Button { Left = 16, Top = 160, Width = 200, Text = Strings.Get("UpdateNow") };
        updateButton.Click += async (_, _) => await RunUpdateAsync();

        cancelButton = new Button { Left = 230, Top = 160, Width = 160, Text = Strings.Get("UpdateCancel") };
        cancelButton.Click += (_, _) => { Result = Outcome.Declined; Close(); };

        restartNowButton = new Button { Left = 16, Top = 160, Width = 200, Visible = false, Text = Strings.Get("RestartNow") };
        restartNowButton.Click += (_, _) => { UserWillRestart = false; Close(); };

        restartLaterButton = new Button { Left = 230, Top = 160, Width = 240, Visible = false, Text = Strings.Get("RestartMyself") };
        restartLaterButton.Click += (_, _) => { UserWillRestart = true; Close(); };

        Controls.AddRange(new Control[] { statusLabel, progressBar, updateButton, cancelButton, restartNowButton, restartLaterButton });

        AcceptButton = updateButton;
        CancelButton = cancelButton;

        Shown += (_, _) => Announce(statusLabel.Text);
    }

    private async Task RunUpdateAsync()
    {
        updateButton.Enabled = false;
        cancelButton.Enabled = false;
        progressBar.Visible = true;
        SetStatus(Strings.Get("UpdateDownloading", newVersion));

        var progress = new Progress<int>(percent =>
        {
            progressBar.Value = Math.Clamp(percent, 0, 100);

            // Spoken in tens: a bar that a screen reader never mentions is an empty promise,
            // and one that speaks every percent is unusable.
            var step = percent / 10 * 10;
            if (step <= lastSpokenPercent || step == 0) return;
            lastSpokenPercent = step;
            Announce(Strings.Get("UpdatePercent", step));
        });

        Error = await SelfUpdater.DownloadAndSwapAsync(progress);

        if (Error != null)
        {
            Result = Outcome.Failed;
            SetStatus(Strings.Get("UpdateCheckFailed", Error));
            progressBar.Visible = false;
            cancelButton.Enabled = true;
            cancelButton.Text = Strings.Get("Close");
            cancelButton.Focus();
            return;
        }

        Result = Outcome.Updated;
        SetStatus(Strings.Get("UpdateDoneRestart"));

        updateButton.Visible = false;
        cancelButton.Visible = false;
        restartNowButton.Visible = true;
        restartLaterButton.Visible = true;
        AcceptButton = restartNowButton;
        restartNowButton.Focus();
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text;
        Announce(text);
    }

    private static void Announce(string text) => Tolk.Speak(text);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window with the X during the download would leave a half-written
        // file and a user who thinks nothing happened.
        if (progressBar.Visible && Result != Outcome.Updated && Result != Outcome.Failed)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
