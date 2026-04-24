#region Usings
using System.Collections.Generic;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.App.Modes;

#region Class: AppModeManager
/// <summary>
/// Tracks the current mode stack. In this WPF app the manager is
/// reserved for stateful keyboard flows (settings wizard, device
/// pairing walk-through, etc.); the primary MVVM-based UI is driven
/// by <c>MainViewModel</c>, not by modes.
/// </summary>
public sealed class AppModeManager
{
    #region Fields
    // Plain Stack<T> — all mutation happens on the UI thread so no
    // concurrent access protection is needed.
    private readonly Stack<IAppMode> _stack = new();
    #endregion

    #region Properties

    /// <summary>The topmost active mode, or null when the stack is empty.</summary>
    public IAppMode? Current => _stack.Count == 0 ? null : _stack.Peek();

    /// <summary>Depth of the stack. Useful for "are we at the root?" checks.</summary>
    public int Depth => _stack.Count;

    #endregion

    #region Stack operations

    /// <summary>Push a mode onto the stack. Current mode's OnExit fires
    /// first, then the new mode's OnEnter.</summary>
    public async Task PushAsync(IAppMode mode)
    {
        if (Current is { } existing)
            await existing.OnExitAsync().ConfigureAwait(true);

        _stack.Push(mode);
        await mode.OnEnterAsync().ConfigureAwait(true);
    }

    /// <summary>Pop the top mode. No-op if the stack is empty.</summary>
    public async Task PopAsync()
    {
        if (_stack.Count == 0) return;

        var top = _stack.Pop();
        await top.OnExitAsync().ConfigureAwait(true);

        if (Current is { } newTop)
            await newTop.OnEnterAsync().ConfigureAwait(true);
    }

    /// <summary>Replace the top mode (Pop + Push without cascading
    /// enter/exit on the mode below).</summary>
    public async Task SwitchAsync(IAppMode mode)
    {
        if (Current is { } existing)
        {
            await existing.OnExitAsync().ConfigureAwait(true);
            _stack.Pop();
        }

        _stack.Push(mode);
        await mode.OnEnterAsync().ConfigureAwait(true);
    }

    #endregion
}
#endregion
