using ERecycleHub.Domain;
using ERecycleHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ERecycleHub.Application;

public sealed class StorageOptions
{
    /// <summary>Directory for uploaded bytes. Outside the web root; files are only reachable through the authorised files endpoint.</summary>
    public string Root { get; set; } = "App_Data/files";
}

public sealed record UploadInput(Stream Content, string FileName, long Length);

/// <summary>Validates and stores uploads on local disk.</summary>
// ponytail: local disk only; extract an interface when a second backend (blob storage) is actually needed.
public sealed class FileService(AppDbContext db, IOptions<StorageOptions> options, TimeProvider clock)
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxDocumentBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> ImageTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };
    private static readonly Dictionary<string, string> DocumentTypes = new()
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Which kinds of file each category accepts and whether anonymous visitors may read them.</summary>
    private static (bool images, bool documents, bool isPublic) Policy(FileCategory c) => c switch
    {
        FileCategory.AuctionImage or FileCategory.PublisherImage or FileCategory.Cms or FileCategory.UserAvatar => (true, false, true),
        FileCategory.KycDocument or FileCategory.TransactionProof => (true, true, false),
        FileCategory.AuctionDocument => (true, true, true),
        _ => (false, false, false),
    };

    public string RootPath => Path.GetFullPath(options.Value.Root);

    /// <summary>Validates and writes the bytes, and adds the metadata row to the context (the caller saves).</summary>
    public async Task<StoredFile> SaveAsync(UploadInput file, FileCategory category, int? ownerUserId, CancellationToken ct = default)
    {
        var (images, documents, isPublic) = Policy(category);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        string? contentType = null;
        if (images && ImageTypes.TryGetValue(ext, out var it)) contentType = it;
        else if (documents && DocumentTypes.TryGetValue(ext, out var dt)) contentType = dt;
        if (contentType is null) throw new AppException("file.type_not_allowed");
        if (file.Length <= 0) throw new AppException("file.empty");
        if (file.Length > (contentType.StartsWith("image/") ? MaxImageBytes : MaxDocumentBytes)) throw new AppException("file.too_large");

        var head = new byte[12];
        var read = await file.Content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (!MagicBytesMatch(head.AsSpan(0, read), ext)) throw new AppException("file.content_mismatch");

        var now = clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), id.ToString("N") + ext);
        var full = Path.Combine(RootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var output = File.Create(full))
        {
            await output.WriteAsync(head.AsMemory(0, read), ct);
            await file.Content.CopyToAsync(output, ct);
        }

        var stored = new StoredFile
        {
            Id = id,
            Category = category,
            IsPublic = isPublic,
            OwnerUserId = ownerUserId,
            OriginalName = Path.GetFileName(file.FileName).Length > 200 ? Path.GetFileName(file.FileName)[..200] : Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            StoragePath = relative.Replace('\\', '/'),
            CreatedAtUtc = now,
        };
        db.Files.Add(stored);
        return stored;
    }

    public async Task<Guid?> SaveOptionalAsync(UploadInput? file, FileCategory category, int? ownerUserId, Guid? current, CancellationToken ct = default) =>
        file is null ? current : (await SaveAsync(file, category, ownerUserId, ct)).Id;

    /// <summary>Returns the file if the caller may read it: public files for everyone, private ones only for their owner and administrators.</summary>
    public async Task<(StoredFile file, string path)?> OpenAsync(Guid id, Actor? actor, CancellationToken ct = default)
    {
        var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return null;
        var allowed = f.IsPublic || (actor is not null && (actor.IsAdmin || actor.UserId == f.OwnerUserId));
        if (!allowed) return null;
        var path = Path.GetFullPath(Path.Combine(RootPath, f.StoragePath));
        if (!path.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return (f, path);
    }

    private static bool MagicBytesMatch(ReadOnlySpan<byte> h, string ext)
    {
        if (h.Length < 4) return false;
        return ext switch
        {
            ".jpg" or ".jpeg" => h[0] == 0xFF && h[1] == 0xD8,
            ".png" => h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47,
            ".gif" => h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46,
            ".webp" => h.Length >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50,
            ".pdf" => h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46,
            ".docx" or ".xlsx" => h[0] == 0x50 && h[1] == 0x4B,
            ".doc" or ".xls" => h[0] == 0xD0 && h[1] == 0xCF && h[2] == 0x11 && h[3] == 0xE0,
            _ => false,
        };
    }
}
