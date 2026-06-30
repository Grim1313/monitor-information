using System.Text;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public static class EdidParser
{
    private static readonly IReadOnlyDictionary<string, string> KnownManufacturers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ACR"] = "Acer",
        ["AOC"] = "AOC",
        ["APP"] = "Apple",
        ["AUO"] = "AU Optronics",
        ["BNQ"] = "BenQ",
        ["BOE"] = "BOE",
        ["CMN"] = "Chimei Innolux",
        ["DEL"] = "Dell",
        ["EIZ"] = "EIZO",
        ["GSM"] = "LG Electronics",
        ["HWP"] = "HP",
        ["IVM"] = "Iiyama",
        ["LEN"] = "Lenovo",
        ["LGD"] = "LG Display",
        ["LPL"] = "LG Philips",
        ["MSI"] = "MSI",
        ["NEC"] = "NEC",
        ["PHL"] = "Philips",
        ["SAM"] = "Samsung",
        ["SDC"] = "Samsung Display",
        ["SEC"] = "Samsung",
        ["SNY"] = "Sony",
        ["VSC"] = "ViewSonic"
    };

    public static EdidInfo? Parse(byte[]? edidBytes)
    {
        if (edidBytes is null || edidBytes.Length < 128)
        {
            return null;
        }

        var block = edidBytes.Take(Math.Max(128, edidBytes.Length)).ToArray();
        var manufacturerId = DecodeManufacturerId(block[8], block[9]);
        var productCode = (ushort)(block[10] | (block[11] << 8));
        var serialNumber = (uint)(block[12] | (block[13] << 8) | (block[14] << 16) | (block[15] << 24));
        var manufactureWeek = block[16];
        var manufactureYear = block[17] == 0 ? 0 : 1990 + block[17];

        string? displayName = null;
        string? serialText = null;
        string? descriptorText = null;
        var preferred = (Width: 0, Height: 0);
        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (preferred.Width == 0 && (block[offset] != 0 || block[offset + 1] != 0))
            {
                preferred = DecodeDetailedTiming(block, offset);
            }

            if (block[offset] != 0 || block[offset + 1] != 0 || block[offset + 2] != 0)
            {
                continue;
            }

            var text = ReadDescriptorText(block, offset + 5, 13);
            switch (block[offset + 3])
            {
                case 0xFC:
                    displayName = text;
                    break;
                case 0xFF:
                    serialText = text;
                    break;
                case 0xFE:
                    descriptorText = text;
                    break;
            }
        }

        return new EdidInfo
        {
            RawBytes = block,
            ChecksumValid = IsChecksumValid(block),
            ManufacturerId = manufacturerId,
            ManufacturerName = KnownManufacturers.TryGetValue(manufacturerId, out var name) ? name : manufacturerId,
            ProductCode = productCode,
            SerialNumber = serialNumber,
            ManufactureWeek = manufactureWeek,
            ManufactureYear = manufactureYear,
            EdidMajorVersion = block[18],
            EdidMinorVersion = block[19],
            WidthCentimeters = block[21],
            HeightCentimeters = block[22],
            Gamma = block[23] == 0xFF ? null : (block[23] + 100) / 100.0,
            ExtensionBlocks = block[126],
            DisplayName = displayName ?? descriptorText,
            SerialNumberText = serialText,
            DescriptorText = descriptorText,
            PreferredWidth = preferred.Width,
            PreferredHeight = preferred.Height
        };
    }

    private static (int Width, int Height) DecodeDetailedTiming(byte[] block, int offset)
    {
        var width = block[offset + 2] | ((block[offset + 4] & 0xF0) << 4);
        var height = block[offset + 5] | ((block[offset + 7] & 0xF0) << 4);
        return (width, height);
    }

    private static bool IsChecksumValid(byte[] block)
    {
        if (block.Length < 128)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 128; i++)
        {
            sum = (sum + block[i]) & 0xFF;
        }

        return sum == 0;
    }

    private static string DecodeManufacturerId(byte first, byte second)
    {
        var value = (first << 8) | second;
        Span<char> chars =
        [
            (char)('A' + ((value >> 10) & 0x1F) - 1),
            (char)('A' + ((value >> 5) & 0x1F) - 1),
            (char)('A' + (value & 0x1F) - 1)
        ];

        return new string(chars);
    }

    private static string? ReadDescriptorText(byte[] block, int offset, int count)
    {
        var bytes = block.Skip(offset).Take(count)
            .TakeWhile(b => b is not 0x00 and not 0x0A and not 0x0D)
            .ToArray();
        var text = Encoding.ASCII.GetString(bytes).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
