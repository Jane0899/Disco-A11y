namespace TtsServer;

/// <summary>
/// One way of turning text into audible speech. The server owns the choice of backend; the
/// mod never knows which one is live. Today there is exactly one - <see cref="WinRtTtsBackend"/>,
/// the Windows speech engine that reaches the OneCore and natural voices - but the seam is
/// here on purpose: an ElevenLabs backend, or one that plays pre-rendered WAV files per
/// speaker, would slot in without the mod changing a line. The <paramref name="speaker"/> is
/// carried through for exactly that future (per-speaker voices) even though the current
/// backend only reads it into the spoken line.
/// </summary>
public interface ITtsBackend
{
    /// <summary>
    /// Speaks one line, blocking until it has finished playing. Called on the server's single
    /// playback thread, so returning only when done is what serializes the queue - ambient orb
    /// lines never talk over each other.
    /// </summary>
    void Speak(string speaker, string text);
}
