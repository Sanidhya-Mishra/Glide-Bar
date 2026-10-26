using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GlideBar.Core.Abstractions;
using GlideBar.Core.Models;

namespace GlideBar.App.ViewModels;

/// <summary>
/// Search zone: debounced (~150 ms) queries against the first available <see cref="ISearchProvider"/>,
/// cancellable, capped. Results launch / reveal via <see cref="ILauncher"/>. In the vertical dock the
/// field is collapsed behind a search icon (<see cref="IsFieldOpen"/>).
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int MaxResults = 20;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly ISearchProvider _provider;
    private readonly ILauncher _launcher;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isFieldOpen;          // collapsed to a search icon until opened
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<SearchResult> Results { get; } = new();
    public bool HasResults => Results.Count > 0;
    public bool ShowPlaceholder => string.IsNullOrEmpty(Query);
    public string ProviderName => _provider.Name;

    public SearchViewModel(IEnumerable<ISearchProvider> providers, ILauncher launcher)
    {
        _provider = providers.FirstOrDefault(p => p.IsAvailable)
                    ?? providers.First();
        _launcher = launcher;
        _debounce = new DispatcherTimer { Interval = Debounce };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunSearchAsync(); };
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        _debounce.Stop();
        if (string.IsNullOrWhiteSpace(value))
        {
            _cts?.Cancel();
            Results.Clear();
            OnPropertyChanged(nameof(HasResults));
            return;
        }
        _debounce.Start();
    }

    private async Task RunSearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var q = Query;

        try
        {
            IsBusy = true;
            var hits = await _provider.SearchAsync(q, MaxResults, ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;

            Results.Clear();
            foreach (var h in hits) Results.Add(h);
            OnPropertyChanged(nameof(HasResults));
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch { /* provider error — leave prior results */ }
        finally { if (!ct.IsCancellationRequested) IsBusy = false; }
    }

    [RelayCommand]
    private void ToggleField() => IsFieldOpen = !IsFieldOpen;

    [RelayCommand]
    private void Open(SearchResult? result)
    {
        if (result is null) return;
        _launcher.Open(result);
    }

    [RelayCommand]
    private void Reveal(SearchResult? result)
    {
        if (result is null) return;
        _launcher.RevealInExplorer(result.Path);
    }

    public void Clear()
    {
        Query = "";
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
}
