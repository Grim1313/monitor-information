using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class OnlineSpecCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "online-specs");

    public OnlineSpecResult? TryRead(string key)
    {
        try
        {
            var path = Path.Combine(_cachePath, $"{key}.json");
            if (!File.Exists(path))
            {
                return null;
            }

            var result = JsonSerializer.Deserialize<OnlineSpecResult>(File.ReadAllText(path), JsonOptions);
            if (result is null || DateTimeOffset.UtcNow - result.RetrievedAt > TimeSpan.FromDays(30))
            {
                return null;
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    public void TryWrite(string key, OnlineSpecResult result)
    {
        try
        {
            Directory.CreateDirectory(_cachePath);
            File.WriteAllText(Path.Combine(_cachePath, $"{key}.json"), JsonSerializer.Serialize(result, JsonOptions));
        }
        catch
        {
            // Online cache is optional. Read-only portable folders should not block lookup.
        }
    }

    public static string CreateKey(MonitorIdentity identity)
    {
        var input = string.Join("|",
            identity.DisplayName,
            identity.ManufacturerName,
            identity.ManufacturerId,
            identity.ProductCodeHex,
            identity.WidthPixels,
            identity.HeightPixels,
            identity.DiagonalInches?.ToString("0.0"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToUpperInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }
}
