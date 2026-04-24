#region Usings
using System;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.Accessibility.Feedback;

#region Interface: IAudioFeedbackService
/// <summary>
/// Plays non-verbal audio cues (chimes, buzzes, ticks) in response to
/// UI events. Consumers pick a <see cref="FeedbackTone"/> and the
/// service handles frequency, duration, and concurrency.
/// </summary>
/// <remarks>
/// The default implementation uses OpenAL (via OpenTK). If OpenAL isn't
/// initialized successfully — missing DLL, no audio device — the
/// service becomes a no-op so the app still runs silent.
/// </remarks>
public interface IAudioFeedbackService : IDisposable
{
    #region State

    /// <summary>True once OpenAL is ready to play tones.</summary>
    bool IsAvailable { get; }

    /// <summary>Master volume [0, 1] for all feedback tones. Defaults
    /// to 0.6. Persisted via settings.</summary>
    float Volume { get; set; }

    #endregion

    #region Playback

    /// <summary>Queue a tone for playback. Non-blocking; returns when
    /// the tone has been scheduled. Awaiting the returned task waits
    /// until playback actually finishes (useful for tests).</summary>
    Task PlayAsync(FeedbackTone tone);

    #endregion
}
#endregion
