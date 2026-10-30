using System.IO.Pipes;
using System.Security.Principal;
using Reprise.Core;
using Reprise.Desktop;

namespace Reprise.Platform.Windows;

/// <summary>
/// Keeps one instance per release stage and reopens its panel on a second launch.
/// </summary>
internal sealed class SingleInstanceController : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>
    /// Acquires this desktop session's stage-specific instance mutex.
    /// </summary>
    public SingleInstanceController()
    {
        var channel = ReleaseChannels.ForVersion(AppVersion.Current);
        _mutex = new Mutex(true, $@"Local\dev.junx.Reprise.{channel}", out var primary);
        IsPrimary = primary;
        _pipeName = $"dev.junx.Reprise.{channel}.{WindowsIdentity.GetCurrent().User?.Value}";
    }

    /// <summary>Whether this process owns the application instance.</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Starts a current-user-only pipe for activation requests.
    /// </summary>
    /// <param name="activate">Schedules panel activation on the UI thread.</param>
    public void Listen(Action activate) => _ = ListenAsync(activate);

    /// <summary>
    /// Asks the already running instance to show its panel without creating another tray icon.
    /// </summary>
    /// <returns>A task completing after a best-effort activation request.</returns>
    public async Task ActivateExistingAsync()
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000);
            await using var writer = new StreamWriter(client);
            await writer.WriteLineAsync("show");
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            Console.Error.WriteLine($"Reprise: existing instance could not be activated: {exception.Message}");
        }
    }

    /// <summary>
    /// Handles short activation messages until application shutdown.
    /// </summary>
    /// <param name="activate">Called when the panel should be shown.</param>
    /// <returns>A task completing when the listener is cancelled.</returns>
    private async Task ListenAsync(Action activate)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync(_shutdown.Token) == "show")
                {
                    activate();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"Reprise: activation pipe unavailable: {exception.Message}");
                await Task.Delay(250, _shutdown.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>
    /// Stops activation requests and releases only the mutex owned by this process.
    /// </summary>
    public void Dispose()
    {
        _shutdown.Cancel();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
