#region Usings
using System;
using System.Runtime.InteropServices;
using System.Threading;
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Class: ProcessLoopbackNative
/// <summary>
/// Windows Core Audio P/Invoke layer for the Windows 10 2004+ process-
/// loopback API. Lets <see cref="WasapiProcessLoopbackSource"/> activate
/// an <c>IAudioClient</c> that captures the default render endpoint
/// with a specific process tree EXCLUDED from the mix — which is the
/// only reliable way to loopback-capture system audio without picking
/// up the user's own screen reader.
/// </summary>
/// <remarks>
/// <para>Reference: Microsoft's <c>ApplicationLoopback</c> sample plus
/// <c>mmdeviceapi.h</c> / <c>audioclientactivationparams.h</c>.</para>
/// <para>Everything here is kept <c>internal</c>; the only public
/// surface is <see cref="ActivateProcessLoopbackClient"/>, which
/// wraps the whole async activation dance behind a synchronous
/// "give me an IAudioClient that excludes this PID" call.</para>
/// </remarks>
internal static class ProcessLoopbackNative
{
    #region Constants

    #region Device-interface path & known GUIDs
    // The "virtual audio device" path used by ActivateAudioInterfaceAsync
    // to request the process-loopback client. Any other path would
    // give us a regular render/capture device, not a process-scoped one.
    internal const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";

    // Interface GUIDs from Windows SDK headers (KSM / mmdeviceapi).
    internal static readonly Guid IID_IAudioClient =
        new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    internal static readonly Guid IID_IAudioCaptureClient =
        new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    #endregion

    #region AudioClient flags & modes
    // Direct values from audioclient.h so we don't need the NAudio enum
    // (which lives in a different namespace and isn't always visible
    // here depending on package versions).
    internal const int AUDCLNT_SHAREMODE_SHARED            = 0;
    internal const uint AUDCLNT_STREAMFLAGS_LOOPBACK       = 0x00020000;
    internal const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK  = 0x00040000;
    internal const uint AUDCLNT_BUFFERFLAGS_SILENT         = 0x2;
    #endregion

    #region PROPVARIANT variant tag
    internal const ushort VT_BLOB = 17;
    #endregion

    #region WAVEFORMATEX tag
    internal const ushort WAVE_FORMAT_PCM = 1;
    #endregion

    #endregion

    #region Structs

    #region AUDIOCLIENT_ACTIVATION_PARAMS
    // Passed inside a PROPVARIANT blob to ActivateAudioInterfaceAsync.
    // ActivationType selects what kind of client we want; the following
    // fields configure the process-loopback-specific variant. Flat
    // layout (no union) because we only ever populate the process-
    // loopback parameters on this path.
    [StructLayout(LayoutKind.Sequential)]
    internal struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public uint ActivationType;      // 0 = Default, 1 = ProcessLoopback
        public uint TargetProcessId;
        public uint ProcessLoopbackMode; // 0 = Include tree, 1 = Exclude tree
    }

    internal const uint ActivationType_ProcessLoopback     = 1;
    internal const uint ProcessLoopbackMode_ExcludeTree    = 1;
    #endregion

    #region WAVEFORMATEX
    // Minimal "plain PCM" descriptor we hand to IAudioClient.Initialize.
    // Pack = 2 matches the Windows SDK layout (the trailing cbSize is a
    // WORD, not a DWORD).
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }
    #endregion

    #endregion

    #region COM interfaces
    // Declared with ComImport so the marshaler generates RCWs for pointers
    // handed back by the Windows API. GUIDs must match Windows SDK exactly.

    #region IActivateAudioInterfaceAsyncOperation
    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(
            out int activateResult,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }
    #endregion

    #region IActivateAudioInterfaceCompletionHandler
    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }
    #endregion

    #region IAudioClient (subset we actually call)
    // Vtable order MUST match audioclient.h exactly or the marshaler
    // will call the wrong method. We declare every slot up to the one
    // we need, using pointer-typed signatures for anything complex.
    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig]
        int Initialize(
            int shareMode,
            uint streamFlags,
            long bufferDuration,     // REFERENCE_TIME (100-ns units)
            long periodicity,
            IntPtr format,           // WAVEFORMATEX*
            IntPtr audioSessionGuid);// LPCGUID (can be NULL)

        [PreserveSig]
        int GetBufferSize(out uint numBufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long streamLatency);

        [PreserveSig]
        int GetCurrentPadding(out uint numPaddingFrames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr waveFormat);

        [PreserveSig]
        int GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);

        [PreserveSig]
        int GetService(
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IUnknown)] out object audioClientService);
    }
    #endregion

    #region IAudioCaptureClient
    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(
            out IntPtr dataBuffer,
            out uint numFramesToRead,
            out uint dwFlags,
            out ulong devicePosition,
            out ulong qpcPosition);

        [PreserveSig]
        int ReleaseBuffer(uint numFramesRead);

        [PreserveSig]
        int GetNextPacketSize(out uint numFramesInNextPacket);
    }
    #endregion

    #endregion

    #region P/Invoke
    // ActivateAudioInterfaceAsync lives in Mmdevapi.dll. PreserveSig=false
    // means the HRESULT is turned into a managed exception on failure,
    // which matches the "it either throws or returns a valid handle"
    // contract we want at this layer.
    [DllImport(
        "Mmdevapi.dll",
        CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true,
        PreserveSig = false)]
    internal static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [In] ref Guid riid,
        [In] IntPtr activationParams,
        [In] IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);
    #endregion

    #region ActivationHandler — managed completion callback
    /// <summary>
    /// Managed implementation of <see cref="IActivateAudioInterfaceCompletionHandler"/>.
    /// Windows calls <see cref="ActivateCompleted"/> on the activation
    /// worker thread; we capture the result, signal an event, and the
    /// waiting thread unblocks and casts the returned IUnknown to
    /// <see cref="IAudioClient"/>.
    /// </summary>
    [ComVisible(true)]
    internal sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        #region State captured by ActivateCompleted

        /// <summary>Event signaled once the callback has run.</summary>
        public ManualResetEventSlim Done { get; } = new(initialState: false);

        /// <summary>Activated interface (cast to IAudioClient by the caller),
        /// or null if activation failed.</summary>
        public object? ActivatedInterface { get; private set; }

        /// <summary>HRESULT from <c>GetActivateResult</c>. 0 means success.</summary>
        public int HResult { get; private set; }

        #endregion

        #region IActivateAudioInterfaceCompletionHandler
        // Must return S_OK (0) so the runtime is happy. The actual
        // success / failure of the activation is in HResult above.
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(
                    out int activateResult,
                    out object iface);
                HResult = activateResult;
                ActivatedInterface = (activateResult >= 0) ? iface : null;
            }
            catch (Exception)
            {
                // Rare — e.g., if the operation pointer is bad. Surface
                // as a generic failure HRESULT so the caller knows.
                HResult = unchecked((int)0x80004005); // E_FAIL
                ActivatedInterface = null;
            }
            finally
            {
                Done.Set();
            }
            return 0; // S_OK
        }
        #endregion
    }
    #endregion

    #region High-level helper: ActivateProcessLoopbackClient

    /// <summary>
    /// Synchronously activate a process-loopback <see cref="IAudioClient"/>
    /// that captures the default render endpoint with
    /// <paramref name="excludedProcessId"/>'s whole process tree excluded
    /// from the mix. Throws on timeout or HRESULT failure.
    /// </summary>
    internal static IAudioClient ActivateProcessLoopbackClient(
        int excludedProcessId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        #region Build the activation params in unmanaged memory
        // We own both buffers until we've cleaned up after the wait.
        var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
        {
            ActivationType      = ActivationType_ProcessLoopback,
            TargetProcessId     = (uint)excludedProcessId,
            ProcessLoopbackMode = ProcessLoopbackMode_ExcludeTree,
        };

        int paramsSize = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
        IntPtr propVariantPtr = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(activationParams, paramsPtr, fDeleteOld: false);

            // Zero the PROPVARIANT header, then set vt + blob fields at
            // the canonical x64 offsets: vt at 0, blob.cbSize at 8,
            // blob.pBlobData at 16 (pointer-aligned within the union).
            for (int i = 0; i < 24; i++) Marshal.WriteByte(propVariantPtr, i, 0);
            Marshal.WriteInt16(propVariantPtr, 0, unchecked((short)VT_BLOB));
            Marshal.WriteInt32(propVariantPtr, 8, paramsSize);
            Marshal.WriteIntPtr(propVariantPtr, 16, paramsPtr);
            #endregion

            #region Kick off activation + wait for the callback
            var handler = new ActivationHandler();
            Guid iidAudioClient = IID_IAudioClient;

            ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                ref iidAudioClient,
                propVariantPtr,
                handler,
                out _);

            if (!handler.Done.Wait(timeout, cancellationToken))
            {
                throw new TimeoutException(
                    "Process-loopback activation did not complete within " +
                    $"{timeout.TotalSeconds:0.#} seconds.");
            }

            if (handler.HResult < 0 || handler.ActivatedInterface is null)
            {
                throw new InvalidOperationException(
                    "ActivateAudioInterfaceAsync failed with HRESULT " +
                    $"0x{handler.HResult:X8}. On Windows older than 10 " +
                    "version 2004 (May 2020), process loopback is not " +
                    "supported — upgrade Windows or clear the excluded-" +
                    "app setting.");
            }

            return (IAudioClient)handler.ActivatedInterface;
            #endregion
        }
        finally
        {
            // Free in reverse order of allocation. Safe to do after the
            // wait: once GetActivateResult returns, Windows has copied
            // everything it needs from the blob.
            Marshal.FreeHGlobal(propVariantPtr);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    #endregion
}
#endregion
