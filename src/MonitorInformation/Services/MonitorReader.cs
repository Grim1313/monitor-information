using System.Runtime.InteropServices;
using Microsoft.Win32;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class MonitorReader
{
    private const int EnumCurrentSettings = -1;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;
    private const uint EddGetDeviceInterfaceName = 0x00000001;

    public IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var monitors = new List<MonitorInfo>();
        var registryEdids = ReadRegistryEdids();
        var usedRegistryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapter = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
            {
                break;
            }

            if ((adapter.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
            {
                continue;
            }

            var mode = GetDisplayMode(adapter.DeviceName);
            var adapterHadMonitor = false;

            for (uint monitorIndex = 0; ; monitorIndex++)
            {
                var monitor = CreateDisplayDevice();
                if (!EnumDisplayDevices(adapter.DeviceName, monitorIndex, ref monitor, 0))
                {
                    break;
                }

                var monitorInterface = CreateDisplayDevice();
                EnumDisplayDevices(adapter.DeviceName, monitorIndex, ref monitorInterface, EddGetDeviceInterfaceName);

                if (string.IsNullOrWhiteSpace(monitor.DeviceID) && string.IsNullOrWhiteSpace(monitorInterface.DeviceID))
                {
                    continue;
                }

                adapterHadMonitor = true;
                var deviceIds = new[] { monitor.DeviceID, monitorInterface.DeviceID }
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var edidEntry = FindEdid(deviceIds, mode, registryEdids, usedRegistryPaths);
                var edid = edidEntry?.Edid;
                if (edidEntry is not null)
                {
                    usedRegistryPaths.Add(edidEntry.RegistryPath);
                }

                monitors.Add(new MonitorInfo
                {
                    DisplayName = FirstNonEmpty(edid?.DisplayName, monitor.DeviceString, adapter.DeviceString, adapter.DeviceName),
                    AdapterName = FirstNonEmpty(adapter.DeviceString, adapter.DeviceName),
                    DeviceName = FirstNonEmpty(monitor.DeviceString, monitor.DeviceName),
                    DeviceId = FirstNonEmpty(monitor.DeviceID, monitorInterface.DeviceID),
                    IsPrimary = (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0,
                    CurrentWidth = mode.Width,
                    CurrentHeight = mode.Height,
                    RefreshRate = mode.RefreshRate,
                    Edid = edid
                });
            }

            if (!adapterHadMonitor)
            {
                monitors.Add(new MonitorInfo
                {
                    DisplayName = FirstNonEmpty(adapter.DeviceString, adapter.DeviceName),
                    AdapterName = FirstNonEmpty(adapter.DeviceString, adapter.DeviceName),
                    DeviceName = adapter.DeviceName,
                    DeviceId = adapter.DeviceID,
                    IsPrimary = (adapter.StateFlags & DisplayDevicePrimaryDevice) != 0,
                    CurrentWidth = mode.Width,
                    CurrentHeight = mode.Height,
                    RefreshRate = mode.RefreshRate
                });
            }
        }

        return monitors;
    }

    private static RegistryEdidEntry? FindEdid(
        IReadOnlyList<string> deviceIds,
        (int Width, int Height, int RefreshRate) mode,
        IReadOnlyList<RegistryEdidEntry> registryEdids,
        HashSet<string> usedRegistryPaths)
    {
        foreach (var deviceId in deviceIds)
        {
            var direct = EdidParser.Parse(ReadEdid(deviceId));
            if (direct is not null)
            {
                return new RegistryEdidEntry("", "", "", direct);
            }
        }

        var best = registryEdids
            .Where(entry => !usedRegistryPaths.Contains(entry.RegistryPath))
            .Select(entry => new ScoredRegistryEdid(entry, ScoreRegistryEdid(entry, deviceIds, mode)))
            .OrderByDescending(entry => entry.Score)
            .FirstOrDefault();

        if (best is null)
        {
            return null;
        }

        if (best.Score >= 40 || registryEdids.Count(entry => !usedRegistryPaths.Contains(entry.RegistryPath)) == 1)
        {
            return best.Entry;
        }

        return null;
    }

    private static int ScoreRegistryEdid(
        RegistryEdidEntry entry,
        IReadOnlyList<string> deviceIds,
        (int Width, int Height, int RefreshRate) mode)
    {
        var score = entry.Edid.ChecksumValid ? 20 : 0;
        foreach (var deviceId in deviceIds)
        {
            if (deviceId.Contains(entry.DisplayKey, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            if (deviceId.Contains(entry.InstanceKey, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
        }

        if (entry.Edid.PreferredWidth == mode.Width && entry.Edid.PreferredHeight == mode.Height)
        {
            score += 35;
        }

        return score;
    }

    private static (int Width, int Height, int RefreshRate) GetDisplayMode(string deviceName)
    {
        var mode = CreateDevMode();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
        {
            return (0, 0, 0);
        }

        return ((int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency);
    }

    private static byte[]? ReadEdid(string deviceId)
    {
        foreach (var path in GetRegistryPaths(deviceId))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("EDID") is byte[] edid)
                {
                    return edid;
                }
            }
            catch
            {
                // Some monitor registry nodes may be inaccessible. Try the next path.
            }
        }

        return null;
    }

    private static IReadOnlyList<RegistryEdidEntry> ReadRegistryEdids()
    {
        var entries = new List<RegistryEdidEntry>();
        const string displayRoot = @"SYSTEM\CurrentControlSet\Enum\DISPLAY";

        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(displayRoot);
            if (root is null)
            {
                return entries;
            }

            foreach (var displayKey in root.GetSubKeyNames())
            {
                using var display = root.OpenSubKey(displayKey);
                if (display is null)
                {
                    continue;
                }

                foreach (var instanceKey in display.GetSubKeyNames())
                {
                    var registryPath = $@"{displayRoot}\{displayKey}\{instanceKey}\Device Parameters";
                    using var parameters = Registry.LocalMachine.OpenSubKey(registryPath);
                    if (parameters?.GetValue("EDID") is not byte[] edidBytes)
                    {
                        continue;
                    }

                    var edid = EdidParser.Parse(edidBytes);
                    if (edid is not null)
                    {
                        entries.Add(new RegistryEdidEntry(displayKey, instanceKey, registryPath, edid));
                    }
                }
            }
        }
        catch
        {
            // Registry-wide EDID discovery is a best-effort fallback.
        }

        return entries;
    }

    private static IEnumerable<string> GetRegistryPaths(string deviceId)
    {
        yield return $@"SYSTEM\CurrentControlSet\Enum\{deviceId}\Device Parameters";

        if (deviceId.StartsWith(@"MONITOR\", StringComparison.OrdinalIgnoreCase))
        {
            yield return $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{deviceId[8..]}\Device Parameters";
        }

        var parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            yield return $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{string.Join('\\', parts.Skip(2))}\Device Parameters";
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static DisplayDevice CreateDisplayDevice()
    {
        return new DisplayDevice
        {
            cb = Marshal.SizeOf<DisplayDevice>()
        };
    }

    private static DevMode CreateDevMode()
    {
        return new DevMode
        {
            dmSize = (ushort)Marshal.SizeOf<DevMode>()
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    private sealed record RegistryEdidEntry(string DisplayKey, string InstanceKey, string RegistryPath, EdidInfo Edid);

    private sealed record ScoredRegistryEdid(RegistryEdidEntry Entry, int Score);
}
