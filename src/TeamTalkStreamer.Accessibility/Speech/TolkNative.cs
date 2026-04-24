#region Usings
using System;
using System.Runtime.InteropServices;
#endregion

namespace TeamTalkStreamer.Accessibility.Speech;

#region Class: TolkNative
/// <summary>
/// Raw P/Invoke declarations for the Tolk native library. Consumers
/// should use <see cref="TolkSpeechService"/>, not these directly —
/// this class exists to keep the unsafe surface isolated in one file.
/// </summary>
/// <remarks>
/// Tolk ships as a pair of DLLs (<c>Tolk.dll</c> and the per-screen-reader
/// client DLLs like <c>nvdaControllerClient64.dll</c>). All must be in
/// the process working directory (i.e. the App's output folder). The
/// build copies them from <c>&lt;solutionRoot&gt;/libs/tolk/</c>.
/// </remarks>
internal static class TolkNative
{
    #region DLL name
    // Kept as a constant rather than an inline literal so we can find
    // every P/Invoke in one place when debugging missing-DLL issues.
    private const string Dll = "Tolk.dll";
    #endregion

    #region Lifecycle
    // Tolk_Load / Tolk_Unload are idempotent; calling Load twice is a no-op.

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Tolk_Load();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Tolk_Unload();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Tolk_IsLoaded();

    #endregion

    #region Detection
    // Returns a pointer to a wide-char string with the screen reader
    // name, or null if none detected. Caller must NOT free the string.

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode)]
    public static extern IntPtr Tolk_DetectScreenReader();

    #endregion

    #region Speech output
    // Tolk_Output: interrupts current speech with the new text.
    // Tolk_Speak: queues (does not interrupt).
    // Tolk_Silence: cancels current speech.

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Tolk_Output(
        [MarshalAs(UnmanagedType.LPWStr)] string str,
        [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl,
               CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Tolk_Speak(
        [MarshalAs(UnmanagedType.LPWStr)] string str,
        [MarshalAs(UnmanagedType.I1)] bool interrupt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Tolk_Silence();

    #endregion

    #region SAPI fallback
    // When no dedicated screen reader is detected, Tolk can use SAPI
    // instead. Must be toggled on before Tolk_Load for the fallback
    // to take effect.

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Tolk_TrySAPI(
        [MarshalAs(UnmanagedType.I1)] bool trySAPI);

    #endregion
}
#endregion
