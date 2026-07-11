using System.Runtime.InteropServices;

namespace Installer;

/// <summary>
/// Creates a Start Menu shortcut to the Keybind Editor, in a subfolder named after the
/// game (matching wherever Steam itself would put per-game shortcuts). Uses the
/// WScript.Shell COM object late-bound via reflection so no COM reference is needed in
/// the csproj.
/// </summary>
public static class StartMenuShortcut
{
    public static bool TryCreate(string gamePath, string keybindEditorExe, out string message)
    {
        try
        {
            var folderName = Path.GetFileName(gamePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Disco Elysium";

            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), folderName);
            Directory.CreateDirectory(startMenuDir);

            var linkPath = Path.Combine(startMenuDir, Strings.Get("ShortcutFileName") + ".lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                message = "WScript.Shell COM object not available.";
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(linkPath);
                try
                {
                    shortcut.TargetPath = keybindEditorExe;
                    shortcut.Arguments = $"\"{gamePath}\"";
                    shortcut.WorkingDirectory = Path.GetDirectoryName(keybindEditorExe);
                    shortcut.IconLocation = keybindEditorExe;
                    shortcut.Description = Strings.Get("ShortcutDescription");
                    shortcut.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }

            message = linkPath;
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }
}
