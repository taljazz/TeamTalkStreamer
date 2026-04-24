#region Usings
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.App.Modes;

#region Interface: IAppMode
/// <summary>
/// One "screen" or "mode" of the app. The mode stack pattern is
/// inherited from the Aircraft Explorer / CSharp Academy projects so
/// keyboard-driven navigation stays consistent across the user's
/// accessible apps.
/// </summary>
/// <remarks>
/// In this WPF app, modes are optional — MVVM bindings handle most
/// UI state. The mode stack is kept around for future features where
/// a stateful, keyboard-only flow (settings wizard, pairing flow, etc.)
/// is clearer as a mode than as a nested view model.
/// </remarks>
public interface IAppMode
{
    #region Identity

    /// <summary>Name announced by the screen reader when this mode
    /// becomes active. E.g. "Main menu", "Settings".</summary>
    string DisplayName { get; }

    #endregion

    #region Lifecycle

    /// <summary>Called when the mode is pushed onto the stack or
    /// becomes the visible top again after a Pop from above.</summary>
    Task OnEnterAsync();

    /// <summary>Called when the mode is popped or covered by a Push
    /// from above. Save transient state here.</summary>
    Task OnExitAsync();

    #endregion
}
#endregion
