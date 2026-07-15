using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MelonLoader;

namespace AccessibilityMod
{
    /// <summary>
    /// The mod's whole side of orb speech: hand a (speaker, text) pair to the external TTS
    /// server and forget it. The mod is deliberately dumb here - it does not know which voice,
    /// which engine, or which volume is in play, nor whether a line actually made a sound. All
    /// of that lives in the server (tools/TtsServer), reached only through this one pipe. That
    /// is what lets the voice grow later - a natural/neural voice, ElevenLabs, pre-rendered WAV
    /// per speaker - without the mod changing a line.
    ///
    /// Why a separate process at all: the modern Windows voices (OneCore and the natural voices)
    /// are only reachable through WinRT, whose runtime is a 25 MB projection. Loading that into
    /// the game, and owning audio playback there, is weight and risk the game does not need. The
    /// server carries it instead, starts warm once, queues lines so ambient orbs never talk over
    /// each other, and dies on its own the moment the game exits (its stdin hits end-of-stream).
    ///
    /// The screen reader (Tolk) stays in-process and unchanged: it is the fast, interruptible,
    /// braille-carrying critical path, and a server crash must never take it down. If the server
    /// cannot even be started, <see cref="Speak"/> returns false and the caller falls back to
    /// the screen reader, so orb text is never simply lost.
    /// </summary>
    public static class OrbSpeech
    {
        private const string ExeName = "DiscoElysiumTtsServer.exe";

        private static readonly object gate = new object();
        private static Process server;
        private static bool giveUp; // exe missing or unstartable: stop trying, fall back to Tolk

        /// <summary>
        /// Queues one orb line on the server's voice. Returns false if the server is unavailable,
        /// so the caller can route the line to the screen reader instead of dropping it.
        /// </summary>
        public static bool Speak(string speaker, string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            lock (gate)
            {
                if (giveUp) return false;
                if (!EnsureServer()) return false;

                if (TryWrite(speaker, text)) return true;

                // A broken pipe means the server died (crash, or killed). Try to bring it back
                // exactly once; if that also fails, give up for this session.
                MelonLogger.Warning("[TTS] orb server pipe broke - restarting it once.");
                KillServer();
                if (!EnsureServer()) return false;
                return TryWrite(speaker, text);
            }
        }

        private static bool TryWrite(string speaker, string text)
        {
            try
            {
                server.StandardInput.WriteLine(ToJson(speaker, text));
                server.StandardInput.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureServer()
        {
            try
            {
                if (server != null && !server.HasExited) return true;
            }
            catch
            {
                // HasExited can throw once the process object is stale; treat as gone.
            }
            server = null;

            var exe = FindExe();
            if (exe == null)
            {
                MelonLogger.Warning($"[TTS] {ExeName} not found - orb text falls back to the screen reader.");
                giveUp = true;
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"\"{Directory.GetCurrentDirectory()}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    StandardInputEncoding = new UTF8Encoding(false),
                    WorkingDirectory = Path.GetDirectoryName(exe),
                };
                server = Process.Start(startInfo);
                if (server == null)
                {
                    giveUp = true;
                    return false;
                }
                MelonLogger.Msg("[TTS] Orb voice server started.");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TTS] could not start the orb server ({ex.Message}) - orb text falls back to the screen reader.");
                giveUp = true;
                return false;
            }
        }

        /// <summary>Closes the pipe so the server exits by itself; called when the mod unloads.</summary>
        public static void Shutdown()
        {
            lock (gate)
            {
                KillServer();
            }
        }

        private static void KillServer()
        {
            try
            {
                if (server != null && !server.HasExited)
                {
                    server.StandardInput.Close(); // EOF: the server drains and exits on its own
                    if (!server.WaitForExit(1000)) server.Kill();
                }
            }
            catch
            {
                // Best-effort: on the way down, a failure to stop the child is not worth a throw.
            }
            server = null;
        }

        private static string ToJson(string speaker, string text)
        {
            var sb = new StringBuilder(text.Length + 32);
            sb.Append("{\"speaker\":\"").Append(Escape(speaker ?? "")).Append("\",\"text\":\"").Append(Escape(text)).Append("\"}");
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>The server ships in the mod zip; look where the installer puts it.</summary>
        private static string FindExe()
        {
            string cwd = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(cwd, "Mods", "TtsServer", ExeName),
                Path.Combine(cwd, "TtsServer", ExeName),
                Path.Combine(cwd, "Mods", ExeName),
                Path.Combine(cwd, ExeName),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
