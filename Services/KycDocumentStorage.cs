namespace SpeedSaga.API.Services;

public class KycDocumentStorage
{
    static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };

    readonly string _root;

    public KycDocumentStorage(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "KycDocuments");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Guid playerId, string docKind, Stream stream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException("Upload JPG, PNG, WEBP, or PDF only");

        var playerDir = Path.Combine(_root, playerId.ToString("N"));
        Directory.CreateDirectory(playerDir);
        var stored = $"{docKind}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext.ToLowerInvariant()}";
        var fullPath = Path.Combine(playerDir, stored);
        await using (var fs = File.Create(fullPath))
            await stream.CopyToAsync(fs, ct);

        return Path.Combine(playerId.ToString("N"), stored).Replace('\\', '/');
    }
}
