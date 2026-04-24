#region Usings
using System;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.Core.Audio;

#region Class: AudioSourceBase
/// <summary>
/// Abstract base implementation of <see cref="IAudioSource"/>. Handles
/// the state machine, event raising, thread safety, and disposal so
/// concrete sources (WASAPI, Mobile, File) only implement the actual
/// capture logic via three template methods.
/// </summary>
/// <remarks>
/// Inheritance contract for concrete sources:
/// <list type="bullet">
///   <item><description><see cref="OnStartAsync"/> — open the device /
///     socket and start producing frames.</description></item>
///   <item><description><see cref="OnStopAsync"/> — stop producing and
///     release the device / socket.</description></item>
///   <item><description><see cref="OnDisposeAsync"/> — dispose of any
///     unmanaged resources.</description></item>
/// </list>
/// Concrete sources call <see cref="SetFormat"/> once at startup and
/// then <see cref="EmitFrame"/> whenever PCM is ready.
/// </remarks>
public abstract class AudioSourceBase : IAudioSource
{
    #region Fields

    #region Synchronization
    // Single lock protects state-machine transitions. Transitions are
    // fast and non-blocking so a plain Monitor lock beats SemaphoreSlim.
    private readonly object _stateLock = new();
    #endregion

    #region Backing fields for properties
    // Read under _stateLock to guarantee a consistent view across
    // multiple properties. Writes always happen under the lock too.
    private AudioSourceState _state = AudioSourceState.Stopped;
    private AudioFormat _format;
    private Exception? _lastError;
    #endregion

    #endregion

    #region Constructor
    // Identity is immutable — set here and never changed.

    /// <param name="id">Stable identifier. Pass <see cref="Guid.NewGuid"/>
    /// for a new source; re-use the stored Guid when restoring from preset.</param>
    /// <param name="displayName">Human-readable label for UI / screen reader.</param>
    /// <param name="type">Category used by router and UI.</param>
    protected AudioSourceBase(Guid id, string displayName, AudioSourceType type)
    {
        Id = id;
        DisplayName = displayName;
        Type = type;
    }

    #endregion

    #region Properties (IAudioSource)

    #region Identity
    // Immutable — captured in the constructor.
    public Guid Id { get; }
    public string DisplayName { get; }
    public AudioSourceType Type { get; }
    #endregion

    #region Live state
    // Lock on every read so property snapshots stay coherent when a
    // transition is happening on another thread.
    public AudioSourceState State
    {
        get { lock (_stateLock) return _state; }
    }

    public AudioFormat Format
    {
        get { lock (_stateLock) return _format; }
    }

    public Exception? LastError
    {
        get { lock (_stateLock) return _lastError; }
    }
    #endregion

    #endregion

    #region Events
    // FrameCaptured fires on the capture thread — handlers must be lean.
    // StateChanged is marshaled onto the thread pool to avoid re-entrancy
    // into the state lock (see TransitionTo below).
    public event EventHandler<AudioFrame>? FrameCaptured;
    public event EventHandler<AudioSourceState>? StateChanged;
    #endregion

    #region Public lifecycle
    // These wrap the concrete OnStartAsync / OnStopAsync with the state
    // machine so derived classes don't each re-implement it.

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        #region Pre-transition guard
        // Legal starts: from Stopped or Faulted. Starting/Capturing are
        // idempotent no-ops. Stopping is an error (caller should wait).
        lock (_stateLock)
        {
            if (_state is AudioSourceState.Starting or AudioSourceState.Capturing)
                return;
            if (_state is AudioSourceState.Stopping)
                throw new InvalidOperationException(
                    "Cannot start an audio source while it is stopping.");
            _lastError = null;
            TransitionToUnlocked(AudioSourceState.Starting);
        }
        #endregion

        #region Delegate to concrete implementation
        // Errors here become a Faulted transition so the UI can observe
        // them uniformly. We also re-throw so the immediate caller knows.
        try
        {
            await OnStartAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock) TransitionToUnlocked(AudioSourceState.Capturing);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _lastError = ex;
                TransitionToUnlocked(AudioSourceState.Faulted);
            }
            throw;
        }
        #endregion
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        #region Pre-transition guard
        // Legal stops: from Starting, Capturing, Paused, or Faulted.
        // Stopped/Stopping are idempotent no-ops.
        lock (_stateLock)
        {
            if (_state is AudioSourceState.Stopped or AudioSourceState.Stopping)
                return;
            TransitionToUnlocked(AudioSourceState.Stopping);
        }
        #endregion

        #region Delegate and finalize transition
        try
        {
            await OnStopAsync(cancellationToken).ConfigureAwait(false);
            lock (_stateLock) TransitionToUnlocked(AudioSourceState.Stopped);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _lastError = ex;
                TransitionToUnlocked(AudioSourceState.Faulted);
            }
            throw;
        }
        #endregion
    }

    #endregion

    #region Template methods (override in concrete sources)
    // Three hooks — the entire capture-specific surface. Keep the
    // contract narrow so new source types are easy to implement.

    /// <summary>Open the capture device / socket and start producing
    /// frames. Must call <see cref="SetFormat"/> before the first
    /// <see cref="EmitFrame"/>.</summary>
    protected abstract Task OnStartAsync(CancellationToken cancellationToken);

    /// <summary>Stop capture and close the device / socket. Must return
    /// only after the concrete source has stopped calling
    /// <see cref="EmitFrame"/>.</summary>
    protected abstract Task OnStopAsync(CancellationToken cancellationToken);

    /// <summary>Release unmanaged resources. Called from
    /// <see cref="DisposeAsync"/> after <see cref="StopAsync"/> has run.</summary>
    protected abstract ValueTask OnDisposeAsync();

    #endregion

    #region Helpers for derived classes
    // The only knobs derived classes touch besides the template methods.

    /// <summary>Set the PCM format the source emits. Call once, after
    /// the device is open but before the first <see cref="EmitFrame"/>.</summary>
    protected void SetFormat(AudioFormat format)
    {
        lock (_stateLock) _format = format;
    }

    /// <summary>Publish a captured frame to subscribers. Safe to call
    /// from any thread; handler runs synchronously on the caller's
    /// thread, so keep capture code non-blocking.</summary>
    protected void EmitFrame(AudioFrame frame)
    {
        // Frames are dropped in Paused rather than forwarded, so that
        // capture keeps flowing and resume is instantaneous. Reading
        // _state without the lock is fine for this branch: a stale
        // read just drops or forwards one extra frame at the boundary.
        if (_state is AudioSourceState.Paused) return;

        FrameCaptured?.Invoke(this, frame);
    }

    #endregion

    #region Private helpers

    /// <summary>State-machine transition. Caller MUST hold
    /// <see cref="_stateLock"/>. The <see cref="StateChanged"/> event is
    /// queued to the thread pool so handlers cannot re-enter the lock
    /// and deadlock us.</summary>
    private void TransitionToUnlocked(AudioSourceState next)
    {
        _state = next;

        // Snapshot the handler under the lock to avoid the classic
        // "unsubscribed right before invoke" race, then queue the
        // invocation so it runs outside the lock.
        var handler = StateChanged;
        if (handler is not null)
            ThreadPool.QueueUserWorkItem(_ => handler(this, next));
    }

    #endregion

    #region Disposal (IAsyncDisposable)
    // Graceful async disposal: stop if still running, then hand off to
    // the derived class for its own cleanup. DisposeAsync itself must
    // never throw — swallow exceptions during the implicit stop.

    public async ValueTask DisposeAsync()
    {
        if (State is not (AudioSourceState.Stopped or AudioSourceState.Faulted))
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch { /* intentional: dispose must not throw */ }
        }

        await OnDisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    #endregion
}
#endregion
