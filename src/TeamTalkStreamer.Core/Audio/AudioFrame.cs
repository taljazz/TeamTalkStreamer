#region Usings
using System;
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Struct: AudioFrame
/// <summary>
/// A single chunk of PCM audio produced by an <see cref="IAudioSource"/>
/// and consumed by an <see cref="Pipeline.IAudioSink"/>. Immutable —
/// implementations must copy into an owned buffer before constructing
/// the frame so the source is free to reuse its capture buffer.
/// </summary>
/// <remarks>
/// Frames typically represent 20 ms of audio (matching Opus frame size).
/// Each frame carries its own <see cref="AudioFormat"/> so downstream
/// code can resample / convert / mix independently.
/// </remarks>
public readonly struct AudioFrame
{
    #region Fields
    // Backing storage for the PCM payload. ReadOnlyMemory<byte> allows
    // callers to slice views without additional copies.
    private readonly ReadOnlyMemory<byte> _payload;
    #endregion

    #region Properties
    // Public surface — everything read-only because the struct is immutable.

    /// <summary>Raw PCM bytes in this frame's declared <see cref="Format"/>.</summary>
    public ReadOnlyMemory<byte> Data => _payload;

    /// <summary>PCM format of <see cref="Data"/>. Source sets this once at
    /// start; frames produced by the same source all share one format.</summary>
    public AudioFormat Format { get; }

    /// <summary>High-resolution capture timestamp (Stopwatch ticks). Used
    /// for drift measurement and mixer alignment, not wall-clock time.</summary>
    public long CaptureTimestampTicks { get; }

    #endregion

    #region Constructor
    // Single constructor — every field must be supplied; frames are not
    // meaningful without all three.

    /// <param name="data">PCM bytes. Caller is expected to have already
    /// copied into an owned buffer — the struct does not take ownership
    /// so it can't safely pin or free it.</param>
    /// <param name="format">Format describing <paramref name="data"/>.</param>
    /// <param name="captureTimestampTicks">Stopwatch ticks at capture time.</param>
    public AudioFrame(ReadOnlyMemory<byte> data, AudioFormat format, long captureTimestampTicks)
    {
        _payload = data;
        Format = format;
        CaptureTimestampTicks = captureTimestampTicks;
    }

    #endregion

    #region Derived info
    // Helpful computations so callers don't do buffer math inline.

    /// <summary>Duration this frame represents in milliseconds. Useful for
    /// buffer accounting, rate control, and drift detection.</summary>
    public double DurationMilliseconds =>
        Format.AverageBytesPerSecond == 0
            ? 0.0
            : (double)_payload.Length * 1000.0 / Format.AverageBytesPerSecond;

    #endregion
}
#endregion
