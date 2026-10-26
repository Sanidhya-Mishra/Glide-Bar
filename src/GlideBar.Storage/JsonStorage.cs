using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GlideBar.Core;
using GlideBar.Core.Abstractions;

namespace GlideBar.Storage;

/// <summary>
/// Typed JSON persistence on top of <see cref="System.Text.Json"/>.
/// <para>
/// Files live in <see cref="AppPaths.DataDirectory"/>; the <c>key</c> maps to
/// <c>&lt;DataDirectory&gt;\&lt;key&gt;.json</c>. Writes are <b>atomic</b>: the payload is
/// serialized to a temp file on the same volume and then swapped over the target with
/// <see cref="File.Replace(string,string,string)"/> (or <see cref="File.Move(string,string)"/>
/// when no target exists yet), so a crash mid-write can never leave a half-written config.
/// </para>
/// <para>Reads tolerate a missing or corrupt file by returning <c>null</c> rather than throwing.</para>
/// </summary>
public sealed class JsonStorage : IStorage
{
    // Source-gen context first (reflection-free for AppSettings &amp; co.), then a reflection
    // fallback so the generic contract still works for any other type a caller might pass.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            StorageJsonContext.Default,
            new DefaultJsonTypeInfoResolver()),
    };

    public async Task<T?> LoadAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
            var value = await JsonSerializer
                .DeserializeAsync(fs, typeof(T), Options, ct)
                .ConfigureAwait(false);
            return value as T;
        }
        catch (OperationCanceledException)
        {
            throw; // honour the caller's cancellation
        }
        catch
        {
            // Missing/corrupt/locked file: tolerate, behave as "nothing stored yet".
            return null;
        }
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        AppPaths.EnsureDataDirectory();
        var path = PathFor(key);
        var dir = Path.GetDirectoryName(path)!;

        // Temp file in the SAME directory => same volume => File.Replace/Move is atomic.
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var fs = new FileStream(
                tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, value, typeof(T), Options, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                // Atomic swap, discarding backup; ignore metadata/ACL copy errors.
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>
    /// Maps a logical key to a file under <see cref="AppPaths.DataDirectory"/>.
    /// The key is reduced to its file name (path-traversal safe) and gets a <c>.json</c> extension.
    /// </summary>
    private static string PathFor(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            key = "config";

        var name = Path.GetFileName(key.Trim());
        if (string.IsNullOrEmpty(name))
            name = "config";
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            name += ".json";

        return Path.Combine(AppPaths.DataDirectory, name);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
