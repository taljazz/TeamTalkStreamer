#region Usings
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Audio.OpenAL;
#endregion

namespace TeamTalkStreamer.Accessibility.Feedback;

#region Class: AudioFeedbackService
/// <summary>
/// Default <see cref="IAudioFeedbackService"/> implementation. Generates
/// short sine-wave tone sequences and plays them through OpenAL. Runs
/// on a single pooled worker task so overlapping requests queue up
/// rather than clash.
/// </summary>
/// <remarks>
/// Tones are synthesized on demand into a 16-bit mono PCM buffer at
/// 44.1 kHz and handed to a reusable OpenAL source. Buffer memory is
/// small (milliseconds of audio) so allocation isn't a concern.
/// </remarks>
public sealed class AudioFeedbackService : IAudioFeedbackService
{
    #region Constants

    #region Audio format
    // Feedback tones only, so CD quality mono is fine.
    private const int SampleRate = 44_100;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    #endregion

    #region Default tone durations (ms)
    // Centralized so a future preset (e.g. "minimal" vs "verbose" UI)
    // can shorten / lengthen cues uniformly.
    private const int ShortMs = 90;
    private const int MediumMs = 160;
    private const int LongMs = 260;
    #endregion

    #endregion

    #region Fields

    #region OpenAL handles
    // Device / context pair created once at construction. Source is
    // reused across plays; buffers are created per-tone and deleted
    // after playback completes.
    private readonly ALDevice _device;
    private readonly ALContext _context;
    private readonly int _source;
    private readonly bool _available;
    #endregion

    #region Playback coordination
    // Single worker so two near-simultaneous PlayAsync calls serialize.
    // Cancellation cuts off the queue on dispose.
    private readonly SemaphoreSlim _playLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    #endregion

    #region Mutable state
    private float _volume = 0.6f;
    #endregion

    #endregion

    #region Constructor
    // Initialize OpenAL. Any failure (missing DLL, no device) flips
    // _available to false and every subsequent call becomes a no-op.

    public AudioFeedbackService()
    {
        try
        {
            _device = ALC.OpenDevice(null);
            if (_device == ALDevice.Null)
            {
                _available = false;
                return;
            }

            _context = ALC.CreateContext(_device, (int[]?)null);
            ALC.MakeContextCurrent(_context);

            _source = AL.GenSource();
            AL.Source(_source, ALSourcef.Gain, _volume);

            _available = true;
        }
        catch (DllNotFoundException)
        {
            _available = false;
        }
        catch (Exception)
        {
            // Any other OpenAL initialization failure degrades gracefully.
            _available = false;
        }
    }

    #endregion

    #region Properties (IAudioFeedbackService)

    public bool IsAvailable => _available;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_available) AL.Source(_source, ALSourcef.Gain, _volume);
        }
    }

    #endregion

    #region Public playback

    public async Task PlayAsync(FeedbackTone tone)
    {
        if (!_available) return;

        // Serialize: each tone plays to completion before the next.
        await _playLock.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            var notes = ResolveNotes(tone);
            foreach (var (frequency, durationMs) in notes)
            {
                await PlayNoteAsync(frequency, durationMs).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose; swallow.
        }
        finally
        {
            _playLock.Release();
        }
    }

    #endregion

    #region Tone dispatch
    // Each FeedbackTone expands into a small sequence of (Hz, ms) notes.
    // Frequencies chosen for distinctiveness, not musical accuracy —
    // we're after "recognizable at a glance," not a pleasant melody.

    private static IReadOnlyList<(double Frequency, int DurationMs)> ResolveNotes(FeedbackTone tone) =>
        tone switch
        {
            FeedbackTone.NavigationTick     => new[] { (1_400.0, 35) },

            FeedbackTone.Success            => new[] { (523.25, ShortMs), (659.25, ShortMs), (783.99, MediumMs) }, // C5-E5-G5
            FeedbackTone.Failure            => new[] { (392.00, ShortMs), (261.63, MediumMs) },                   // G4-C4 down

            FeedbackTone.ConnectChime       => new[] { (659.25, ShortMs), (987.77, MediumMs) },                   // E5-B5
            FeedbackTone.DisconnectTone     => new[] { (493.88, ShortMs), (293.66, MediumMs) },                   // B4-D4

            FeedbackTone.StreamingStarted   => new[] { (523.25, ShortMs), (659.25, ShortMs),
                                                       (783.99, ShortMs), (1046.50, LongMs) },                    // C5-E5-G5-C6
            FeedbackTone.StreamingPaused    => new[] { (440.00, MediumMs) },                                      // A4
            FeedbackTone.StreamingStopped   => new[] { (523.25, ShortMs), (349.23, MediumMs) },                   // C5-F4 down

            FeedbackTone.Error              => new[] { (164.81, MediumMs), (130.81, LongMs) },                    // E3-C3 low

            _                                => Array.Empty<(double, int)>(),
        };

    #endregion

    #region PCM synthesis & playback

    /// <summary>Synthesize a single sine-wave note and play it through
    /// the persistent OpenAL source. Awaits until the note finishes.</summary>
    private async Task PlayNoteAsync(double frequency, int durationMs)
    {
        int sampleCount = SampleRate * durationMs / 1000;
        byte[] pcm = new byte[sampleCount * (BitsPerSample / 8)];

        // Generate 16-bit signed PCM samples. Apply a tiny linear
        // attack/release envelope (5 ms each side) so notes don't click.
        int attackSamples = Math.Min(sampleCount / 4, SampleRate / 200);
        double phaseStep = 2.0 * Math.PI * frequency / SampleRate;

        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = 1.0;
            if (i < attackSamples) envelope = (double)i / attackSamples;
            else if (i > sampleCount - attackSamples)
                envelope = (double)(sampleCount - i) / attackSamples;

            short sample = (short)(Math.Sin(i * phaseStep) * envelope * short.MaxValue * 0.7);
            pcm[i * 2] = (byte)(sample & 0xFF);
            pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        // Push into an OpenAL buffer, queue on the source, play,
        // wait, then clean up the buffer.
        int buffer = AL.GenBuffer();
        AL.BufferData(buffer, ALFormat.Mono16, pcm, SampleRate);
        AL.SourceQueueBuffer(_source, buffer);
        AL.SourcePlay(_source);

        try
        {
            await Task.Delay(durationMs + 20, _cts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Dequeue and release the buffer so we don't leak ALuint handles.
            AL.SourceStop(_source);
            AL.SourceUnqueueBuffer(_source);
            AL.DeleteBuffer(buffer);
        }
    }

    #endregion

    #region Disposal
    // Order matters: cancel the playback task first so it doesn't race
    // with OpenAL teardown, then release source / context / device.

    public void Dispose()
    {
        _cts.Cancel();
        _playLock.Dispose();

        if (_available)
        {
            try
            {
                AL.SourceStop(_source);
                AL.DeleteSource(_source);
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                ALC.CloseDevice(_device);
            }
            catch
            {
                // Nothing useful to do if teardown fails.
            }
        }

        _cts.Dispose();
    }

    #endregion
}
#endregion
