using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DisplayRest;

internal sealed class AutomaticUpdate(string instance, Action<Action> post, Func<bool> canInstall, Action exit) : IDisposable
{
    private static readonly string WorkerPath = Path.Combine(UserSettings.Folder, "update-worker.exe");
    private DateTimeOffset _nextCheck;
    private Process? _worker;
    private EventWaitHandle? _request, _approved;
    private RegisteredWaitHandle? _wait;

    internal uint CheckWhenIdle()
    {
        if (_worker is not null)
        {
            if (!_worker.HasExited)
                return 5000;
            Dispose();
        }
        var remaining = _nextCheck - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero)
            return (uint)Math.Max(1000, remaining.TotalMilliseconds);
        if (!canInstall())
            return 60000;

        _nextCheck = DateTimeOffset.UtcNow.AddHours(24);
        var token = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(UserSettings.Folder);
            File.Copy(Environment.ProcessPath!, WorkerPath, overwrite: true);
            _request = new EventWaitHandle(false, EventResetMode.AutoReset, RequestName(instance, token));
            _approved = new EventWaitHandle(false, EventResetMode.AutoReset, ApprovalName(instance, token));
            var approval = _approved;
            _wait = ThreadPool.RegisterWaitForSingleObject(_request, (_, _) => post(() =>
            {
                if (_approved != approval || !canInstall())
                {
                    _nextCheck = DateTimeOffset.UtcNow.AddMinutes(2);
                    return;
                }
                approval.Set();
                exit();
            }), null, Timeout.Infinite, executeOnlyOnce: true);
            _worker = Process.Start(StartInfo(WorkerPath, "--update-worker", Environment.ProcessId.ToString(), token));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException)
        {
            Dispose();
        }
        return 5000;
    }

    public void Dispose()
    {
        bool canDelete = _worker is null || _worker.HasExited;
        _worker?.Dispose();
        _worker = null;
        _wait?.Unregister(null);
        _wait = null;
        _request?.Dispose();
        _request = null;
        _approved?.Dispose();
        _approved = null;
        if (canDelete)
            CleanWorkerCopy();
    }

    internal static string RequestName(string instance, string token) => $"{instance}-update-request-{token}";
    internal static string ApprovalName(string instance, string token) => $"{instance}-update-approved-{token}";
    internal static string InstallName(string instance) => instance + "-installing-update";
    internal static void CleanWorkerCopy() => TryDelete(WorkerPath);

    internal static ProcessStartInfo StartInfo(string path, params string[] args)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        };
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
