#region Usings
// Pure value type — no imports.
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Struct: AudioFormat
/// <summary>
/// Describes the PCM format of an <see cref="AudioFrame"/> — sample rate,
/// channel count, and bit depth. Abstracts away NAudio's <c>WaveFormat</c>
/// so Core can declare formats without taking a dependency on the
/// capture library.
/// </summary>
/// <remarks>
/// Internally the router normalizes every source to
/// <see cref="TeamTalkDefault"/> (48 kHz mono 16-bit) before handing
/// frames to the TeamTalk sink, because that matches TT's default Opus
/// configuration exactly.
/// </remarks>
public readonly record struct AudioFormat(
    int SampleRate,
    int Channels,
    int BitsPerSample)
{
    #region Well-known formats
    // Named presets so source/sink code doesn't sprinkle magic numbers.

    /// <summary>48 kHz, mono, 16-bit signed PCM. The canonical pipeline
    /// format — matches TeamTalk's default Opus codec configuration.</summary>
    public static AudioFormat TeamTalkDefault { get; } = new(48_000, 1, 16);

    /// <summary>44.1 kHz, stereo, 16-bit. CD quality — a common WASAPI
    /// loopback default on older systems.</summary>
    public static AudioFormat CdStereo { get; } = new(44_100, 2, 16);

    /// <summary>48 kHz, stereo, 16-bit. A common WASAPI loopback default
    /// on modern Windows systems.</summary>
    public static AudioFormat DvdStereo { get; } = new(48_000, 2, 16);

    #endregion

    #region Derived properties
    // Computed shorthands — keep buffer math in one place.

    /// <summary>Bytes for one sample frame across all channels.</summary>
    public int BytesPerFrame => Channels * (BitsPerSample / 8);

    /// <summary>Byte rate — handy for sizing buffers by duration.</summary>
    public int AverageBytesPerSecond => SampleRate * BytesPerFrame;

    #endregion

    #region Helpers
    // Formatting helper also used for screen-reader announcements.

    /// <summary>Readable summary suitable for Tolk to speak aloud.</summary>
    public override string ToString() =>
        $"{SampleRate} Hz, {Channels} channel{(Channels == 1 ? "" : "s")}, {BitsPerSample}-bit";

    #endregion
}
#endregion
