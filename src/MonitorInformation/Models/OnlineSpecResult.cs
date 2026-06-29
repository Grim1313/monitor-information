namespace MonitorInformation.Models;

public sealed class OnlineSpecResult
{
    public required string ProviderName { get; init; }

    public required string SourceUrl { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public required int Confidence { get; init; }

    public required string MatchSummary { get; init; }

    public required IReadOnlyList<OnlineSpecField> Fields { get; init; }
}

public sealed class OnlineSpecField
{
    public required string Name { get; init; }

    public required string Value { get; init; }
}

public sealed class MonitorIdentity
{
    public required string DisplayName { get; init; }

    public required string ManufacturerName { get; init; }

    public required string ManufacturerId { get; init; }

    public required string ProductCodeHex { get; init; }

    public required int WidthPixels { get; init; }

    public required int HeightPixels { get; init; }

    public required double? DiagonalInches { get; init; }
}
