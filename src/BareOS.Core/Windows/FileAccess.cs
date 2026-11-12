using System.Security.Cryptography;

namespace BareOS.Core.Windows;

/// <summary>Реализация IFileAccess: ресурсы лежат в каталоге ресурсов, резервные копии — в каталоге бэкапов прогонов.</summary>
public sealed class FileAccess : IFileAccess
{
    private readonly string _resourceDir;
    private readonly string _backupDir;

    public FileAccess(string resourceDir, string backupDir)
    {
        _resourceDir = resourceDir;
        _backupDir = backupDir;
    }

    private static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

    private string SourcePath(string source)
    {
        var full = Path.GetFullPath(Path.Combine(_resourceDir, source));
        if (!full.StartsWith(Path.GetFullPath(_resourceDir), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Ресурс вне каталога ресурсов: {source}");
        if (!File.Exists(full)) throw new FileNotFoundException($"Ресурс не найден: {full}");
        return full;
    }

    private static string Hash(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public FileState Read(string dest)
    {
        var p = Expand(dest);
        return File.Exists(p) ? new FileState(true, Hash(p)) : new FileState(false);
    }

    public string SourceHash(string source) => Hash(SourcePath(source));

    public string Backup(string dest, string tag)
    {
        Directory.CreateDirectory(_backupDir);
        var safe = string.Concat(tag.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var bak = Path.Combine(_backupDir, safe + ".bak");
        File.Copy(Expand(dest), bak, overwrite: true);
        return bak;
    }

    public void Copy(string source, string dest)
    {
        var d = Expand(dest);
        Directory.CreateDirectory(Path.GetDirectoryName(d)!);
        File.Copy(SourcePath(source), d, overwrite: true);
    }

    public void Restore(string backupPath, string dest)
    {
        var d = Expand(dest);
        Directory.CreateDirectory(Path.GetDirectoryName(d)!);
        File.Copy(backupPath, d, overwrite: true);
    }

    public void Delete(string dest)
    {
        var d = Expand(dest);
        if (File.Exists(d)) File.Delete(d);
    }
}
