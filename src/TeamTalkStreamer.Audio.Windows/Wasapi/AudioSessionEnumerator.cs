#region Usings
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Class: AudioSessionEnumerator
/// <summary>
/// Enumerates audio sessions that are currently producing sound on
/// the default render endpoint. Lets the UI show a short list of
/// "apps actually making noise right now" rather than every running
/// process on the machine.
/// </summary>
/// <remarks>
/// Uses NAudio's <c>AudioSessionManager</c> / <c>AudioSessionControl</c>
/// wrappers around the native <c>IAudioSessionManager2</c> APIs. Results
/// are deduplicated by process name (a single app can own multiple
/// sessions — we only show it once in the picker).
/// </remarks>
public static class AudioSessionEnumerator
{
    #region Public API

    /// <summary>
    /// Return every active (non-expired) audio session on the default
    /// render device, deduplicated by process. Dead / stopped sessions
    /// and our own process are filtered out.
    /// </summary>
    public static IReadOnlyList<AudioSessionInfo> EnumerateActiveSessions()
    {
        var results = new List<AudioSessionInfo>();
        var seenProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #region Open the default render device
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            return results;

        using var device = enumerator.GetDefaultAudioEndpoint(
            DataFlow.Render, Role.Multimedia);
        #endregion

        #region Walk the session manager
        // AudioSessionManager2 is lazily initialized on first access; its
        // Sessions collection is a snapshot at the moment of access.
        var sessions = device.AudioSessionManager.Sessions;
        if (sessions is null) return results;

        int ownPid = Environment.ProcessId;

        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!TryBuildSessionInfo(session, ownPid, out var info)) continue;
            if (!seenProcessNames.Add(info.ProcessName)) continue;
            results.Add(info);
        }
        #endregion

        return results;
    }

    #endregion

    #region Private helpers

    /// <summary>
    /// Try to turn a raw <see cref="AudioSessionControl"/> into a
    /// friendly <see cref="AudioSessionInfo"/>. Returns false for
    /// system / inactive / self-owned sessions so the picker list
    /// stays focused on third-party running apps.
    /// </summary>
    private static bool TryBuildSessionInfo(
        AudioSessionControl session,
        int ownPid,
        out AudioSessionInfo info)
    {
        info = default!;

        #region Filter out uninteresting sessions
        // Inactive: stream is present but not currently playing — skip.
        // Expired: cleaned-up session that the API hasn't purged yet.
        if (session.State != AudioSessionState.AudioSessionStateActive)
            return false;

        uint pid;
        try { pid = session.GetProcessID; }
        catch { return false; }

        if (pid == 0) return false;                         // system sounds
        if ((int)pid == ownPid) return false;               // our own feedback tones
        #endregion

        #region Resolve process name
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
        }
        catch
        {
            // Process exited between GetProcessID and GetProcessById.
            return false;
        }

        if (string.IsNullOrWhiteSpace(processName)) return false;
        #endregion

        #region Resolve display name
        // DisplayName comes from SetDisplayName if the app set one;
        // otherwise it's empty. Fall back to the process name.
        string displayName = "";
        try { displayName = session.DisplayName ?? ""; }
        catch { /* COM can throw on dead sessions; just skip DisplayName */ }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = processName;
        #endregion

        info = new AudioSessionInfo(
            ProcessId: (int)pid,
            ProcessName: processName,
            DisplayName: displayName);
        return true;
    }

    #endregion
}
#endregion
