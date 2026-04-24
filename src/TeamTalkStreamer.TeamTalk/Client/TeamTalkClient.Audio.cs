#nullable enable

#region Usings
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.TeamTalk.Client;

#region Class: TeamTalkClient (partial — audio injection)
/// <summary>
/// PCM injection into the TeamTalk channel. Uses TeamTalk's virtual
/// sound input device (<c>TT_SOUNDDEVICE_ID_TEAMTALK_VIRTUAL</c>) so
/// we bypass real mic capture, then submits frames via
/// <c>InsertAudioBlock</c>.
/// </summary>
/// <remarks>
/// TeamTalk's voice path has two mutually-exclusive modes:
/// <list type="bullet">
///   <item><description><c>EnableVoiceTransmission(true)</c> — reads
///     from the initialized sound input device (mic).</description></item>
///   <item><description><c>InsertAudioBlock(block)</c> — submits raw
///     PCM on the voice stream, bypassing the mic.</description></item>
/// </list>
/// We want the second. Calling <c>InsertAudioBlock</c> with a valid
/// block starts/continues the input session; calling with a
/// null-equivalent block (<c>nSamples == 0</c>, <c>lpRawAudio == 0</c>)
/// ends it, per the SDK docs.
/// </remarks>
public sealed partial class TeamTalkClient
{
    #region Transmission state

    /// <summary>Enable PCM-injection mode. Must be in
    /// <see cref="TeamTalkConnectionState.Joined"/> first. After this
    /// returns, <see cref="SendFrameAsync"/> is ready to accept frames.</summary>
    public void BeginTransmission()
    {
        if (State is not (TeamTalkConnectionState.Joined or
                          TeamTalkConnectionState.Streaming))
            throw new InvalidOperationException(
                "Must be joined to a channel before transmitting.");

#if TT_SDK_REFERENCED
        // Wire up the virtual input device so InsertAudioBlock is the
        // accepted source of audio. No real mic is opened.
        _native?.InitSoundInputDevice(
            BearWare.SoundDeviceConstants.TT_SOUNDDEVICE_ID_TEAMTALK_VIRTUAL);
#endif

        TransitionTo(TeamTalkConnectionState.Streaming);
    }

    /// <summary>End the injection session. The server sees the voice
    /// stream go quiet on its normal inactivity timer.</summary>
    public void EndTransmission()
    {
        if (State != TeamTalkConnectionState.Streaming) return;

#if TT_SDK_REFERENCED
        // Empty block with nSamples == 0 signals end-of-input per the
        // docs on InsertAudioBlock.
        var endBlock = new BearWare.AudioBlock
        {
            nStreamID = 1,
            nSampleRate = (int)AudioFormat.TeamTalkDefault.SampleRate,
            nChannels = AudioFormat.TeamTalkDefault.Channels,
            lpRawAudio = IntPtr.Zero,
            nSamples = 0,
        };
        _native?.InsertAudioBlock(endBlock);

        // Release the virtual input device so a future StartStreaming
        // re-initializes cleanly.
        _native?.CloseSoundInputDevice();
#endif

        TransitionTo(TeamTalkConnectionState.Joined);
    }

    #endregion

    #region Frame submission

    /// <summary>
    /// Hand a PCM frame to the SDK for transmission. The frame must be
    /// 16-bit PCM at the sample rate / channel count negotiated with
    /// the channel's codec (default: 48 kHz mono).
    /// </summary>
    /// <remarks>
    /// The SDK's <see cref="BearWare.AudioBlock.lpRawAudio"/> is a
    /// raw <see cref="IntPtr"/> into unmanaged-looking memory, so we
    /// pin the managed buffer for the duration of the call. Pinning
    /// per-frame is cheap; the alternative (a persistent pinned
    /// pool) is a future optimization.
    /// </remarks>
    public ValueTask SendFrameAsync(
        AudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        if (State != TeamTalkConnectionState.Streaming)
            return ValueTask.CompletedTask;

#if TT_SDK_REFERENCED
        // Copy bytes into a short[] for pinning. (Frame.Data is
        // ReadOnlyMemory<byte> which we can't directly pin as a short[].)
        int byteCount = frame.Data.Length;
        int sampleCount = byteCount / 2;
        short[] samples = new short[sampleCount];
        var span = frame.Data.Span;
        for (int i = 0; i < sampleCount; i++)
            samples[i] = (short)(span[i * 2] | (span[i * 2 + 1] << 8));

        // Pin, populate the AudioBlock, submit, unpin.
        var handle = GCHandle.Alloc(samples, GCHandleType.Pinned);
        try
        {
            var block = new BearWare.AudioBlock
            {
                nStreamID = 1,
                nSampleRate = frame.Format.SampleRate,
                nChannels = frame.Format.Channels,
                lpRawAudio = handle.AddrOfPinnedObject(),
                nSamples = sampleCount / Math.Max(1, frame.Format.Channels),
            };
            _native?.InsertAudioBlock(block);
        }
        finally
        {
            handle.Free();
        }
#else
        _ = frame; // silence the "unused" warning in SDK-less builds
#endif

        return ValueTask.CompletedTask;
    }

    #endregion
}
#endregion
