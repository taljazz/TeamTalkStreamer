#region Usings
// Pure enum — no imports.
#endregion

namespace TeamTalkStreamer.App.Modes;

#region Enum: ModeResult
/// <summary>
/// Instruction to <see cref="AppModeManager"/> about what to do after
/// a mode's input handler returns. Same four values as Aircraft
/// Explorer / CSharp Academy for consistency across the user's apps.
/// </summary>
public enum ModeResult
{
    /// <summary>Stay on the current mode; no stack change.</summary>
    None = 0,

    /// <summary>Push a new mode onto the stack on top of the current one.</summary>
    Push = 1,

    /// <summary>Pop the current mode off the stack, reactivating the one below.</summary>
    Pop = 2,

    /// <summary>Replace the current mode with a new one (no popping cascade).</summary>
    Switch = 3,

    /// <summary>Exit the whole app.</summary>
    Quit = 4,
}
#endregion
