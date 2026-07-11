namespace KeybindEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var initialGamePath = args.Length >= 1 && Directory.Exists(args[0]) ? args[0] : null;
        Application.Run(new MainForm(initialGamePath));
    }
}
