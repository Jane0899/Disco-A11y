using System.Runtime.InteropServices;

namespace KeybindEditor;

/// <summary>
/// Minimal Tolk screen reader wrapper for this standalone WinForms tool (trimmed copy of
/// the P/Invoke signatures used by the mod's own mod/TolkScreenReader.cs). Requires
/// Tolk.dll (and nvdaControllerClient64.dll etc. next to it) in the app's output directory.
/// </summary>
internal static class Tolk
{
    [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Load();

    [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_IsLoaded();

    [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Unload();

    [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySAPI);

    [DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Tolk_Output(
        [MarshalAs(UnmanagedType.LPWStr)] string str,
        [MarshalAs(UnmanagedType.I1)] bool interrupt);

    private static bool loaded;

    public static void Initialize()
    {
        try
        {
            Tolk_TrySAPI(true);
            Tolk_Load();
            loaded = Tolk_IsLoaded();
        }
        catch
        {
            loaded = false;
        }
    }

    public static void Speak(string text, bool interrupt = true)
    {
        if (!loaded || string.IsNullOrEmpty(text)) return;
        try { Tolk_Output(text, interrupt); } catch { /* best-effort */ }
    }

    public static void Shutdown()
    {
        if (!loaded) return;
        try { Tolk_Unload(); } catch { /* best-effort */ }
        loaded = false;
    }
}
