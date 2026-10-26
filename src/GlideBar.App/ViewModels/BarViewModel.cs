using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>
/// Top-level view model bound to the overlay window. Holds the bar's visual state (expanded /
/// collapsed / dragging / settings-open), orientation, and the three content zones. Critically it
/// owns the <see cref="IMetricsProvider"/> lifecycle: sampling runs ONLY while expanded and not
/// paused, and <see cref="IMetricsProvider.SampleProduced"/> is marshalled to the UI thread.
/// </summary>
public sealed partial class BarViewModel : ObservableObject, IDisposable
{
    private readonly IMetricsProvider _metrics;
    private readonly ISettingsService _settings;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private bool _isPinnedOpen;      // keeps it expanded regardless of hover
    [ObservableProperty] private bool _isSettingsOpen;
    [ObservableProperty] private bool _isAddOpen;
    [ObservableProperty] private bool _isControlCenterOpen;
    [ObservableProperty] private bool _isMonitoringPaused;
    [ObservableProperty] private bool _reduceEffects;
    [ObservableProperty] private BarOrientation _orientation = BarOrientation.Horizontal;

    public SearchViewModel Search { get; }
    public PinsViewModel Pins { get; }
    public VitalsViewModel Vitals { get; }
    public SettingsViewModel SettingsPanel { get; }
    public AppPickerViewModel AppPicker { get; }
    public MediaViewModel Media { get; }
    public NotificationsViewModel Notifications { get; }

    public bool IsHorizontal => Orientation == BarOrientation.Horizontal;
    public bool IsVertical => Orientation == BarOrientation.Vertical;

    // Visual-state helpers the window binds zone visibility to.
    public bool ShowHandle => !IsExpanded;
    public bool ShowHorizontalContent => IsExpanded && IsHorizontal;
    public bool ShowVerticalContent => IsExpanded && IsVertical;

    public BarViewModel(
        IMetricsProvider metrics,
        ISettingsService settings,
        SearchViewModel search,
        PinsViewModel pins,
        VitalsViewModel vitals,
        SettingsViewModel settingsPanel,
        AppPickerViewModel appPicker,
        MediaViewModel media,
        NotificationsViewModel notifications)
    {
        _metrics = metrics;
        _settings = settings;
        Search = search;
        Pins = pins;
        Vitals = vitals;
        SettingsPanel = settingsPanel;
        AppPicker = appPicker;
        Media = media;
        Notifications = notifications;
        AppPicker.CloseRequested += (_, _) => IsAddOpen = false;

        Vitals.ApplyCapabilities(_metrics.Capabilities);
        _metrics.SampleProduced += OnSampleProduced;

        ReduceEffects = _settings.Settings.ReduceEffects;
        _metrics.SetInterval(TimeSpan.FromMilliseconds(Math.Max(200, _settings.Settings.PollIntervalMs)));
        Orientation = _settings.Settings.Placement.Orientation;
    }

    partial void OnOrientationChanged(BarOrientation value)
    {
        OnPropertyChanged(nameof(IsHorizontal));
        OnPropertyChanged(nameof(IsVertical));
        OnPropertyChanged(nameof(ShowHorizontalContent));
        OnPropertyChanged(nameof(ShowVerticalContent));
        // Both docks start with the search collapsed behind its icon (expands on click).
        Search.IsFieldOpen = false;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHandle));
        OnPropertyChanged(nameof(ShowHorizontalContent));
        OnPropertyChanged(nameof(ShowVerticalContent));
    }

    /// <summary>Expand or collapse, toggling metric sampling accordingly (perf contract).</summary>
    public void SetExpanded(bool expanded)
    {
        if (IsExpanded == expanded) return;
        IsExpanded = expanded;
        UpdateMetricsState();

        if (!expanded)
        {
            IsSettingsOpen = false;
            IsAddOpen = false;
            IsControlCenterOpen = false; // also stops media listening via OnIsControlCenterOpenChanged
            Search.IsFieldOpen = false;
            Search.Clear();
        }
    }

    /// <summary>Open/close the Control Center (Now Playing + notifications). Media listening follows
    /// the open state (perf contract — nothing runs while it's closed).</summary>
    [RelayCommand]
    private void ToggleControlCenter()
    {
        IsControlCenterOpen = !IsControlCenterOpen;
        if (IsControlCenterOpen)
        {
            IsSettingsOpen = false;
            IsAddOpen = false;
        }
    }

    partial void OnIsControlCenterOpenChanged(bool value)
    {
        if (value) Media.Start();
        else Media.Stop();
    }

    public void TogglePinnedOpen() => IsPinnedOpen = !IsPinnedOpen;

    public void SetMonitoringPaused(bool paused)
    {
        IsMonitoringPaused = paused;
        UpdateMetricsState();
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        if (IsSettingsOpen) IsAddOpen = false;
    }

    /// <summary>Open/close the add panel (installed-app picker + file/folder). Loads apps on first open.</summary>
    [RelayCommand]
    private async Task ToggleAdd()
    {
        IsAddOpen = !IsAddOpen;
        if (IsAddOpen)
        {
            IsSettingsOpen = false;
            await AppPicker.EnsureLoadedAsync();
        }
    }

    private void UpdateMetricsState()
    {
        bool shouldRun = IsExpanded && !IsMonitoringPaused;
        if (shouldRun && !_metrics.IsRunning) _metrics.Start();
        else if (!shouldRun && _metrics.IsRunning) _metrics.Stop();
    }

    private void OnSampleProduced(object? sender, SystemSnapshot snap)
    {
        // Event arrives on a background thread — marshal to the UI thread.
        var d = Application.Current?.Dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) Vitals.Update(snap);
        else d.BeginInvoke(() => Vitals.Update(snap));
    }

    public void Dispose()
    {
        _metrics.SampleProduced -= OnSampleProduced;
        _metrics.Stop();
        Media.Dispose();
    }
}
