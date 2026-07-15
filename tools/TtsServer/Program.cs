using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Windows.Media.SpeechSynthesis;

namespace TtsServer;

/// <summary>
/// The orb text-to-speech server: a short-lived child process the mod starts and feeds. It
/// reads one JSON request per line from stdin - {"speaker":"...","text":"..."} - and speaks
/// each on a single playback thread through the configured Windows voice. The mod is dumb: it
/// only writes lines and never learns which voice, engine, or volume is in play, or whether a
/// line even made a sound.
///
/// Lifecycle is tied to the game, not to a clock: when the game (the parent) exits, the pipe
/// closes, stdin hits end-of-stream, and the server drains what is queued and exits cleanly.
/// No timers, no lingering process.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Diagnostic modes, so the engine can be exercised without the game or any audio.
        if (args.Length >= 1 && args[0] == "--voices")
        {
            foreach (var v in SpeechSynthesizer.AllVoices)
                Console.WriteLine($"{v.DisplayName}\t{v.Language}");
            return 0;
        }

        if (args.Length >= 2 && args[0] == "--render")
        {
            return Render(outPath: args[1], text: args.Length >= 3 ? args[2] : "Test.");
        }

        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: DiscoElysiumTtsServer <gamePath>   (reads JSON lines on stdin)");
            return 2;
        }

        var config = new OrbConfig(gamePath: args[0]);
        using var backend = new WinRtTtsBackend(config);

        // Single consumer thread: Speak() blocks until a line finishes, so the queue plays in
        // order and ambient orb lines never overlap.
        using var queue = new BlockingCollection<Utterance>(new ConcurrentQueue<Utterance>());
        var worker = new Thread(() =>
        {
            foreach (var u in queue.GetConsumingEnumerable())
            {
                try { backend.Speak(u.Speaker, u.Text); }
                catch (Exception ex) { Console.Error.WriteLine($"[tts] speak failed: {ex.Message}"); }
            }
        }) { IsBackground = true, Name = "tts-playback" };
        worker.Start();

        // Read stdin explicitly as UTF-8 so German text (ä, ö, ü) survives the pipe from the
        // mod regardless of the console's default code page.
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            var u = Parse(line);
            if (u != null) queue.Add(u);
        }

        // Parent gone: stop accepting, let the current line finish, then leave.
        queue.CompleteAdding();
        worker.Join(TimeSpan.FromSeconds(5));
        return 0;
    }

    private static int Render(string outPath, string text)
    {
        try
        {
            var synth = new SpeechSynthesizer();
            var stream = synth.SynthesizeTextToStreamAsync(text).AsTask().GetAwaiter().GetResult();
            using var netStream = stream.AsStreamForRead();
            using var file = File.Create(outPath);
            netStream.CopyTo(file);
            Console.WriteLine($"voice={synth.Voice.DisplayName} bytes={new FileInfo(outPath).Length}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("render failed: " + ex);
            return 1;
        }
    }

    private static Utterance? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            var speaker = root.TryGetProperty("speaker", out var s) ? s.GetString() ?? "" : "";
            return new Utterance(speaker, text);
        }
        catch (JsonException)
        {
            return null; // a stray non-JSON line is ignored rather than crashing the server
        }
    }

    private sealed record Utterance(string Speaker, string Text);
}
