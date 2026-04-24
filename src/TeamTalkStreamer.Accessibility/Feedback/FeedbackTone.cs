#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.Accessibility.Feedback;

#region Enum: FeedbackTone
/// <summary>
/// Non-verbal audio cues played by <c>AudioFeedbackService</c>. Each
/// value maps to a pre-defined sequence of tones (frequencies +
/// durations) so the UI can say "play a success chime" without
/// knowing any audio details.
/// </summary>
/// <remarks>
/// The goal of these tones is to give blind users the same "it worked"
/// / "something's off" gut feeling a sighted user gets from an icon or
/// color. Keep the palette small and distinctive — too many tones and
/// users can't tell them apart.
/// </remarks>
public enum FeedbackTone
{
    #region Navigation (quick, low-salience)

    /// <summary>Short high tick. Played on menu / list navigation so the
    /// user hears movement without a verbose screen-reader announcement.</summary>
    NavigationTick = 0,

    #endregion

    #region Outcomes (success / failure pairs)

    /// <summary>Ascending C–E–G chime. Generic "action succeeded"
    /// confirmation (source added, settings saved, etc.).</summary>
    Success = 10,

    /// <summary>Descending two-note buzz. Generic "action failed / invalid"
    /// cue. Distinct from <see cref="Error"/>, which is for exceptions.</summary>
    Failure = 11,

    #endregion

    #region Connection lifecycle

    /// <summary>Rising two-note chime. Played when TeamTalk connects or
    /// a mobile device pairs successfully.</summary>
    ConnectChime = 20,

    /// <summary>Falling two-note tone. Played on disconnect / unpair.</summary>
    DisconnectTone = 21,

    #endregion

    #region Streaming state

    /// <summary>Triumphant C–E–G–C fanfare. Played when the full session
    /// goes live (connected + source attached + streaming).</summary>
    StreamingStarted = 30,

    /// <summary>Single soft tone. Played when streaming is paused.</summary>
    StreamingPaused = 31,

    /// <summary>Short descending thud. Played when streaming stops
    /// cleanly (user-initiated).</summary>
    StreamingStopped = 32,

    #endregion

    #region Errors

    /// <summary>Low double buzz. Reserved for real faults — exceptions,
    /// disconnects from error, compile failures, etc. Deliberately
    /// unpleasant so the user notices.</summary>
    Error = 90,

    #endregion
}
#endregion
