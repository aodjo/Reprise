namespace Reprise.Desktop;

/// <summary>Connects an optional player account through its official browser authorization flow.</summary>
public interface IPlayerAccountConnection
{
    /// <summary>Whether this application build has the provider's public client identifier.</summary>
    bool IsConfigured { get; }
    /// <summary>Whether this user has an authorized account stored locally.</summary>
    bool IsConnected { get; }
    /// <summary>Current user-facing connection or provider error message.</summary>
    string Status { get; }
    /// <summary>Notifies settings when authorization or a provider restriction changes.</summary>
    event EventHandler? Changed;
    /// <summary>Opens official consent in the browser and accepts a state-checked loopback callback.</summary>
    Task ConnectAsync(Func<Uri, Task> openBrowser, CancellationToken cancellationToken = default);
    /// <summary>Removes this application's stored authorization and cached device state.</summary>
    void Disconnect();
}
