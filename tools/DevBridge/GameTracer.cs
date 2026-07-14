using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace DevBridge
{
    /// <summary>
    /// Writes down what the game is doing, so a hang can be looked at afterwards instead of
    /// guessed at.
    ///
    /// Built for one concrete question: why does loading a save sometimes stop on the
    /// loading screen and never come back? The loading screen is not a timer - it is held
    /// open by objects that register a delay (SceneTransitionManager.AddLoadingScreenDelay)
    /// and is supposed to come down when the last one deregisters. If something registers a
    /// delay and never removes it, the screen stays up forever. So the tracer names every
    /// object that adds or removes a delay, and can say at any moment who is still holding
    /// the screen.
    ///
    /// Everything the game itself logs (Unity's Debug.Log, warnings, exceptions) is captured
    /// too - the game says a lot, it just says it into a file nobody reads.
    ///
    /// Dev bridge only: this never ships in the mod.
    /// </summary>
    public static class GameTracer
    {
        private static readonly object writeLock = new object();
        private static readonly List<string> delayHolders = new();

        private static bool enabled;
        private static string logPath;

        public static bool Enabled => enabled;

        public static string LogPath =>
            logPath ??= Path.Combine("UserData", "DevBridge", "game-trace.log");

        public static void SetEnabled(bool on)
        {
            if (on == enabled) return;
            enabled = on;

            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                Application.add_logMessageReceived(new Action<string, string, LogType>(OnUnityLog));
                Write($"=== trace started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            else
            {
                Application.remove_logMessageReceived(new Action<string, string, LogType>(OnUnityLog));
                Write("=== trace stopped ===");
            }
        }

        private static void OnUnityLog(string message, string stackTrace, LogType type)
        {
            // Errors and exceptions carry their stack; the rest would drown the log in it.
            if (type == LogType.Error || type == LogType.Exception)
            {
                Write($"[{type}] {message}\n{stackTrace}");
            }
            else
            {
                Write($"[{type}] {message}");
            }
        }

        public static void Write(string line)
        {
            if (!enabled) return;

            try
            {
                lock (writeLock)
                {
                    File.AppendAllText(LogPath,
                        $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}",
                        new UTF8Encoding(true));
                }
            }
            catch
            {
                // A tracer that throws would take down the thing it is meant to observe.
            }
        }

        /// <summary>Who is currently holding the loading screen open - the answer to the hang.</summary>
        public static string DescribeDelayHolders()
        {
            lock (delayHolders)
            {
                if (delayHolders.Count == 0) return "nothing is holding the loading screen";
                return $"{delayHolders.Count} object(s) holding the loading screen open:\n" +
                       string.Join("\n", delayHolders);
            }
        }

        public static void NoteDelayAdded(string who)
        {
            lock (delayHolders) delayHolders.Add(who);
            Write($"[LOADSCREEN] + delay by {who}  (now {delayHolders.Count})");
        }

        public static void NoteDelayRemoved(string who, bool removed)
        {
            lock (delayHolders) delayHolders.Remove(who);
            Write($"[LOADSCREEN] - delay by {who}  (removed={removed}, now {delayHolders.Count})");
        }

        public static string Tail(int lines)
        {
            try
            {
                if (!File.Exists(LogPath)) return "no trace log yet - run 'trace on' first";
                var all = File.ReadAllLines(LogPath);
                int start = Math.Max(0, all.Length - lines);
                return string.Join("\n", all[start..]);
            }
            catch (Exception ex)
            {
                return $"could not read trace: {ex.Message}";
            }
        }

        private static string Describe(Il2CppSystem.Object obj)
        {
            try
            {
                if (obj == null) return "null";
                var behaviour = obj.TryCast<MonoBehaviour>();
                if (behaviour != null) return $"{behaviour.GetIl2CppType().Name} on '{behaviour.gameObject.name}'";
                return obj.GetIl2CppType().Name;
            }
            catch
            {
                return "?";
            }
        }

        [HarmonyPatch(typeof(Il2CppFortressOccident.SceneTransitionManager), nameof(Il2CppFortressOccident.SceneTransitionManager.AddLoadingScreenDelay))]
        public static class AddDelayPatch
        {
            public static void Postfix(Il2CppSystem.Object delayingObj, bool __result)
            {
                if (!enabled) return;
                if (__result) NoteDelayAdded(Describe(delayingObj));
            }
        }

        [HarmonyPatch(typeof(Il2CppFortressOccident.SceneTransitionManager), nameof(Il2CppFortressOccident.SceneTransitionManager.RemoveLoadingScreenDelay))]
        public static class RemoveDelayPatch
        {
            public static void Postfix(Il2CppSystem.Object delayingObj, bool __result)
            {
                if (!enabled) return;
                NoteDelayRemoved(Describe(delayingObj), __result);
            }
        }

        [HarmonyPatch(typeof(Il2CppFortressOccident.SceneTransitionManager), nameof(Il2CppFortressOccident.SceneTransitionManager.Load))]
        public static class LoadPatch
        {
            public static void Prefix(string sceneName, string destinationId, bool isGameLoad, bool showLoadingScreen, bool hideLoadingScreen)
            {
                Write($"[SCENE] Load('{sceneName}', dest='{destinationId}', isGameLoad={isGameLoad}, show={showLoadingScreen}, hide={hideLoadingScreen})");
            }
        }

        [HarmonyPatch(typeof(Il2CppFortressOccident.SceneTransitionManager), nameof(Il2CppFortressOccident.SceneTransitionManager.ShowLoadingScreen))]
        public static class ShowPatch
        {
            public static void Postfix() => Write("[LOADSCREEN] shown");
        }

        [HarmonyPatch(typeof(Il2CppFortressOccident.SceneTransitionManager), nameof(Il2CppFortressOccident.SceneTransitionManager.HideLoadingScreen))]
        public static class HidePatch
        {
            public static void Postfix() => Write("[LOADSCREEN] hidden");
        }

        [HarmonyPatch(typeof(Il2Cpp.SunshinePersistence), nameof(Il2Cpp.SunshinePersistence.Load))]
        public static class PersistenceLoadPatch
        {
            public static void Prefix(string fileName, bool isBundled) =>
                Write($"[SAVE] Load('{fileName}', bundled={isBundled})");
        }

        [HarmonyPatch(typeof(Il2Cpp.SunshinePersistence), nameof(Il2Cpp.SunshinePersistence.LoadNewest))]
        public static class PersistenceLoadNewestPatch
        {
            public static void Prefix() => Write("[SAVE] LoadNewest()");
        }
    }
}
