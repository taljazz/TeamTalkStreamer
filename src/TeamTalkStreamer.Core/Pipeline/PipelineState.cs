#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.Core.Pipeline;

#region Enum: PipelineState
/// <summary>
/// High-level state of the routing pipeline (router + mixer + sinks
/// combined). Distinct from any individual source or sink state — the
/// UI surfaces this as the single "streaming now" indicator.
/// </summary>
public enum PipelineState
{
    /// <summary>No sources attached, or no sinks connected. Nothing to do.</summary>
    Idle = 0,

    /// <summary>At least one source and one sink are registered, but
    /// no source is currently capturing. The router is standing by.</summary>
    Ready = 1,

    /// <summary>Frames are actively flowing from sources through the
    /// mixer to the sinks. This is what the user means by "streaming."</summary>
    Running = 2,

    /// <summary>User asked to pause the pipeline: sources keep running
    /// but the router stops forwarding to sinks.</summary>
    Paused = 3,

    /// <summary>Transient: pipeline is draining buffers before shutting
    /// down.</summary>
    Stopping = 4,

    /// <summary>A sink or source fault propagated up. Inspect logs; user
    /// can reset to <c>Idle</c>.</summary>
    Faulted = 99,
}
#endregion
