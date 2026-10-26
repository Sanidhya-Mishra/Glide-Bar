using System.Windows;
using Microsoft.Win32;

namespace GlideBar.App.Services;

public enum AppTheme { Light, Dark }

/// <summary>
/// Owns the active light/dark token <see cref="ResourceDictionary"/> at a fixed slot in
/// <c>Application.Resources.MergedDictionaries</c>. Resolves "system | light | dark" settings,
/// follows the OS theme when set to "system", and raises <see cref="ThemeChanged"/> so the shell
/// can re-apply window chrome (dark title backdrop, acrylic tint).
/// </summary>
public sealed class ThemeManager
{
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Tokens.Dark.xaml", UriKind.Absolute);
    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Tokens.Light.xaml", UriKind.Absolute);

    private string _preference = "system"; // "system" | "light" | "dark"
    private ResourceDictionary? _activeTokens; // the currently-merged token dictionary (slot we own)

    public AppTheme Current { get; private set; } = AppTheme.Dark;
    public event EventHandler<AppTheme>? ThemeChanged;

    public ThemeManager()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Applies the preference string from settings ("system"/"light"/"dark").</summary>
    public void SetPreference(string preference)
    {
        _preference = string.IsNullOrWhiteSpace(preference) ? "system" : preference.ToLowerInvariant();
        Apply();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _preference == "system")
            Application.Current?.Dispatcher.Invoke(Apply);
    }

    private void Apply()
    {
        var theme = Resolve();
        Current = theme;

        var app = Application.Current;
        if (app is null) return;

        var dict = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkUri : LightUri };
        var merged = app.Resources.MergedDictionaries;

        // Insert tokens ahead of the other dictionaries (e.g. Controls.xaml) on first apply;
        // thereafter swap our own dictionary in place so Controls is never disturbed.
        if (_activeTokens is not null && merged.Contains(_activeTokens))
        {
            int idx = merged.IndexOf(_activeTokens);
            merged[idx] = dict;
        }
        else
        {
            merged.Insert(0, dict);
        }
        _activeTokens = dict;

        ThemeChanged?.Invoke(this, theme);
    }

    private AppTheme Resolve() => _preference switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => SystemUsesLightTheme() ? AppTheme.Light : AppTheme.Dark,
    };

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i != 0;
        }
        catch { return false; }
    }
}
