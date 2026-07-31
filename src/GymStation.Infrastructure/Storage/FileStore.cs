namespace GymStation.Infrastructure.Storage;

/// <summary>
/// Media storage abstraction (portraits, logos, hero images). v1 backs onto a local
/// volume; the interface keeps a future object-store swap from touching callers.
/// </summary>
public interface IFileStore
{
    Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);
    bool Exists(string relativePath);
}

public class LocalFileStore(string root) : IFileStore
{
    private string Resolve(string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relativePath));

        // Relative-path containment (not a prefix check, which "/root" vs "/root2" would bypass).
        var rel = Path.GetRelativePath(rootFull, full);
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(rel))
        {
            throw new InvalidOperationException("Path escapes the file store root.");
        }

        return full;
    }

    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct = default)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return relativePath;
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var full = Resolve(relativePath);
        return Task.FromResult<Stream?>(File.Exists(full) ? File.OpenRead(full) : null);
    }

    public bool Exists(string relativePath) => File.Exists(Resolve(relativePath));
}
