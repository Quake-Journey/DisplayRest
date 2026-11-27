using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace DisplayRest;

internal static class UpdateWorker
{
    private const string Repo = "Quake-Journey/DisplayRest";
    private const string AssetName = "PCRest.exe";
    private const string LegacyAssetName = "DisplayRest.exe";
    private static readonly Uri Latest = new($"https://api.github.com/repos/{Repo}/releases/latest");
    private const int Limit = 100 * 1024 * 1024;

    private sealed record ReleaseAsset(Version Version, Uri Url, long Size, byte[] Digest);
    private sealed class ParentExitSignal : WaitHandle
    {
        internal ParentExitSignal(Process parent) => SafeWaitHandle = new SafeWaitHandle(parent.Handle, ownsHandle: false);
    }

    internal const int ReleaseChanged = 2, InstallDeferred = 3;

    internal static int Check()
    {
        try
        {
            if (!TryVersion(FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion, out var current))
                return 1;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var http = CreateClient(current);
            var asset = ReadLatest(http, current, timeout.Token).GetAwaiter().GetResult();
            if (asset is not null)
            {
                var version = asset.Version;
                Console.WriteLine(version.ToString(version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2));
            }
            return 0;
        }
        catch (Exception error) when (IsUpdateError(error)) { return 1; }
    }

    private static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"PCRest/{current.Major}.{current.Minor}");
        return http;
    }

    private static bool IsUpdateError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or
        HttpRequestException or OperationCanceledException or JsonException or FormatException or InvalidOperationException or
        ArgumentException or CryptographicException or System.Collections.Generic.KeyNotFoundException or System.Security.SecurityException;

    internal static int Run(string instance, string[] args)
    {
        if (args.Length != 4 || !TryVersion(args[3], out var expected) || !int.TryParse(args[1], out var owner) ||
            !Guid.TryParseExact(args[2], "N", out _) ||
            !EventWaitHandle.TryOpenExisting(AutomaticUpdate.RequestName(instance, args[2]), out var request))
            return 1;

        using (request)
        {
            if (!EventWaitHandle.TryOpenExisting(AutomaticUpdate.ApprovalName(instance, args[2]), out var approval))
                return 1;
            using (approval)
            {
                try
                {
                    using var parent = Process.GetProcessById(owner);
                    var target = parent.MainModule?.FileName;
                    if (target is null || parent.HasExited || !TryVersion(FileVersionInfo.GetVersionInfo(target).ProductVersion, out var current))
                        return 1;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                    using var http = CreateClient(current);
                    var asset = ReadLatest(http, current, timeout.Token).GetAwaiter().GetResult();
                    if (asset is null || asset.Version != expected)
                        return ReleaseChanged;
                    if (parent.HasExited)
                        return InstallDeferred;
                    var stage = target + "." + args[2] + ".new";
                    try
                    {
                        Download(http, asset, stage, timeout.Token).GetAwaiter().GetResult();
                        if (parent.HasExited)
                            return InstallDeferred;

                        using var installing = new EventWaitHandle(false, EventResetMode.ManualReset,
                            AutomaticUpdate.InstallName(instance), out bool isNew);
                        if (!isNew)
                            return InstallDeferred;
                        try
                        {
                            if (!EventWaitHandle.TryOpenExisting(AutomaticUpdate.DeferredName(instance, args[2]), out var deferred))
                                return InstallDeferred;
                            using var declined = deferred;
                            using var parentExit = new ParentExitSignal(parent);
                            request.Set();
                            // An occupied UI thread may take time to answer. A late approval must still have a waiting installer.
                            if (WaitHandle.WaitAny([approval, declined, parentExit]) != 0)
                                return InstallDeferred;
                            parent.WaitForExit();
                            return Install(stage, target, args[2]) ? 0 : 1;
                        }
                        finally { installing.Set(); }
                    }
                    finally { AutomaticUpdate.TryDelete(stage); }
                }
                catch (Exception error) when (IsUpdateError(error))
                {
                    return 1;
                }
            }
        }
    }

    private static async Task<ReleaseAsset?> ReadLatest(HttpClient http, Version current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(content, cancellationToken: token);
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString();
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() ||
            tag is null || !tag.StartsWith('v') || !TryVersion(tag.AsSpan(1), out var next) || next <= current)
            return null;

        var assets = release.GetProperty("assets");
        return FindAsset(assets, tag, next, AssetName) ?? FindAsset(assets, tag, next, LegacyAssetName) ??
            throw new InvalidDataException("The newer release has no compatible, verifiable executable.");
    }

    private static ReleaseAsset? FindAsset(JsonElement assets, string tag, Version version, string name)
    {
        var expected = new Uri($"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{name}");
        foreach (var entry in assets.EnumerateArray())
        {
            if (entry.GetProperty("name").GetString() != name ||
                entry.GetProperty("state").GetString() != "uploaded" ||
                entry.GetProperty("browser_download_url").GetString() != expected.AbsoluteUri)
                continue;
            long size = entry.GetProperty("size").GetInt64();
            var digest = entry.GetProperty("digest").GetString();
            if (size is > 0 and <= Limit && digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal))
                return new(version, expected, size, Convert.FromHexString(digest.AsSpan(7)));
        }
        return null;
    }

    private static async Task Download(HttpClient http, ReleaseAsset asset, string stage, CancellationToken token)
    {
        var url = asset.Url;
        for (int i = 0; i < 4; i++)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location is not { IsAbsoluteUri: true } || location.Scheme != "https" || !location.IsDefaultPort ||
                    location.UserInfo.Length != 0 || location.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new IOException("Unexpected release asset redirect.");
                url = location;
                continue;
            }

            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536];
                long remaining = asset.Size;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    remaining -= count;
                    if (remaining < 0)
                        throw new IOException("Release asset exceeds its declared size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if (remaining != 0 || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), asset.Digest))
                    throw new IOException("Release asset SHA-256 or size mismatch.");
                output.Flush(flushToDisk: true);
            }

            var info = FileVersionInfo.GetVersionInfo(stage);
            if (info.ProductName != "DisplayRest" || !TryVersion(info.ProductVersion, out var binary) || binary != asset.Version)
                throw new IOException("Release asset version or product name mismatch.");
            return;
        }
        throw new IOException("Too many release asset redirects.");
    }

    private static bool Install(string stage, string target, string token)
    {
        var backup = target + "." + token + ".backup";
        try
        {
            File.Replace(stage, target, backup);
            using var restarted = Process.Start(AutomaticUpdate.StartInfo(target, "--resume-tray"));
            if (restarted is not null && !restarted.WaitForExit(2500))
            {
                AutomaticUpdate.TryDelete(backup);
                return true;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception) { }

        // Keep the backup on disk if Windows refuses the rollback.
        if (File.Exists(backup))
            File.Move(backup, target, overwrite: true);
        if (File.Exists(target))
            Process.Start(AutomaticUpdate.StartInfo(target, "--resume-tray"))?.Dispose();
        return false;
    }

    private static bool TryVersion(ReadOnlySpan<char> text, out Version version)
    {
        int metadata = text.IndexOf('+');
        if (metadata >= 0)
            text = text[..metadata];
        if (Version.TryParse(text, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
            return true;
        }
        version = new Version();
        return false;
    }
}
