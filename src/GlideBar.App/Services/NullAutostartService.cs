using GlideBar.Core.Abstractions;

namespace GlideBar.App.Services;

/// <summary>
/// Null-safe stand-in for <see cref="IAutostartService"/>. The real implementation (HKCU Run key /
/// Startup shortcut) is wired at integration time; until then this keeps the tray's "Start with
/// Windows" toggle from throwing while persisting the user's intent in-memory for the session.
/// </summary>
internal sealed class NullAutostartService : IAutostartService
{
    public bool IsEnabled { get; private set; }

    // INTEGRATION: replaced by the real registry/startup-folder service.
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
