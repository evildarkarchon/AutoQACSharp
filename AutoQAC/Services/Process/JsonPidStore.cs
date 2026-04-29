using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoQAC.Infrastructure.Logging;
using AutoQAC.Models;

namespace AutoQAC.Services.Process;

/// <summary>
/// JSON-backed PID store that serializes read-modify-write operations with process-local and file locks.
/// </summary>
public sealed class JsonPidStore(IPidStorePathProvider pathProvider, ILoggingService logger) : IPidStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrackedProcess>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var stream = OpenPidFile();
            await LockWithRetryAsync(stream, ct).ConfigureAwait(false);
            var locked = true;
            try
            {
                return await ReadEntriesAsync(stream, resetOnCorrupt: true, ct).ConfigureAwait(false);
            }
            finally
            {
                UnlockQuietly(stream, ref locked);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        Func<IReadOnlyList<TrackedProcess>, IReadOnlyList<TrackedProcess>> update,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var stream = OpenPidFile();
            await LockWithRetryAsync(stream, ct).ConfigureAwait(false);
            var locked = true;
            try
            {
                var existing = await ReadEntriesAsync(stream, resetOnCorrupt: true, ct).ConfigureAwait(false);
                var updated = update(existing);
                await WriteEntriesAsync(stream, updated, ct).ConfigureAwait(false);
            }
            finally
            {
                UnlockQuietly(stream, ref locked);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private FileStream OpenPidFile()
    {
        var directory = Path.GetDirectoryName(pathProvider.PidFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(
            pathProvider.PidFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
    }

    private async Task<IReadOnlyList<TrackedProcess>> ReadEntriesAsync(
        FileStream stream,
        bool resetOnCorrupt,
        CancellationToken ct)
    {
        try
        {
            stream.Position = 0;
            if (stream.Length == 0)
            {
                return [];
            }

            var entries = await JsonSerializer.DeserializeAsync<List<TrackedProcess>>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);
            return entries ?? [];
        }
        catch (JsonException ex) when (resetOnCorrupt)
        {
            await PreserveCorruptStoreAsync(stream, ct).ConfigureAwait(false);
            logger.Error(ex, "[Orphan] PID store JSON was corrupt and has been reset: {Path}", pathProvider.PidFilePath);
            await WriteEntriesAsync(stream, [], ct).ConfigureAwait(false);
            return [];
        }
    }

    private async Task PreserveCorruptStoreAsync(FileStream stream, CancellationToken ct)
    {
        stream.Position = 0;
        var copyPath = Path.Combine(
            Path.GetDirectoryName(pathProvider.PidFilePath) ?? string.Empty,
            $"autoqac-pids.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");

        await using var copy = new FileStream(copyPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await stream.CopyToAsync(copy, ct).ConfigureAwait(false);
    }

    private static async Task WriteEntriesAsync(
        FileStream stream,
        IReadOnlyList<TrackedProcess> entries,
        CancellationToken ct)
    {
        stream.SetLength(0);
        stream.Position = 0;
        await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task LockWithRetryAsync(FileStream stream, CancellationToken ct)
    {
        const int maxAttempts = 8;
        var delay = TimeSpan.FromMilliseconds(25);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                stream.Lock(0, 1);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay += delay;
            }
        }
    }

    private static void UnlockQuietly(FileStream stream, ref bool locked)
    {
        if (!locked)
        {
            return;
        }

        try
        {
            stream.Unlock(0, 1);
            locked = false;
        }
        catch (IOException)
        {
            // Unlock can race disposal after exceptional I/O; the original operation result is more useful to callers.
        }
    }
}
