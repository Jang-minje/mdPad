using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Windows;

namespace MdPad.Wpf;

public partial class App : System.Windows.Application
{
    private const string MutexName = "MdPadWv2.SingleInstance";
    private const string PipeName = "MdPadWv2.ProtocolPipe";

    private Mutex? _singleInstanceMutex;
    private CancellationTokenSource? _pipeCancellation;
    private readonly Queue<string[]> _pendingExternalArguments = new();

    public event Action<string[]>? ExternalArgumentsReceived;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            WriteErrorLog(e.Exception);
            System.Windows.MessageBox.Show(e.Exception.Message, "MD Pad 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                WriteErrorLog(exception);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            SendArgumentsToPrimaryInstance(e.Args);
            Shutdown();
            return;
        }

        _pipeCancellation = new CancellationTokenSource();
        _ = ListenForExternalArgumentsAsync(_pipeCancellation.Token);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _pipeCancellation?.Cancel();
            _pipeCancellation?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
        }

        base.OnExit(e);
    }

    private async Task ListenForExternalArgumentsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var json = await reader.ReadToEndAsync(cancellationToken);
                var args = JsonSerializer.Deserialize<string[]>(json) ?? [];
                Dispatcher.Invoke(() => DispatchExternalArguments(args));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception exception)
            {
                WriteErrorLog(exception);
            }
        }
    }

    public string[][] DrainPendingExternalArguments()
    {
        lock (_pendingExternalArguments)
        {
            var pending = _pendingExternalArguments.ToArray();
            _pendingExternalArguments.Clear();
            return pending;
        }
    }

    private void DispatchExternalArguments(string[] args)
    {
        if (ExternalArgumentsReceived is null)
        {
            lock (_pendingExternalArguments)
            {
                _pendingExternalArguments.Enqueue(args);
            }

            return;
        }

        ExternalArgumentsReceived.Invoke(args);
    }

    private static void SendArgumentsToPrimaryInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.Write(JsonSerializer.Serialize(args));
            writer.Flush();
        }
        catch
        {
        }
    }

    private static void WriteErrorLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdPadWv2");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
        }
    }
}
