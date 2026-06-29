using System.Runtime.InteropServices;
using Microsoft.Win32;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class MonitorReader
{
    private const int EnumCurrentSettings = -1;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;
    private const int DisplayDevicePrimaryDevice = 0x00000004;

    public IReadOnlyList<MonitorInfo> GetActiveMonitors()
    {
        var monitors = new List<MonitorInfo>();

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

                if (string.IsNullOrWhiteSpace(monitor.DeviceID))
                {
                    continue;
                }

                adapterHadMonitor = true;
                var edid = EdidParser.Parse(ReadEdid(monitor.DeviceID));
                monitors.Add(new MonitorInfo
                {
                    DisplayName = FirstNonEmpty(edid?.DisplayName, monitor.DeviceString, adapter.DeviceString, adapter.DeviceName),
                    AdapterName = FirstNonEmpty(adapter.DeviceString, adapter.DeviceName),
                    DeviceName = FirstNonEmpty(monitor.DeviceString, monitor.DeviceName),
                    DeviceId = monitor.DeviceID,
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
}
