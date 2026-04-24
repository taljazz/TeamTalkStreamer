#region Usings
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.Persistence.Config;

#region Interface: IAppSettingsStore
/// <summary>
/// Loads and saves the application's <see cref="AppSettings"/>.
/// Abstracted so tests and future backends (registry, roaming profile,
/// cloud sync) can swap in.
/// </summary>
public interface IAppSettingsStore
{
    #region File location

    /// <summary>Absolute path to the settings file the store reads and
    /// writes. Useful for the "open settings folder" menu item and
    /// diagnostic logging.</summary>
    string SettingsFilePath { get; }

    #endregion

    #region Load / Save

    /// <summary>Read settings from storage. If the file is missing or
    /// malformed, returns a fresh <see cref="AppSettings"/> populated
    /// with defaults.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Write settings to storage. Creates the parent directory
    /// if it doesn't exist. Writes atomically (temp file + rename) so
    /// a crash mid-write can't corrupt the live file.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    #endregion
}
#endregion
