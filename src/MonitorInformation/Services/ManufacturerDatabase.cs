using System.IO;
using System.Text.Json;

namespace MonitorInformation.Services;

public static class ManufacturerDatabase
{
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, string>? _manufacturers;

    private static readonly IReadOnlyDictionary<string, string> BuiltInManufacturers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

    public static string Resolve(string manufacturerId)
    {
        var manufacturers = GetManufacturers();
        return manufacturers.TryGetValue(manufacturerId, out var name) ? name : manufacturerId;
    }

    public static void Reload()
    {
        lock (Gate)
        {
            _manufacturers = LoadFromDisk();
        }
    }

    private static IReadOnlyDictionary<string, string> GetManufacturers()
    {
        lock (Gate)
        {
            return _manufacturers ??= LoadFromDisk();
        }
    }

    private static IReadOnlyDictionary<string, string> LoadFromDisk()
    {
        var result = new Dictionary<string, string>(BuiltInManufacturers, StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(AppContext.BaseDirectory, "resources", "manufacturers", "pnp-vendors.json");
        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var external = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (external is null)
            {
                return result;
            }

            foreach (var pair in external)
            {
                var key = pair.Key.Trim().ToUpperInvariant();
                var value = pair.Value.Trim();
                if (key.Length == 3 && value.Length > 0)
                {
                    result[key] = value;
                }
            }
        }
        catch
        {
            // A user-editable vendor file must not block EDID parsing.
        }

        return result;
    }
}
