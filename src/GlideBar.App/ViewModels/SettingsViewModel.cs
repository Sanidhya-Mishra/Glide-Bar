using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.App.Services;
using GlideBar.Core.Abstractions;

namespace GlideBar.App.ViewModels;

/// <summary>
/// Backs the settings flyout: theme, metric update rate, autostart, temperatures, and quit.
/// Changes apply immediately (and persist via <see cref="ISettingsService"/>).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ThemeManager _themeManager;
    private readonly IAutostartService _autostart;
    private readonly IMetricsProvider _metrics;
    private readonly VitalsViewModel _vitals;
    private bool _loading;

    [ObservableProperty] private string _theme = "system";
    [ObservableProperty] private int _pollIntervalMs = 1000;
    [ObservableProperty] private bool _autostartEnabled;
    [ObservableProperty] private bool _temperaturesEnabled;
    [ObservableProperty] private string _tempStatus = "";

    public SettingsViewModel(ISettingsService settings, ThemeManager theme, IAutostartService autostart,
                             IMetricsProvider metrics, VitalsViewModel vitals)
    {
        _settings = settings;
        _themeManager = theme;
        _autostart = autostart;
        _metrics = metrics;
        _vitals = vitals;

        _loading = true;
        Theme = string.IsNullOrWhiteSpace(settings.Settings.Theme) ? "system" : settings.Settings.Theme;
        PollIntervalMs = settings.Settings.PollIntervalMs <= 0 ? 1000 : settings.Settings.PollIntervalMs;
        try { AutostartEnabled = autostart.IsEnabled; } catch { AutostartEnabled = settings.Settings.Autostart; }
        TemperaturesEnabled = settings.Settings.TemperaturesEnabled;
        _loading = false;
    }

    // Highlight helpers for the segmented controls.
    public bool IsThemeSystem => Theme == "system";
    public bool IsThemeLight => Theme == "light";
    public bool IsThemeDark => Theme == "dark";
    public bool IsRateFast => PollIntervalMs <= 500;
    public bool IsRateNormal => PollIntervalMs > 500 && PollIntervalMs < 2000;
    public bool IsRateSlow => PollIntervalMs >= 2000;

    partial void OnThemeChanged(string value)
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        if (_loading) return;
        _themeManager.SetPreference(value);
        _settings.Settings.Theme = value;
        Save();
    }

    partial void OnPollIntervalMsChanged(int value)
    {
        OnPropertyChanged(nameof(IsRateFast));
        OnPropertyChanged(nameof(IsRateNormal));
        OnPropertyChanged(nameof(IsRateSlow));
        if (_loading) return;
        _metrics.SetInterval(TimeSpan.FromMilliseconds(Math.Max(200, value)));
        _settings.Settings.PollIntervalMs = value;
        Save();
    }

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_loading) return;
        try { _autostart.SetEnabled(value); } catch { }
        _settings.Settings.Autostart = value;
        Save();
    }

    async partial void OnTemperaturesEnabledChanged(bool value)
    {
        if (_loading) return;
        _settings.Settings.TemperaturesEnabled = value;
        Save();
        if (!value) { TempStatus = ""; _vitals.TempAvailable = false; return; }

        TempStatus = "Enabling…";
        bool ok;
        try { ok = await _metrics.TryEnableTemperaturesAsync(); }
        catch { ok = false; }
        _vitals.ApplyCapabilities(_metrics.Capabilities);
        bool have = _metrics.Capabilities.CpuTemp || _metrics.Capabilities.GpuTemp;
        TempStatus = ok && have ? "" : "No sensor found (try running as admin)";
    }

    [RelayCommand] private void SetTheme(string value) => Theme = value;
    [RelayCommand] private void SetRate(string ms) { if (int.TryParse(ms, out var v)) PollIntervalMs = v; }
    [RelayCommand] private void Quit() => Application.Current.Shutdown();

    private void Save() => _ = _settings.SaveAsync();
}
