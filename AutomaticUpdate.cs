using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DisplayRest;

internal sealed record UpdateStatus(string? Key = null, string? Version = null, bool Busy = false, bool Error = false);

internal sealed class AutomaticUpdate(string instance, Action<Action> post, Func<bool, bool> canInstall,
    Func<string, bool> offer, Action<UpdateStatus> changed, Action exit) : IDisposable
{
    private static readonly string WorkerPath = Path.Combine(UserSettings.Folder, "update-worker.exe");
    private Process? _probe, _worker;
    private EventWaitHandle? _request, _approved, _deferred;
    private RegisteredWaitHandle? _wait;
    private DateTimeOffset _nextCheck, _lastSuccess;
    private string? _available;
    private bool _enabled, _manual, _disposed;
    private int _hours;
    internal UpdateStatus Status { get; private set; } = new();
    internal bool WaitingToInstall => _available is not null;

    internal void Configure(bool enabled, int hours)
    {
        if (_enabled == enabled && _hours == hours)
            return;
        _nextCheck = _enabled && _lastSuccess != default ? _lastSuccess.AddHours(hours) : DateTimeOffset.UtcNow.AddSeconds(15);
        _enabled = enabled;
        _hours = hours;
        if (!enabled && !_manual)
            _available = null;
    }

    internal void CheckManually()
    {
        if (_disposed || _worker is not null)
            return;
        _manual = true;
        if (_probe is null)
            StartProbe();
        else
            Report("UpdateChecking", busy: true);
    }

    // Driven by the host window timer. Network and cryptography stay out of the tray process.
    internal uint Poll()
    {
        if (_probe is { HasExited: true } probe)
        {
            _probe = null;
            using (probe)
            {
                if (probe.ExitCode != 0)
                    Failed("UpdateCheckError");
                else
                {
                    var version = probe.StandardOutput.ReadToEnd().Trim();
                    if (version.Length != 0 && !Version.TryParse(version, out _))
                        Failed("UpdateCheckError");
                    else
                        Checked(version);
                }
            }
        }
        if (_worker is { HasExited: true } worker)
        {
            int code = worker.ExitCode;
            ReleaseWorker();
            if (code == UpdateWorker.InstallDeferred)
            {
                _available = null;
                _nextCheck = DateTimeOffset.UtcNow.AddMinutes(2);
                Report("UpdateDeferred");
            }
            else
                Failed(code == UpdateWorker.ReleaseChanged ? "UpdateReleaseChanged" : "UpdateInstallError");
            _manual = false;
        }
        if (_probe is null && _worker is null)
        {
            if (_available is not null && (_manual || _enabled) && canInstall(_manual))
                StartInstall();
            else if (_enabled && !_manual && DateTimeOffset.UtcNow >= _nextCheck)
            {
                StartProbe();
            }
        }
        if (_probe is not null || _worker is not null)
            return 500;
        if (_available is not null && _manual)
            return 5000;
        if (!_enabled)
            return 0;
        var delay = Math.Max(250, (_nextCheck - DateTimeOffset.UtcNow).TotalMilliseconds);
        return (uint)(_available is null ? delay : Math.Min(delay, 60000));
    }

    private void StartProbe()
    {
        _available = null;
        try
        {
            var info = StartInfo(Environment.ProcessPath!, "--check-update");
            info.RedirectStandardOutput = true;
            _probe = Process.Start(info) ?? throw new IOException();
            Report("UpdateChecking", busy: _manual);
        }
        catch (Exception error) when (LaunchError(error)) { Failed("UpdateCheckError"); }
    }

    private void Checked(string version)
    {
        _lastSuccess = DateTimeOffset.UtcNow;
        _nextCheck = _lastSuccess.AddHours(_hours);
        if (version.Length == 0)
        {
            _manual = false;
            Report("UpdateCurrent");
            return;
        }

        if (_manual && !offer(version))
        {
            _manual = false;
            Report("UpdateAvailable", version);
            return;
        }
        if (!_manual && !_enabled)
        {
            Report(null);
            return;
        }
        _available = version;
        if (canInstall(_manual))
            StartInstall();
        else
            Report(_manual ? "UpdateWaiting" : "UpdateAvailable", version, busy: _manual);
    }

    private void StartInstall()
    {
        var version = _available!;
        _available = null;
        var token = Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(UserSettings.Folder);
            File.Copy(Environment.ProcessPath!, WorkerPath, overwrite: true);
            _request = new EventWaitHandle(false, EventResetMode.AutoReset, RequestName(instance, token));
            _approved = new EventWaitHandle(false, EventResetMode.AutoReset, ApprovalName(instance, token));
            _deferred = new EventWaitHandle(false, EventResetMode.AutoReset, DeferredName(instance, token));
            var approval = _approved;
            _wait = ThreadPool.RegisterWaitForSingleObject(_request, (_, _) => post(() =>
            {
                if (_approved != approval || _disposed)
                    return;
                if (canInstall(_manual))
                {
                    approval.Set();
                    exit();
                }
                else
                    _deferred!.Set();
            }), null, Timeout.Infinite, executeOnlyOnce: true);
            _worker = Process.Start(StartInfo(WorkerPath, "--update-worker", Environment.ProcessId.ToString(), token, version))
                ?? throw new IOException();
            Report("UpdateDownloading", version, busy: true);
        }
        catch (Exception error) when (LaunchError(error))
        {
            ReleaseWorker();
            Failed("UpdateInstallError");
        }
    }

    private static bool LaunchError(Exception error) => error is IOException or UnauthorizedAccessException or
        Win32Exception or System.Security.SecurityException or InvalidOperationException;

    private void Failed(string key)
    {
        _manual = false;
        _available = null;
        _nextCheck = DateTimeOffset.UtcNow.AddMinutes(5);
        Report(key, error: true);
    }

    private void Report(string? key, string? version = null, bool busy = false, bool error = false)
    {
        Status = new(key, version, busy, error);
        changed(Status);
    }

    public void Dispose()
    {
        _disposed = true;
        _probe?.Dispose();
        _probe = null;
        ReleaseWorker();
    }

    private void ReleaseWorker()
    {
        bool canDelete = _worker is null || _worker.HasExited;
        _worker?.Dispose();
        _worker = null;
        _wait?.Unregister(null);
        _wait = null;
        _request?.Dispose();
        _request = null;
        _deferred?.Dispose();
        _deferred = null;
        _approved?.Dispose();
        _approved = null;
        if (canDelete)
            CleanWorkerCopy();
    }

    internal static string RequestName(string instance, string token) => $"{instance}-update-request-{token}";
    internal static string ApprovalName(string instance, string token) => $"{instance}-update-approved-{token}";
    internal static string DeferredName(string instance, string token) => $"{instance}-update-deferred-{token}";
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
