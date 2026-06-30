using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using MonitorInformation.Models;
using MonitorInformation.Services;

namespace MonitorInformation;

public partial class MainWindow
{
    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localization = new();
    private readonly ThemeService _themeService = new();
    private readonly MonitorReader _monitorReader = new();
    private readonly OnlineSpecsService _onlineSpecsService = new();
    private readonly ObservableCollection<MonitorInfo> _monitors = [];

    private AppSettings _settings = new();
    private bool _updatingUi;
    private bool _suppressOnlineLookup;
    private CancellationTokenSource? _onlineLookupCancellation;
    private const string LatestReleaseUrl = "https://github.com/Grim1313/monitor-information/releases/latest";

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        _localization.SetCulture(_settings.Language);
        _themeService.Apply(_settings.Theme);

        MonitorList.ItemsSource = _monitors;

        InitializeSelectors();
        ApplyText();
        RefreshMonitors();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmSettingChange = 0x001A;
        const int wmThemeChanged = 0x031A;

        if ((msg == wmSettingChange || msg == wmThemeChanged) && _settings.Theme == ThemeService.SystemTheme)
        {
            _themeService.Apply(_settings.Theme);
        }

        return IntPtr.Zero;
    }

    private void InitializeSelectors()
    {
        _updatingUi = true;

        LanguageBox.DisplayMemberPath = nameof(SelectorOption.Label);
        LanguageBox.SelectedValuePath = nameof(SelectorOption.Value);
        ThemeBox.DisplayMemberPath = nameof(SelectorOption.Label);
        ThemeBox.SelectedValuePath = nameof(SelectorOption.Value);

        PopulateLanguageOptions();
        PopulateThemeOptions();

        LanguageBox.SelectedValue = _settings.Language;
        ThemeBox.SelectedValue = _settings.Theme;

        _updatingUi = false;
    }

    private void PopulateLanguageOptions()
    {
        LanguageBox.ItemsSource = LocalizationService.SupportedCultures
            .Select(culture => new SelectorOption(culture, _localization.Text($"lang.{culture}")))
            .ToArray();
    }

    private void PopulateThemeOptions()
    {
        ThemeBox.ItemsSource = new[]
        {
            new SelectorOption(ThemeService.SystemTheme, _localization.Text("theme.system")),
            new SelectorOption(ThemeService.LightTheme, _localization.Text("theme.light")),
            new SelectorOption(ThemeService.OledDarkTheme, _localization.Text("theme.oledDark"))
        };
    }

    private void ApplyText()
    {
        Title = _localization.Text("app.title");
        TitleText.Text = _localization.Text("app.title");
        SubtitleText.Text = _localization.Text("app.subtitle");
        LanguageLabel.Text = _localization.Text("label.language");
        ThemeLabel.Text = _localization.Text("label.theme");
        MonitorListLabel.Text = _localization.Text("label.connectedDisplays");
        RefreshButton.Content = _localization.Text("button.refresh");
        VersionText.Text = _localization.Format("app.version", new Dictionary<string, string>
        {
            ["version"] = GetApplicationVersion()
        });
        CheckUpdateRun.Text = _localization.Text("link.checkUpdates");
        OnlineSpecsBox.Content = _localization.Text("online.specs");
        OnlineStatusText.Text = OnlineSpecsBox.IsChecked == true
            ? _localization.Text("online.ready")
            : _localization.Text("online.off");
        SelectedSourceText.Text = _localization.Text("source.local");
        OverviewTab.Header = _localization.Text("tab.overview");
        HardwareTab.Header = _localization.Text("tab.hardware");
        SpecsTab.Header = _localization.Text("tab.specs");
        RawTab.Header = _localization.Text("tab.raw");
        CopyRawButton.Content = _localization.Text("button.copyRawEdid");

        if (MonitorList.SelectedItem is MonitorInfo selected)
        {
            RenderMonitor(selected);
        }
        else
        {
            SelectedNameText.Text = _localization.Text("empty.noDisplay");
        }
    }

    private int RefreshMonitors(bool suppressOnlineLookup = false)
    {
        StatusText.Text = _localization.Text("status.loading");
        _monitors.Clear();
        _suppressOnlineLookup = suppressOnlineLookup;

        try
        {
            foreach (var monitor in _monitorReader.GetActiveMonitors())
            {
                _monitors.Add(monitor);
            }

            if (_monitors.Count > 0)
            {
                MonitorList.SelectedIndex = 0;
            }
            else
            {
                SelectedNameText.Text = _localization.Text("empty.noDisplay");
                OverviewGrid.Children.Clear();
                HardwareGrid.Children.Clear();
                SpecsGrid.Children.Clear();
                RawEdidBox.Text = "";
                AddField(OverviewGrid, "empty.noMonitors", _localization.Text("empty.noMonitors"));
                AddField(SpecsGrid, "online.off", _localization.Text("online.off"));
            }
        }
        finally
        {
            _suppressOnlineLookup = false;
        }

        StatusText.Text = _localization.Format("status.loaded", new Dictionary<string, string>
        {
            ["count"] = _monitors.Count.ToString(CultureInfo.CurrentCulture)
        });
        return _monitors.Count;
    }

    private void RenderMonitor(MonitorInfo monitor)
    {
        SelectedNameText.Text = monitor.DisplayName;
        RawEdidBox.Text = monitor.RawEdidHex;

        OverviewGrid.Children.Clear();
        HardwareGrid.Children.Clear();
        SpecsGrid.Children.Clear();

        AddField(OverviewGrid, "field.displayName", monitor.DisplayName);
        AddField(OverviewGrid, "field.currentMode", monitor.CurrentModeText ?? _localization.Text("value.unknown"));
        AddField(OverviewGrid, "field.refreshRate", monitor.RefreshRate > 0
            ? _localization.Format("value.hertz", new Dictionary<string, string> { ["value"] = monitor.RefreshRate.ToString(CultureInfo.CurrentCulture) })
            : _localization.Text("value.unknown"));
        AddField(OverviewGrid, "field.primary", monitor.IsPrimary ? _localization.Text("value.yes") : _localization.Text("value.no"));
        AddField(OverviewGrid, "field.manufacturer", monitor.Edid?.ManufacturerName ?? _localization.Text("value.unknown"));
        AddField(OverviewGrid, "field.physicalSize", FormatSize(monitor.Edid));
        AddField(OverviewGrid, "field.edidStatus", monitor.Edid is null ? _localization.Text("value.noEdid") : _localization.Text("value.available"));
        AddField(OverviewGrid, "field.checksum", monitor.Edid?.ChecksumValid == true ? _localization.Text("value.valid") : _localization.Text("value.invalid"));

        AddField(HardwareGrid, "field.adapter", monitor.AdapterName);
        AddField(HardwareGrid, "field.connection", monitor.DeviceId);
        AddField(HardwareGrid, "field.manufacturerId", monitor.Edid?.ManufacturerId ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.productCode", monitor.Edid?.ProductCodeHex ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.serialNumber", monitor.Edid?.SerialNumberText ?? monitor.Edid?.SerialNumber.ToString(CultureInfo.InvariantCulture) ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.manufactureDate", monitor.Edid?.ManufactureDateText ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.gamma", monitor.Edid?.GammaText ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.edidVersion", monitor.Edid?.VersionText ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.preferredResolution", monitor.Edid?.PreferredResolutionText ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.descriptorText", monitor.Edid?.DescriptorText ?? _localization.Text("value.unknown"));
        AddField(HardwareGrid, "field.extensionBlocks", monitor.Edid?.ExtensionBlocks.ToString(CultureInfo.CurrentCulture) ?? _localization.Text("value.unknown"));

        if (OnlineSpecsBox.IsChecked == true && !_suppressOnlineLookup)
        {
            _ = LookupOnlineSpecsAsync(monitor);
        }
        else
        {
            AddField(SpecsGrid, "online.off", _localization.Text("online.off"));
        }
    }

    private string FormatSize(EdidInfo? edid)
    {
        if (edid is null || edid.WidthCentimeters <= 0 || edid.HeightCentimeters <= 0)
        {
            return _localization.Text("value.unknown");
        }

        var diagonal = Math.Sqrt(edid.WidthCentimeters * edid.WidthCentimeters + edid.HeightCentimeters * edid.HeightCentimeters) / 2.54;
        var inches = _localization.Format("value.inches", new Dictionary<string, string>
        {
            ["value"] = diagonal.ToString("0.0", CultureInfo.CurrentCulture)
        });
        var centimeters = _localization.Format("value.centimeters", new Dictionary<string, string>
        {
            ["width"] = edid.WidthCentimeters.ToString(CultureInfo.CurrentCulture),
            ["height"] = edid.HeightCentimeters.ToString(CultureInfo.CurrentCulture)
        });

        return $"{centimeters} ({inches})";
    }

    private void AddField(Panel target, string labelKey, string value)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        border.SetResourceReference(Border.BackgroundProperty, "FieldBackgroundBrush");

        var stack = new StackPanel();
        var label = new TextBlock
        {
            Text = _localization.Text(labelKey),
            FontSize = 12
        };
        label.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        stack.Children.Add(label);

        var text = new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15
        };
        text.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(text);

        border.Child = stack;
        target.Children.Add(border);
    }

    private void AddRawField(Panel target, string label, string value)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        border.SetResourceReference(Border.BackgroundProperty, "FieldBackgroundBrush");

        var stack = new StackPanel();
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12
        };
        labelText.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        stack.Children.Add(labelText);

        var valueText = new TextBlock
        {
            Text = value,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15
        };
        valueText.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        stack.Children.Add(valueText);

        border.Child = stack;
        target.Children.Add(border);
    }

    private void AddLinkField(Panel target, string label, string url)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        border.SetResourceReference(Border.BackgroundProperty, "FieldBackgroundBrush");

        var stack = new StackPanel();
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 12
        };
        labelText.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        stack.Children.Add(labelText);

        var linkText = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var hyperlink = new Hyperlink(new Run(url));
        hyperlink.Click += (_, _) => OpenUrl(url);
        linkText.Inlines.Add(hyperlink);
        stack.Children.Add(linkText);

        border.Child = stack;
        target.Children.Add(border);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || LanguageBox.SelectedValue is not string culture)
        {
            return;
        }

        _settings.Language = culture;
        _localization.SetCulture(culture);
        _settingsService.Save(_settings);

        _updatingUi = true;
        PopulateLanguageOptions();
        PopulateThemeOptions();
        LanguageBox.SelectedValue = _settings.Language;
        ThemeBox.SelectedValue = _settings.Theme;
        _updatingUi = false;

        ApplyText();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || ThemeBox.SelectedValue is not string theme)
        {
            return;
        }

        _settings.Theme = theme;
        _settingsService.Save(_settings);
        _themeService.Apply(theme);
        RerenderSelectedMonitor();
    }

    private void RerenderSelectedMonitor()
    {
        if (MonitorList.SelectedItem is MonitorInfo monitor)
        {
            RenderMonitor(monitor);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = _localization.Text("status.refreshing");

        try
        {
            CancelOnlineLookup();
            _onlineSpecsService.ClearCache();
            ManufacturerDatabase.Reload();
            var count = RefreshMonitors(suppressOnlineLookup: true);

            StatusText.Text = _localization.Format("status.refreshComplete", new Dictionary<string, string>
            {
                ["count"] = count.ToString(CultureInfo.CurrentCulture)
            });

            if (OnlineSpecsBox.IsChecked == true && MonitorList.SelectedItem is MonitorInfo monitor)
            {
                await LookupOnlineSpecsAsync(monitor, forceRefresh: true);
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorList.SelectedItem is MonitorInfo monitor)
        {
            RenderMonitor(monitor);
        }
    }

    private async void OnlineSpecsBox_Changed(object sender, RoutedEventArgs e)
    {
        if (OnlineStatusText is null)
        {
            return;
        }

        if (OnlineSpecsBox.IsChecked == true)
        {
            OnlineStatusText.Text = _localization.Text("online.ready");
            if (MonitorList.SelectedItem is MonitorInfo monitor)
            {
                await LookupOnlineSpecsAsync(monitor);
            }
        }
        else
        {
            CancelOnlineLookup();
            OnlineStatusText.Text = _localization.Text("online.off");
            SpecsGrid.Children.Clear();
            AddField(SpecsGrid, "online.off", _localization.Text("online.off"));
        }
    }

    private async Task LookupOnlineSpecsAsync(MonitorInfo monitor, bool forceRefresh = false)
    {
        CancelOnlineLookup();
        _onlineLookupCancellation = new CancellationTokenSource();
        var token = _onlineLookupCancellation.Token;

        SpecsGrid.Children.Clear();
        AddField(SpecsGrid, "online.searching", _localization.Text("online.searching"));
        OnlineStatusText.Text = _localization.Text("online.searching");

        try
        {
            var identity = CreateIdentity(monitor);
            var result = await _onlineSpecsService.SearchAsync(identity, token, forceRefresh);
            if (token.IsCancellationRequested)
            {
                return;
            }

            SpecsGrid.Children.Clear();
            if (result is null)
            {
                OnlineStatusText.Text = _localization.Text("online.noTrustedMatch");
                AddField(SpecsGrid, "online.noTrustedMatch", _localization.Text("online.noTrustedMatch"));
                AddField(SpecsGrid, "spec.query", identity.DisplayName);
                return;
            }

            OnlineStatusText.Text = _localization.Format("online.found", new Dictionary<string, string>
            {
                ["provider"] = result.ProviderName
            });
            RenderOnlineSpecs(result);
        }
        catch (OperationCanceledException)
        {
            // User switched displays or disabled online lookup.
        }
        catch (Exception ex)
        {
            SpecsGrid.Children.Clear();
            OnlineStatusText.Text = _localization.Text("online.error");
            AddField(SpecsGrid, "online.error", ex.Message);
        }
    }

    private void RenderOnlineSpecs(OnlineSpecResult result)
    {
        AddField(SpecsGrid, "spec.provider", result.ProviderName);
        AddField(SpecsGrid, "spec.match", result.MatchSummary);
        AddField(SpecsGrid, "spec.confidence", $"{result.Confidence}/100");
        AddLinkField(SpecsGrid, _localization.Text("spec.source"), result.SourceUrl);

        foreach (var field in result.Fields)
        {
            AddRawField(SpecsGrid, field.Name, field.Value);
        }
    }

    private MonitorIdentity CreateIdentity(MonitorInfo monitor)
    {
        double? diagonal = null;
        if (monitor.Edid is { WidthCentimeters: > 0, HeightCentimeters: > 0 } edid)
        {
            diagonal = Math.Sqrt(edid.WidthCentimeters * edid.WidthCentimeters + edid.HeightCentimeters * edid.HeightCentimeters) / 2.54;
        }

        return new MonitorIdentity
        {
            DisplayName = monitor.Edid?.DisplayName ?? monitor.DisplayName,
            ManufacturerName = monitor.Edid?.ManufacturerName ?? "",
            ManufacturerId = monitor.Edid?.ManufacturerId ?? "",
            ProductCodeHex = monitor.Edid?.ProductCodeHex ?? "",
            WidthPixels = monitor.CurrentWidth,
            HeightPixels = monitor.CurrentHeight,
            DiagonalInches = diagonal
        };
    }

    private void CancelOnlineLookup()
    {
        _onlineLookupCancellation?.Cancel();
        _onlineLookupCancellation?.Dispose();
        _onlineLookupCancellation = null;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Clipboard.SetText(url);
        }
    }

    private static string GetApplicationVersion()
    {
        var informationalVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', StringSplitOptions.RemoveEmptyEntries)[0];
        }

        return typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private void CheckUpdateLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(LatestReleaseUrl);
    }

    private void CopyRawButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RawEdidBox.Text))
        {
            StatusText.Text = _localization.Text("status.copyNone");
            return;
        }

        Clipboard.SetText(RawEdidBox.Text);
        StatusText.Text = _localization.Text("status.copyOk");
    }
}

internal sealed record SelectorOption(string Value, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
