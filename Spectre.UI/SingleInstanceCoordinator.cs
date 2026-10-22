using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Spectre.UI;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly FileStream _lockStream;
    private readonly string _lockPath;
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
