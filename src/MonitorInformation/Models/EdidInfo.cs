namespace MonitorInformation.Models;

public sealed class EdidInfo
{
    public required byte[] RawBytes { get; init; }

    public required bool ChecksumValid { get; init; }

    public required string ManufacturerId { get; init; }

    public required string ManufacturerName { get; init; }

    public required ushort ProductCode { get; init; }

    public required uint SerialNumber { get; init; }

    public required int ManufactureWeek { get; init; }

    public required int ManufactureYear { get; init; }

    public required int EdidMajorVersion { get; init; }

    public required int EdidMinorVersion { get; init; }

    public required int WidthCentimeters { get; init; }

    public required int HeightCentimeters { get; init; }

    public required double? Gamma { get; init; }

    public required int ExtensionBlocks { get; init; }

    public string? DisplayName { get; init; }

    public string? SerialNumberText { get; init; }

    public string ProductCodeHex => $"0x{ProductCode:X4}";

    public string VersionText => $"{EdidMajorVersion}.{EdidMinorVersion}";

    public string? GammaText => Gamma.HasValue ? Gamma.Value.ToString("0.00") : null;

    public string ManufactureDateText
    {
        get
        {
            if (ManufactureYear <= 0)
            {
                return "";
            }

            return ManufactureWeek is > 0 and <= 53
                ? $"Week {ManufactureWeek}, {ManufactureYear}"
                : ManufactureYear.ToString();
        }
    }
}
