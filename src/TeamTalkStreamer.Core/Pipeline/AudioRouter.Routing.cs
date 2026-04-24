#region Usings
using System;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.Core.Pipeline;

#region Class: AudioRouter (partial — hot path)
/// <summary>
/// Second partial of <see cref="AudioRouter"/>. Contains only the
/// per-frame dispatch code so the hot path is easy to spot, profile,
/// and optimize in isolation from the cold registration bookkeeping
/// that lives in <c>AudioRouter.cs</c>.
/// </summary>
public sealed partial class AudioRouter
{
    #region Frame dispatch

    /// <summary>
    /// Invoked by <see cref="IAudioSource.FrameCaptured"/> for every
    /// frame from every attached source. Runs on the source's capture
    /// thread — keep this method lean and non-blocking.
    /// </summary>
    /// <remarks>
    /// Current behavior: snapshot the sink list under a short lock,
    /// then fire-and-forget <see cref="IAudioSink.WriteAsync"/> for
    /// each sink. Sinks are responsible for buffering and backpressure;
    /// the router deliberately does not await writes so a slow sink
    /// can't block capture from a fast source.
    ///
    /// Future work (not in v1):
    /// <list type="bullet">
    ///   <item><description>Run frames through <c>AudioMixer</c> when
    ///     more than one source is active so multiple sources become a
    ///     single mixed stream per sink.</description></item>
    ///   <item><description>Normalize to <see cref="AudioFormat.TeamTalkDefault"/>
    ///     here rather than relying on sources to emit the right format.</description></item>
    /// </list>
    /// </remarks>
    private void OnSourceFrame(object? sender, AudioFrame frame)
    {
        #region Snapshot sinks
        // Copy to an array under the lock so the hot loop below can run
        // without holding it. Arrays are cheaper to iterate than lists
        // and don't allocate an enumerator.
        IAudioSink[] snapshot;
        lock (_sinksLock)
        {
            if (_sinks.Count == 0) return;          // no-op fast path
            snapshot = _sinks.ToArray();
        }
        #endregion

        #region Fan out to sinks
        // WriteAsync is intentionally not awaited. Sinks buffer; if a
        // sink's buffer is full it decides what to do. The discard (_)
        // makes the fire-and-forget explicit to analyzers.
        foreach (var sink in snapshot)
        {
            _ = sink.WriteAsync(frame);
        }
        #endregion
    }

    #endregion
}
#endregion
