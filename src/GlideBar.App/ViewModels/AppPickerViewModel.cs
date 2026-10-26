using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.App.Interop;
using GlideBar.App.Services;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>One selectable tile in the add panel (an installed app, or a picked file/folder).</summary>
public sealed partial class AppEntryViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public string Name { get; }
    public string Path { get; }
    public PinnedKind Kind { get; }
    public ImageSource? Icon { get; }

    /// <summary>Single-letter fallback shown when no shell icon could be extracted.</summary>
    public string Glyph => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    public bool HasIcon => Icon is not null;

    public AppEntryViewModel(string name, string path, PinnedKind kind, ImageSource? icon)
    {
        Name = name;
        Path = path;
        Kind = kind;
        Icon = icon;
    }

    [RelayCommand] private void Toggle() => IsSelected = !IsSelected;
}

/// <summary>
/// Backs the "+" add panel: lists installed apps as selectable icon tiles, lets the user add files
/// or folders to the same selection, and on <see cref="DoneCommand"/> pins everything selected via
/// <see cref="PinsViewModel.AddPath"/> and raises <see cref="CloseRequested"/>.
/// </summary>
public sealed partial class AppPickerViewModel : ObservableObject
{
    private readonly PinsViewModel _pins;
    private readonly InstalledAppsProvider _apps;
    private bool _loaded;

    public ObservableCollection<AppEntryViewModel> Entries { get; } = new();
    public ICollectionView EntriesView { get; }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _filter = "";

    /// <summary>Raised when the panel should close (Done / Cancel).</summary>
    public event EventHandler? CloseRequested;

    public AppPickerViewModel(PinsViewModel pins, InstalledAppsProvider apps)
    {
        _pins = pins;
        _apps = apps;

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = o => o is AppEntryViewModel e &&
            (string.IsNullOrWhiteSpace(Filter) ||
             e.Name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase));
    }

    partial void OnFilterChanged(string value) => EntriesView.Refresh();

    /// <summary>Enumerate installed apps + extract their icons once, off the UI thread.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsLoading = true;
        try
        {
            var found = await Task.Run(() =>
            {
                var list = _apps.Enumerate();
                var result = new List<(InstalledApp app, ImageSource? icon)>(list.Count);
                foreach (var a in list)
                    result.Add((a, ShellIcon.ForPath(a.Path)));
                return result;
            }).ConfigureAwait(true);

            foreach (var (a, icon) in found)
                Entries.Add(new AppEntryViewModel(a.Name, a.Path, PinnedKind.App, icon));

            EntriesView.Refresh();
        }
        catch { /* never throw into UI — leave the panel empty */ }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add files", Multiselect = true, CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
            foreach (var p in dlg.FileNames) AddCustom(p);
    }

    [RelayCommand]
    private void AddFolders()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add folders", Multiselect = true };
        if (dlg.ShowDialog() == true)
            foreach (var p in dlg.FolderNames) AddCustom(p);
    }

    /// <summary>Add a picked file/folder as a pre-selected tile at the top of the grid.</summary>
    private void AddCustom(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var existing = Entries.FirstOrDefault(e =>
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { existing.IsSelected = true; return; }

        PinnedKind kind;
        string name;
        if (Directory.Exists(path))
        {
            kind = PinnedKind.Folder;
            name = new DirectoryInfo(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
        }
        else if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            kind = ext is ".exe" or ".lnk" or ".appref-ms" ? PinnedKind.App : PinnedKind.File;
            name = Path.GetFileNameWithoutExtension(path);
        }
        else return;

        if (string.IsNullOrWhiteSpace(name)) name = path;

        Entries.Insert(0, new AppEntryViewModel(name, path, kind, ShellIcon.ForPath(path)) { IsSelected = true });
        EntriesView.Refresh();
    }

    [RelayCommand]
    private void Done()
    {
        foreach (var e in Entries.Where(e => e.IsSelected))
            _pins.AddPath(e.Path, e.Kind);
        Reset();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Reset();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clear the current selection + filter (called on close, so the panel reopens fresh).</summary>
    private void Reset()
    {
        foreach (var e in Entries) e.IsSelected = false;
        Filter = "";
    }
}
