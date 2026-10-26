using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.App.Interop;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>One pinned tile. Exposes the target's real shell icon (with a glyph fallback) +
/// open / reveal / unpin commands.</summary>
public sealed partial class PinItemViewModel : ObservableObject
{
    private readonly PinnedItem _model;
    private readonly ILauncher _launcher;
    private readonly Action<string> _remove;

    /// <summary>The target's extracted shell icon; null until loaded (or if none is available).</summary>
    [ObservableProperty] private ImageSource? _icon;

    public PinItemViewModel(PinnedItem model, ILauncher launcher, Action<string> remove)
    {
        _model = model;
        _launcher = launcher;
        _remove = remove;
        LoadIconAsync();
    }

    public string Id => _model.Id;
    public string Name => _model.Name;
    public string Path => _model.Path;
    public PinnedKind Kind => _model.Kind;

    /// <summary>True once a real shell icon was extracted (drives the glyph-vs-image fallback in XAML).</summary>
    public bool HasIcon => Icon is not null;
    partial void OnIconChanged(ImageSource? value) => OnPropertyChanged(nameof(HasIcon));

    /// <summary>Extract the shell icon off the UI thread; the frozen result is assigned back on it.</summary>
    private async void LoadIconAsync()
    {
        try
        {
            var path = _model.Path;
            var icon = await Task.Run(() => ShellIcon.ForPath(path));
            if (icon is not null) Icon = icon; // resumes on the UI thread (captured context)
        }
        catch { /* leave the glyph fallback in place */ }
    }

    /// <summary>Single-letter fallback (used if the icon font is unavailable).</summary>
    public string Glyph => string.IsNullOrWhiteSpace(_model.Name) ? "?" : _model.Name.Trim()[..1].ToUpperInvariant();

    /// <summary>A Segoe MDL2 Assets glyph per kind, for the icon-font tile.</summary>
    public string KindGlyph => Kind switch
    {
        PinnedKind.Folder => "\uE8B7", // Folder
        PinnedKind.App => "\uECAA",    // AppIconDefault
        _ => "\uE8A5",                 // Document
    };

    [RelayCommand] private void Open() => _launcher.Open(_model);
    [RelayCommand] private void OpenLocation() => _launcher.RevealInExplorer(_model.Path);
    [RelayCommand] private void Unpin() => _remove(_model.Id);
}

/// <summary>
/// Pins zone: mirrors <see cref="ISettingsService.Settings"/>.<c>Pins</c> into an observable
/// collection and rebuilds on <see cref="ISettingsService.PinsChanged"/>. Supports adding by folder
/// picker, by file picker, or by path (drag-and-drop), and removing (unpin).
/// </summary>
public sealed partial class PinsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILauncher _launcher;

    public ObservableCollection<PinItemViewModel> Pins { get; } = new();

    public PinsViewModel(ISettingsService settings, ILauncher launcher)
    {
        _settings = settings;
        _launcher = launcher;
        _settings.PinsChanged += (_, _) => Dispatch(Rebuild);
        Rebuild();
    }

    public bool HasPins => Pins.Count > 0;

    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pin a folder", Multiselect = true };
        if (dlg.ShowDialog() == true)
            foreach (var p in dlg.FolderNames) AddPath(p);
    }

    [RelayCommand]
    private void AddFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pin a file", Multiselect = true, CheckFileExists = true };
        if (dlg.ShowDialog() == true)
            foreach (var p in dlg.FileNames) AddPath(p);
    }

    [RelayCommand]
    private void AddApp()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pin an app",
            Multiselect = true,
            CheckFileExists = true,
            // Default to Start-menu / common program shortcuts, where installed apps live.
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Filter = "Applications (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true)
            foreach (var p in dlg.FileNames) AddPath(p, PinnedKind.App);
    }

    /// <summary>Pin a file / folder / app by path (used by the pickers and by drag-and-drop).</summary>
    /// <param name="forceKind">When set, overrides the inferred kind (e.g. the app picker pins as App).</param>
    public void AddPath(string path, PinnedKind? forceKind = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        PinnedKind kind;
        string name;
        if (Directory.Exists(path))
        {
            kind = PinnedKind.Folder;
            name = new DirectoryInfo(path.TrimEnd(System.IO.Path.DirectorySeparatorChar,
                                                  System.IO.Path.AltDirectorySeparatorChar)).Name;
            if (string.IsNullOrEmpty(name)) name = path; // e.g. a drive root "D:\"
        }
        else if (File.Exists(path))
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            kind = ext is ".exe" or ".lnk" or ".appref-ms" ? PinnedKind.App : PinnedKind.File;
            name = System.IO.Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            return; // path no longer exists
        }

        if (string.IsNullOrWhiteSpace(name)) name = path;

        if (forceKind is { } k) kind = k;

        // Skip duplicates.
        if (_settings.Settings.Pins.Any(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        _settings.AddPin(new PinnedItem { Name = name, Path = path, Kind = kind });
    }

    private void Remove(string id) => _settings.RemovePin(id);

    private void Rebuild()
    {
        Pins.Clear();
        foreach (var p in _settings.Settings.Pins.OrderBy(p => p.Order))
            Pins.Add(new PinItemViewModel(p, _launcher, Remove));
        OnPropertyChanged(nameof(HasPins));
    }

    private static void Dispatch(Action a)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) a();
        else d.Invoke(a);
    }
}
