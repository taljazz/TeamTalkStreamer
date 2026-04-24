#region Usings
using System;
using Concentus;
using Concentus.Enums;
#endregion

namespace TeamTalkStreamer.MobileBridge.Protocol;

#region Class: OpusDecoderWrapper
/// <summary>
/// Thin convenience wrapper around Concentus's <c>OpusDecoder</c>.
/// Hides the slightly awkward C-style init/Decode signature and gives
/// the rest of the project a simple <c>DecodeFrame</c> method that
/// takes compressed bytes and returns 16-bit PCM.
/// </summary>
/// <remarks>
/// Concentus is a pure-managed port of libopus — no native DLL needed.
/// That's why we picked it over <c>OpusSharp</c> or a libopus P/Invoke.
/// </remarks>
public sealed class OpusDecoderWrapper
{
    #region Fields
    // Concentus decoder is stateful; keep one per client connection so
    // packet-loss concealment and jitter buffering work correctly.
    // IOpusDecoder is the modern interface; the concrete implementation
    // is chosen by OpusCodecFactory (native if available, managed fallback).
    private readonly IOpusDecoder _decoder;
    private readonly int _sampleRate;
    private readonly int _channels;
    #endregion

    #region Constructor

    /// <param name="sampleRate">Decoder output rate. Opus supports
    /// 8/12/16/24/48 kHz; 48 kHz matches the pipeline's canonical rate.</param>
    /// <param name="channels">1 for mono, 2 for stereo. Pipeline uses mono.</param>
    public OpusDecoderWrapper(int sampleRate = 48_000, int channels = 1)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);
    }

    #endregion

    #region Public API

    /// <summary>Decode one Opus packet to 16-bit PCM samples. Returns
    /// the number of samples per channel actually decoded.</summary>
    /// <param name="opusPayload">Compressed Opus frame (typically 20 ms
    /// at 48 kHz = up to ~120 bytes for speech).</param>
    /// <param name="pcmOut">Destination buffer, must be large enough
    /// for one frame — 960 samples per channel for 20 ms at 48 kHz.</param>
    public int DecodeFrame(ReadOnlySpan<byte> opusPayload, Span<short> pcmOut)
    {
        // Frame size in samples per channel. 20 ms @ 48 kHz = 960.
        int frameSize = (_sampleRate / 1000) * 20;

        // Modern span-based overload — no allocation.
        return _decoder.Decode(opusPayload, pcmOut, frameSize, decode_fec: false);
    }

    #endregion
}
#endregion
