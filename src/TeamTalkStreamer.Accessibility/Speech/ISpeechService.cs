#region Usings
using System;
#endregion

namespace TeamTalkStreamer.Accessibility.Speech;

#region Interface: ISpeechService
/// <summary>
/// Abstracts the screen-reader output so the rest of the app can
/// announce state changes, errors, and UI navigation without taking a
/// direct dependency on Tolk or any specific screen reader.
/// </summary>
/// <remarks>
/// The default implementation is <c>TolkSpeechService</c>, which routes
/// to NVDA / JAWS / SAPI via the Tolk library. A null-object stub can
/// be swapped in for headless tests.
/// </remarks>
public interface ISpeechService : IDisposable
{
    #region State

    /// <summary>True if a screen reader (or SAPI fallback) was detected
    /// and Tolk initialized successfully.</summary>
    bool IsAvailable { get; }

    /// <summary>Name of the active screen reader, or <c>"SAPI"</c> for
    /// the fallback, or <c>null</c> when not available.</summary>
    string? ActiveScreenReader { get; }

    #endregion

    #region Speech commands
    // Two kinds: Speak (queued) and Output (interrupts). Mirror Tolk's
    // own API so the mapping is 1:1.

    /// <summary>Queue text to be spoken after anything already in the
    /// screen reader's queue. Use for UI announcements that shouldn't
    /// interrupt what the user is doing.</summary>
    void Speak(string text);

    /// <summary>Interrupt the current speech and say this instead. Use
    /// sparingly — reserved for important state changes (errors,
    /// disconnects, streaming start/stop).</summary>
    void Output(string text);

    /// <summary>Stop whatever is currently being spoken.</summary>
    void Silence();

    #endregion
}
#endregion
