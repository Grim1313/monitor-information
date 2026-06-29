using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonitorInformation.Models;

namespace MonitorInformation.Services;

public sealed class EnergyStarSpecProvider : ISpecProvider
{
    private const string DatasetEndpoint = "https://data.energystar.gov/resource/qbg3-d468.json";
    private const string DatasetPage = "https://data.energystar.gov/Active-Specifications/ENERGY-STAR-Certified-Displays/qbg3-d468";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string Name => "ENERGY STAR Certified Displays";

    public async Task<OnlineSpecResult?> SearchAsync(MonitorIdentity identity, CancellationToken cancellationToken)
    {
        var query = BuildQuery(identity);
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        var url = $"{DatasetEndpoint}?$limit=10&$q={Uri.EscapeDataString(query)}";
        await using var stream = await HttpClient.GetStreamAsync(url, timeout.Token).ConfigureAwait(false);
        var rows = await JsonSerializer.DeserializeAsync<List<EnergyStarDisplayRow>>(stream, cancellationToken: timeout.Token).ConfigureAwait(false);

        var best = rows?
            .Select(row => new ScoredRow(row, Score(row, identity)))
            .Where(row => row.Score >= 45)
            .OrderByDescending(row => row.Score)
            .FirstOrDefault();

        return best is null ? null : ToResult(best.Row, best.Score);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MonitorInformation/0.1.0 (+https://github.com/)");
        return client;
    }

    private static string BuildQuery(MonitorIdentity identity)
    {
        var displayName = Cleanup(identity.DisplayName);
        if (string.IsNullOrWhiteSpace(displayName) || IsGenericDisplayName(displayName))
        {
            return "";
        }

        var manufacturer = Cleanup(identity.ManufacturerName);
        if (!string.IsNullOrWhiteSpace(manufacturer) && !string.Equals(manufacturer, identity.ManufacturerId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{manufacturer} {displayName}";
        }

        return displayName;
    }

    private static int Score(EnergyStarDisplayRow row, MonitorIdentity identity)
    {
        var score = 0;
        var displayName = Normalize(identity.DisplayName);
        var manufacturer = Normalize(identity.ManufacturerName);
        var brand = Normalize(row.BrandName);
        var modelName = Normalize(row.ModelName);
        var modelNumber = Normalize(row.ModelNumber);
        var additional = Normalize(row.AdditionalModelInformation);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            if (modelName == displayName || modelNumber == displayName)
            {
                score += 70;
            }
            else if (modelName.Contains(displayName, StringComparison.Ordinal) ||
                     modelNumber.Contains(displayName, StringComparison.Ordinal) ||
                     additional.Contains(displayName, StringComparison.Ordinal))
            {
                score += 50;
            }
            else if (displayName.Contains(modelName, StringComparison.Ordinal) && modelName.Length >= 4)
            {
                score += 35;
            }
        }

        if (!string.IsNullOrWhiteSpace(manufacturer) && !string.IsNullOrWhiteSpace(brand) &&
            (brand.Contains(manufacturer, StringComparison.Ordinal) || manufacturer.Contains(brand, StringComparison.Ordinal)))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(row.NativeResolutionPixels) &&
            row.NativeResolutionPixels.Contains($"{identity.WidthPixels} x {identity.HeightPixels}", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (identity.DiagonalInches is not null &&
            double.TryParse(row.ScreenSizeInches, NumberStyles.Float, CultureInfo.InvariantCulture, out var screenSize) &&
            Math.Abs(screenSize - identity.DiagonalInches.Value) <= 1.0)
        {
            score += 10;
        }

        return Math.Min(score, 100);
    }

    private OnlineSpecResult ToResult(EnergyStarDisplayRow row, int score)
    {
        var fields = new List<OnlineSpecField>();
        Add(fields, "Brand", row.BrandName);
        Add(fields, "Model name", row.ModelName);
        Add(fields, "Model number", row.ModelNumber);
        Add(fields, "Display type", row.DisplayType);
        Add(fields, "Panel type", row.PanelType);
        Add(fields, "Native resolution", row.NativeResolutionPixels);
        Add(fields, "Screen size", FormatInches(row.ScreenSizeInches));
        Add(fields, "Interfaces", row.SignalOrDataInterfaces);
        Add(fields, "Features", row.ModelFeatures);
        Add(fields, "HDR", row.HighDynamicRangeHdr);
        Add(fields, "Contrast ratio", row.DisplayContrastRatio);
        Add(fields, "Maximum luminance", FormatNits(row.MaximumLuminanceCandelas));
        Add(fields, "USB-C power delivery", FormatWatts(row.MaximumPowerDeliveryW));
        Add(fields, "On mode power", FormatWatts(row.OnModePowerWatts));
        Add(fields, "Sleep mode power", FormatWatts(row.SleepModePowerWatts));
        Add(fields, "Off mode power", FormatWatts(row.OffModePowerWatts));
        Add(fields, "Date certified", FormatDate(row.DateCertified));
        Add(fields, "Markets", row.Markets);
        Add(fields, "ENERGY STAR ID", row.EnergyStarModelIdentifier);

        return new OnlineSpecResult
        {
            ProviderName = Name,
            SourceUrl = DatasetPage,
            RetrievedAt = DateTimeOffset.UtcNow,
            Confidence = score,
            MatchSummary = $"{row.BrandName} {row.ModelName}".Trim(),
            Fields = fields
        };
    }

    private static void Add(List<OnlineSpecField> fields, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            fields.Add(new OnlineSpecField { Name = name, Value = value.Trim() });
        }
    }

    private static string? FormatInches(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : $"{value} in";
    }

    private static string? FormatWatts(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : $"{value} W";
    }

    private static string? FormatNits(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : $"{value} cd/m2";
    }

    private static string? FormatDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;
    }

    private static string Cleanup(string? value)
    {
        return value?.Trim() ?? "";
    }

    private static string Normalize(string? value)
    {
        return new string((value ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool IsGenericDisplayName(string value)
    {
        return value.Contains("generic", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("pnp", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("built-in", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ScoredRow(EnergyStarDisplayRow Row, int Score);

    private sealed class EnergyStarDisplayRow
    {
        [JsonPropertyName("brand_name")]
        public string? BrandName { get; init; }

        [JsonPropertyName("model_name")]
        public string? ModelName { get; init; }

        [JsonPropertyName("model_number")]
        public string? ModelNumber { get; init; }

        [JsonPropertyName("additional_model_information")]
        public string? AdditionalModelInformation { get; init; }

        [JsonPropertyName("display_type")]
        public string? DisplayType { get; init; }

        [JsonPropertyName("panel_type")]
        public string? PanelType { get; init; }

        [JsonPropertyName("native_resolution_pixels")]
        public string? NativeResolutionPixels { get; init; }

        [JsonPropertyName("screen_size_inches")]
        public string? ScreenSizeInches { get; init; }

        [JsonPropertyName("model_features")]
        public string? ModelFeatures { get; init; }

        [JsonPropertyName("signal_or_data_interfaces")]
        public string? SignalOrDataInterfaces { get; init; }

        [JsonPropertyName("on_mode_power_watts")]
        public string? OnModePowerWatts { get; init; }

        [JsonPropertyName("sleep_mode_power_watts")]
        public string? SleepModePowerWatts { get; init; }

        [JsonPropertyName("off_mode_power_watts")]
        public string? OffModePowerWatts { get; init; }

        [JsonPropertyName("maximum_luminance_candelas")]
        public string? MaximumLuminanceCandelas { get; init; }

        [JsonPropertyName("high_dynamic_range_hdr")]
        public string? HighDynamicRangeHdr { get; init; }

        [JsonPropertyName("display_contrast_ratio_at")]
        public string? DisplayContrastRatio { get; init; }

        [JsonPropertyName("maximum_power_delivery_w")]
        public string? MaximumPowerDeliveryW { get; init; }

        [JsonPropertyName("date_certified")]
        public string? DateCertified { get; init; }

        [JsonPropertyName("markets")]
        public string? Markets { get; init; }

        [JsonPropertyName("energy_star_model_identifier")]
        public string? EnergyStarModelIdentifier { get; init; }
    }
}
