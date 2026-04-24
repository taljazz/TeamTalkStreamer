#region Usings
using System;
using System.Runtime.InteropServices;
#endregion

namespace TeamTalkStreamer.Accessibility.Speech;

#region Class: TolkSpeechService
/// <summary>
/// <see cref="ISpeechService"/> implementation backed by the Tolk
/// library. Handles detection, lifecycle, and marshaling of the wide-
/// character screen reader name, and degrades gracefully when the
/// native DLLs aren't present (calls become no-ops).
/// </summary>
/// <remarks>
/// Typical lifetime: construct once at app startup (via DI), let it
/// live for the process. Disposing calls <c>Tolk_Unload</c>.
/// </remarks>
public sealed class TolkSpeechService : ISpeechService
{
    #region Fields

    #region Native state
    // _nativeAvailable caches whether Tolk.dll actually loaded. If a
    // DllNotFoundException fires on first use we flip this to false
    // and every subsequent call becomes a cheap no-op.
    private bool _nativeAvailable = true;
    #endregion

    #region Published state
    // Updated once during construction, then read-only for the rest of
    // the object's life.
    private string? _activeScreenReader;
    private bool _isAvailable;
    #endregion

    #endregion

    #region Constructor
    // Loads Tolk, enables SAPI fallback, and caches the detected
    // screen reader name. Any failure flips _nativeAvailable off and
    // leaves IsAvailable = false; callers see a no-op service.

    public TolkSpeechService()
    {
        try
        {
            // Must enable SAPI BEFORE Load, per Tolk docs — otherwise
            // Tolk skips SAPI detection during initialization.
            TolkNative.Tolk_TrySAPI(true);
            TolkNative.Tolk_Load();

            _isAvailable = TolkNative.Tolk_IsLoaded();
            _activeScreenReader = ReadDetectedScreenReader();
        }
        catch (DllNotFoundException)
        {
            // libs/tolk/ isn't populated yet, or deploy is missing the
            // DLL. Silently degrade — the app still works, just quietly.
            _nativeAvailable = false;
            _isAvailable = false;
            _activeScreenReader = null;
        }
    }

    #endregion

    #region Properties (ISpeechService)

    public bool IsAvailable => _isAvailable;
    public string? ActiveScreenReader => _activeScreenReader;

    #endregion

    #region Public speech commands
    // Wrap each P/Invoke with the "native available?" guard so the
    // service is safe to use even when Tolk isn't installed.

    public void Speak(string text)
    {
        if (!_nativeAvailable || string.IsNullOrEmpty(text)) return;

        try { TolkNative.Tolk_Speak(text, interrupt: false); }
        catch (DllNotFoundException) { _nativeAvailable = false; }
    }

    public void Output(string text)
    {
        if (!_nativeAvailable || string.IsNullOrEmpty(text)) return;

        try { TolkNative.Tolk_Output(text, interrupt: true); }
        catch (DllNotFoundException) { _nativeAvailable = false; }
    }

    public void Silence()
    {
        if (!_nativeAvailable) return;

        try { TolkNative.Tolk_Silence(); }
        catch (DllNotFoundException) { _nativeAvailable = false; }
    }

    #endregion

    #region Private helpers

    /// <summary>Turns the pointer from <c>Tolk_DetectScreenReader</c>
    /// into a managed string. Returns <c>null</c> when no reader is
    /// detected.</summary>
    private static string? ReadDetectedScreenReader()
    {
        IntPtr ptr = TolkNative.Tolk_DetectScreenReader();
        return ptr == IntPtr.Zero
            ? null
            : Marshal.PtrToStringUni(ptr);
    }

    #endregion

    #region Disposal
    // Paired with Tolk_Load in the constructor. Swallow missing-DLL
    // here since we can't un-load what never loaded.

    public void Dispose()
    {
        if (!_nativeAvailable) return;

        try { TolkNative.Tolk_Unload(); }
        catch (DllNotFoundException) { /* already gone */ }
    }

    #endregion
}
#endregion
