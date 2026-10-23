using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Spectre.UI;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static string PipeName => $"Spectre.Activate.{Environment.UserName}";

    private readonly FileStream _lockStream;
    private readonly string _lockPath;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listener;
    private bool _disposed;

    private SingleInstanceCoordinator(FileStream lockStream, string lockPath)
    {
        _lockStream = lockStream;
        _lockPath = lockPath;
    }

    public static SingleInstanceCoordinator? TryCreate(string lockPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // CreateNew is atomic across Windows, Linux, and macOS. The PID
                // allows a stale marker from a crashed process to be recovered.
                var lockStream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);

                using var writer = new StreamWriter(
                    lockStream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    leaveOpen: true);
                writer.Write(Environment.ProcessId);
                writer.Flush();
                return new SingleInstanceCoordinator(lockStream, lockPath);
            }
            catch (IOException)
            {
                if (IsOwnerAlive(lockPath))
                    return null;

                try
                {
                    File.Delete(lockPath);
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    // Called by a second launch: asks the running instance to show its main window.
    // Waits up to 2 seconds, which also covers the first instance still starting up.
    public static bool SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Called by the running instance: runs onActivate (on a background thread)
    // every time a second launch signals it.
    public void StartListening(Action onActivate)
    {
        if (_disposed || _listener is not null) return;
        _listener = Task.Run(() => ListenAsync(onActivate, _cts.Token));
    }

    private static async Task ListenAsync(Action onActivate, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var buffer = new byte[1];
                _ = await server.ReadAsync(buffer, ct).ConfigureAwait(false);
                onActivate();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A client that vanished mid-connect must not kill the listener.
                Debug.WriteLine($"Single-instance listener error: {ex.Message}");
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static bool IsOwnerAlive(string lockPath)
    {
        try
        {
            var text = File.ReadAllText(lockPath).Trim();
            if (string.IsNullOrEmpty(text))
            {
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    Thread.Sleep(25);
                    text = File.ReadAllText(lockPath).Trim();
                    if (!string.IsNullOrEmpty(text))
                        break;
                }
            }

            return int.TryParse(text, out var pid) &&
                   Process.GetProcesses().Any(process => process.Id == pid);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel(); // stops the pipe listener
        _lockStream.Dispose();
        try
        {
            File.Delete(_lockPath);
        }
        catch (IOException)
        {
            // The marker is harmless after process exit and can be recovered next launch.
        }
    }
}
