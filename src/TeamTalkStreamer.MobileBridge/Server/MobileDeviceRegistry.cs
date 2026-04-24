#region Usings
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TeamTalkStreamer.MobileBridge.Protocol;
#endregion

namespace TeamTalkStreamer.MobileBridge.Server;

#region Class: MobileDeviceRegistry
/// <summary>
/// Tracks every mobile device currently (or recently) connected to
/// this server. Keyed by the stable device Guid so a reconnect maps
/// back to the same entry without creating a duplicate.
/// </summary>
public sealed class MobileDeviceRegistry
{
    #region Record: Entry
    /// <summary>Snapshot of what the server knows about one device.</summary>
    public sealed record Entry(
        Guid DeviceId,
        string DisplayName,
        MobileDeviceState State,
        DateTimeOffset LastSeenUtc);
    #endregion

    #region Fields
    // ConcurrentDictionary so UI and WebSocket threads don't need a
    // shared lock to read/update.
    private readonly ConcurrentDictionary<Guid, Entry> _devices = new();
    #endregion

    #region Events

    /// <summary>Raised whenever an entry is added, removed, or has its
    /// state updated.</summary>
    public event EventHandler<Entry>? EntryChanged;

    #endregion

    #region Public API

    /// <summary>Snapshot of all current entries.</summary>
    public IReadOnlyCollection<Entry> Snapshot() => _devices.Values.ToArray();

    /// <summary>Create or update an entry. Returns the new entry.</summary>
    public Entry Upsert(Guid deviceId, string displayName, MobileDeviceState state)
    {
        var entry = new Entry(deviceId, displayName, state, DateTimeOffset.UtcNow);
        _devices[deviceId] = entry;
        EntryChanged?.Invoke(this, entry);
        return entry;
    }

    /// <summary>Update just the state of an existing entry. No-op if
    /// the device isn't in the registry.</summary>
    public void TransitionState(Guid deviceId, MobileDeviceState state)
    {
        if (_devices.TryGetValue(deviceId, out var existing))
        {
            var updated = existing with
            {
                State = state,
                LastSeenUtc = DateTimeOffset.UtcNow,
            };
            _devices[deviceId] = updated;
            EntryChanged?.Invoke(this, updated);
        }
    }

    /// <summary>Remove an entry. Fires <see cref="EntryChanged"/> with
    /// <see cref="MobileDeviceState.Disconnected"/> before removing.</summary>
    public void Remove(Guid deviceId)
    {
        if (_devices.TryRemove(deviceId, out var existing))
        {
            var finalEntry = existing with
            {
                State = MobileDeviceState.Disconnected,
                LastSeenUtc = DateTimeOffset.UtcNow,
            };
            EntryChanged?.Invoke(this, finalEntry);
        }
    }

    #endregion
}
#endregion
