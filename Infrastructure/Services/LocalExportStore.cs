using System.Security.Cryptography;
using System.Text.RegularExpressions;
using APCS.Application.Abstractions.Storage;
using Microsoft.Extensions.Configuration;

namespace APCS.Infrastructure.Services;

public sealed class LocalExportStore(IConfiguration configuration) : ILocalExportStore
{
    private const long MaximumBytes = 400L * 1024 * 1024;
    private static readonly Regex KeyPattern = new("^local-export/[0-9a-fA-F-]{36}/[0-9a-fA-F-]{36}$", RegexOptions.Compiled);
    private readonly string _root = Path.GetFullPath(configuration["Exports:LocalDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "private-exports"));

    public async Task<LocalExportFile> SaveAsync(Guid jobId, Guid leaseToken, Stream content, CancellationToken ct)
    {
        var key = $"local-export/{jobId:D}/{leaseToken:D}";
        var target = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".tmp";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaximumBytes) throw new InvalidDataException("ZIP export exceeds the 400 MiB limit.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await output.FlushAsync(ct);
            }
            if (total < 22) throw new InvalidDataException("ZIP export is empty or invalid.");
            File.Move(temporary, target, overwrite: true);
            return new(key, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public async Task<bool> VerifyAsync(string key, string sha256, long? bytes, CancellationToken ct)
    {
        var file = Resolve(key);
        if (!File.Exists(file)) return false;
        var info = new FileInfo(file);
        if (info.Length < 22 || (bytes.HasValue && info.Length != bytes.Value)) return false;
        await using var input = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
        return string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(file)
            ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)
            : null);
    }

    private string Resolve(string key)
    {
        if (!KeyPattern.IsMatch(key)) throw new InvalidDataException("Invalid local export key.");
        var parts = key.Split('/');
        return Path.Combine(_root, parts[1], parts[2] + ".zip");
    }
}
