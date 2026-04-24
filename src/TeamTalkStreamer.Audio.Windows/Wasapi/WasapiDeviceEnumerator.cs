#region Usings
using System.Collections.Generic;
using NAudio.CoreAudioApi;
#endregion

namespace TeamTalkStreamer.Audio.Windows.Wasapi;

#region Class: WasapiDeviceEnumerator
/// <summary>
/// Thin wrapper over NAudio's <c>MMDeviceEnumerator</c> that returns
/// the list of render (playback) devices we can loopback-capture.
/// Kept separate from <c>WasapiLoopbackSource</c> so the UI can list
/// devices without instantiating a source.
/// </summary>
/// <remarks>
/// Each returned <see cref="DeviceInfo"/> carries the stable
/// <c>MMDevice.ID</c>, which is what we persist in settings — device
/// names can change between Windows versions and driver updates, but
/// the ID is stable across reboots.
/// </remarks>
public static class WasapiDeviceEnumerator
{
    #region Record: DeviceInfo
    /// <summary>Minimal device descriptor surfaced to the UI and
    /// persistence layers.</summary>
    public readonly record struct DeviceInfo(
        string Id,
        string FriendlyName,
        bool IsDefault);
    #endregion

    #region Public API

    /// <summary>Return every active render endpoint on the system, with
    /// the current default marked.</summary>
    public static IReadOnlyList<DeviceInfo> EnumerateRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        // Capture the default's ID so we can flag it in the returned list.
        string? defaultId = null;
        if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render, Role.Multimedia);
            defaultId = defaultDevice.ID;
        }

        var result = new List<DeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(
                     DataFlow.Render, DeviceState.Active))
        {
            try
            {
                result.Add(new DeviceInfo(
                    Id: device.ID,
                    FriendlyName: device.FriendlyName,
                    IsDefault: device.ID == defaultId));
            }
            finally
            {
                device.Dispose();
            }
        }

        return result;
    }

    #endregion
}
#endregion
