namespace MonitorInformation.Models;

public sealed class MonitorInfo
{
    public required string DisplayName { get; init; }

    public required string AdapterName { get; init; }

    public required string DeviceName { get; init; }

    public required string DeviceId { get; init; }

    public required bool IsPrimary { get; init; }

    public required int CurrentWidth { get; init; }

    public required int CurrentHeight { get; init; }

    public required int RefreshRate { get; init; }

    public EdidInfo? Edid { get; init; }

    public string CurrentModeText => CurrentWidth > 0 && CurrentHeight > 0
        ? $"{CurrentWidth} x {CurrentHeight}"
        : "";

    public string SummaryLine
    {
        get
        {
            var mode = CurrentModeText;
            var vendor = Edid?.ManufacturerName;

            if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(vendor))
            {
                return $"{mode} - {vendor}";
            }

            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            return DeviceName;
        }
    }

    public string RawEdidHex => Edid is null ? "" : EdidFormatter.ToHexDump(Edid.RawBytes);
}

internal static class EdidFormatter
{
    public static string ToHexDump(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "";
        }

        var lines = new List<string>((bytes.Length + 15) / 16);
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);
            var hex = string.Join(" ", bytes.Skip(offset).Take(count).Select(b => b.ToString("X2")));
            lines.Add($"{offset:X4}: {hex}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
