#region Usings
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TeamTalkStreamer.Core.Audio;
#endregion

namespace TeamTalkStreamer.Core.Pipeline;

#region Class: AudioRouter (partial — registration & state)
/// <summary>
/// Central pipeline component: attaches to <see cref="IAudioSource"/>s,
/// (optionally) runs their frames through an <c>AudioMixer</c>, and
/// forwards the result to one or more <see cref="IAudioSink"/>s.
/// </summary>
/// <remarks>
/// Split across two partial files on purpose:
/// <list type="bullet">
///   <item><description><c>AudioRouter.cs</c> (this file) — fields,
///     construction, source/sink registration, state bookkeeping.</description></item>
///   <item><description><c>AudioRouter.Routing.cs</c> — the hot path:
///     per-frame dispatch from sources to sinks.</description></item>
/// </list>
/// Keeping the cold bookkeeping separate from the hot per-frame code
/// makes profiling and targeted optimization straightforward.
/// </remarks>
public sealed partial class AudioRouter : IDisposable
{
    #region Fields

    #region Attached components
    // Concurrent dictionaries / locked list so components can be added
    // or removed while the pipeline is running without blocking the
    // per-frame dispatch in AudioRouter.Routing.cs.
    private readonly ConcurrentDictionary<Guid, IAudioSource> _sources = new();
    private readonly List<IAudioSink> _sinks = new();
    private readonly object _sinksLock = new();
    #endregion

    #region State
    // Coarse pipeline state driven by "any sources? any sinks?"
    // Individual source/sink state is tracked on the components themselves.
    private PipelineState _state = PipelineState.Idle;
    private readonly object _stateLock = new();
    #endregion

    #endregion

    #region Events

    /// <summary>Raised whenever the pipeline's coarse <see cref="PipelineState"/>
    /// changes. UI subscribes to update the "streaming" indicator.</summary>
    public event EventHandler<PipelineState>? StateChanged;

    #endregion

    #region Properties

    /// <summary>Current aggregate pipeline state.</summary>
    public PipelineState State
    {
        get { lock (_stateLock) return _state; }
    }

    /// <summary>Live dictionary of attached sources, keyed by Id. Safe to
    /// enumerate from any thread (backed by <c>ConcurrentDictionary</c>).</summary>
    public IReadOnlyDictionary<Guid, IAudioSource> Sources => _sources;

    /// <summary>Number of sinks currently attached. The sink list itself is
    /// not exposed — use <see cref="AddSink"/> / <see cref="RemoveSink"/>.</summary>
    public int SinkCount
    {
        get { lock (_sinksLock) return _sinks.Count; }
    }

    #endregion

    #region Source registration
    // Attach wires a source's FrameCaptured into our hot-path handler.
    // Detach unhooks it. Both are idempotent.

    /// <summary>Register a source. The router will immediately start
    /// receiving its frames (once the source itself is capturing).</summary>
    public void AttachSource(IAudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.TryAdd(source.Id, source))
        {
            // OnSourceFrame lives in AudioRouter.Routing.cs.
            source.FrameCaptured += OnSourceFrame;
            RecomputeState();
        }
    }

    /// <summary>Unregister a source. Does not stop or dispose it — that's
    /// the caller's responsibility.</summary>
    public void DetachSource(Guid sourceId)
    {
        if (_sources.TryRemove(sourceId, out var source))
        {
            source.FrameCaptured -= OnSourceFrame;
            RecomputeState();
        }
    }

    #endregion

    #region Sink registration

    /// <summary>Register a sink. Every subsequent frame from every source
    /// will be forwarded to it via <see cref="IAudioSink.WriteAsync"/>.</summary>
    public void AddSink(IAudioSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        lock (_sinksLock) _sinks.Add(sink);
        RecomputeState();
    }

    /// <summary>Unregister a sink. Does not close or dispose it.</summary>
    public void RemoveSink(IAudioSink sink)
    {
        lock (_sinksLock) _sinks.Remove(sink);
        RecomputeState();
    }

    #endregion

    #region State bookkeeping
    // Pipeline is:
    //   Idle    - no sources or no sinks
    //   Ready   - at least one of each, but nothing capturing yet
    //   Running - computed elsewhere (router doesn't introspect sources)
    //
    // In v1 we keep this simple: Idle vs. Ready is all we distinguish
    // here; the App layer upgrades "Ready" to "Running" when it wants to
    // based on the source states it's orchestrating.

    private void RecomputeState()
    {
        PipelineState next;
        lock (_sinksLock)
        {
            next = (_sources.IsEmpty || _sinks.Count == 0)
                ? PipelineState.Idle
                : PipelineState.Ready;
        }

        PipelineState previous;
        lock (_stateLock)
        {
            previous = _state;
            _state = next;
        }

        if (previous != next)
            StateChanged?.Invoke(this, next);
    }

    #endregion

    #region Disposal

    /// <summary>Detach all sources and sinks. Does not dispose them.</summary>
    public void Dispose()
    {
        foreach (var src in _sources.Values)
            src.FrameCaptured -= OnSourceFrame;
        _sources.Clear();

        lock (_sinksLock) _sinks.Clear();

        RecomputeState();
    }

    #endregion
}
#endregion
