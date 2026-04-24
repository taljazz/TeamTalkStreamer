#region Usings
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#endregion

namespace TeamTalkStreamer.Persistence.Config;

#region Class: JsonAppSettingsStore
/// <summary>
/// <see cref="IAppSettingsStore"/> backed by a JSON file in
/// <c>%APPDATA%\TeamTalkStreamer\settings.json</c>. Uses
/// <c>System.Text.Json</c> with a writeable, human-readable
/// configuration so users can hand-edit the file if they need to.
/// </summary>
/// <remarks>
/// Atomic writes: save to <c>settings.json.tmp</c> first, then rename
/// over the real file. This is the standard "write-then-rename" dance
/// and protects against partial writes if the process dies mid-save.
/// </remarks>
public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    #region Fields

    #region Paths
    // _directory is created on first save if it doesn't exist.
    // _settingsPath is the live file; _tempPath is the atomic-rename buffer.
    private readonly string _directory;
    private readonly string _settingsPath;
    private readonly string _tempPath;
    #endregion

    #region JSON options
    // Indented + camelCase for readability. WriteIndented costs a bit
    // on save but the files are tiny and hand-editability is valuable.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    #endregion

    #endregion

    #region Constructor

    /// <summary>Default constructor — uses the standard Roaming AppData
    /// location. Pass a custom path via the other ctor for tests.</summary>
    public JsonAppSettingsStore()
        : this(DefaultDirectory()) { }

    /// <summary>Construct against an explicit directory. Mostly useful
    /// in tests, or for portable-install scenarios.</summary>
    public JsonAppSettingsStore(string directory)
    {
        _directory = directory;
        _settingsPath = Path.Combine(_directory, "settings.json");
        _tempPath = _settingsPath + ".tmp";
    }

    #endregion

    #region IAppSettingsStore

    public string SettingsFilePath => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Missing file is normal on first run — return defaults.
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, JsonOpts, cancellationToken)
                .ConfigureAwait(false);

            // Null-on-parse means an empty or "null" file — treat as defaults.
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Corrupt file: quarantine it so the user can inspect later,
            // then return defaults so the app still starts.
            QuarantineCorrupt(_settingsPath);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_directory);

        // Serialize to the temp file first, then rename. Rename is
        // atomic on NTFS which is the only thing we target.
        await using (var stream = File.Create(_tempPath))
        {
            await JsonSerializer
                .SerializeAsync(stream, settings, JsonOpts, cancellationToken)
                .ConfigureAwait(false);
        }

        // File.Move with overwrite=true = atomic replace on NTFS.
        File.Move(_tempPath, _settingsPath, overwrite: true);
    }

    #endregion

    #region Helpers

    /// <summary>%APPDATA%\TeamTalkStreamer — the canonical location for
    /// per-user app settings on Windows.</summary>
    private static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TeamTalkStreamer");

    /// <summary>Rename a corrupt settings file to <c>settings.bad.N.json</c>
    /// so the app can overwrite with fresh defaults without losing the
    /// evidence of what went wrong.</summary>
    private static void QuarantineCorrupt(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(path)!;
            int n = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(dir, $"settings.bad.{n}.json");
                n++;
            } while (File.Exists(candidate) && n < 100);

            File.Move(path, candidate);
        }
        catch
        {
            // Best-effort: if we can't move it, we'll just overwrite it
            // next save. Better than failing to start.
        }
    }

    #endregion
}
#endregion
