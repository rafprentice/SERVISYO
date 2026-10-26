using Microsoft.EntityFrameworkCore;
using Servisyo.Web.Data;
using Servisyo.Web.Models;

namespace Servisyo.Web.Services;

public enum PhotoOutcome { Saved, Removed, NotAllowed, NoFile, TooLarge, NotAnImage }

public record PhotoResult(PhotoOutcome Outcome, string Message, string Role)
{
    public bool Succeeded => Outcome is PhotoOutcome.Saved or PhotoOutcome.Removed;
}

public interface IProfilePhotoService
{
    /// Saves a new profile photo for the signed-in customer or provider (replacing the old one).
    Task<PhotoResult> SaveAsync(int userId, IFormFile? file, CancellationToken ct = default);

    /// Removes the profile photo (the initials are shown again).
    Task<PhotoResult> RemoveAsync(int userId, CancellationToken ct = default);

    /// The photo address for the sidebar ("/uploads/avatars/…"), or null when there is none.
    Task<string?> GetUrlAsync(int userId, CancellationToken ct = default);
}

/// Rules: Active customer or provider accounts only; JPG, PNG or WebP; at most 2 MB. The file type is decided by the
/// file's first bytes (not by its name or by what the browser claims), and it is saved under a new random name,
/// so a visitor can never choose the file name, the folder or the extension.
public class ProfilePhotoService : IProfilePhotoService
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const string UrlFolder = "/uploads/avatars/";

    private readonly ServisyoDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProfilePhotoService> _logger;

    public ProfilePhotoService(ServisyoDbContext db, IWebHostEnvironment env, ILogger<ProfilePhotoService>? logger = null)
    {
        _db = db;
        _env = env;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfilePhotoService>.Instance;
    }

    private string Folder => Path.Combine(_env.WebRootPath, "uploads", "avatars");

    /// The customer or provider row of an ACTIVE user (from the sign-in cookie).
    private async Task<(Customer? Customer, Provider? Provider)> FindAsync(int userId, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.UserId == userId
            && c.User.Role == Roles.Customer && c.User.AccountStatus == AccountStatuses.Active, ct);
        if (customer is not null) return (customer, null);

        var provider = await _db.Providers.FirstOrDefaultAsync(p => p.UserId == userId
            && p.User.Role == Roles.ServiceProvider && p.User.AccountStatus == AccountStatuses.Active, ct);
        return (null, provider);
    }

    public async Task<string?> GetUrlAsync(int userId, CancellationToken ct = default)
    {
        var photo = await _db.Customers.AsNoTracking().Where(c => c.UserId == userId).Select(c => c.ProfilePhoto).FirstOrDefaultAsync(ct)
                    ?? await _db.Providers.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.ProfilePhoto).FirstOrDefaultAsync(ct);
        return IsOurs(photo) ? photo : null;
    }

    public async Task<PhotoResult> SaveAsync(int userId, IFormFile? file, CancellationToken ct = default)
    {
        var (customer, provider) = await FindAsync(userId, ct);
        if (customer is null && provider is null) return new(PhotoOutcome.NotAllowed, string.Empty, string.Empty);
        var role = customer is not null ? Roles.Customer : Roles.ServiceProvider;

        if (file is null || file.Length == 0) return new(PhotoOutcome.NoFile, "Choose a photo to upload.", role);
        if (file.Length > MaxBytes) return new(PhotoOutcome.TooLarge, "That photo is too large. Please choose one under 2 MB.", role);

        // read it once into memory (2 MB at most) and look at the first bytes
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var ext = DetectImage(bytes);
        if (ext is null) return new(PhotoOutcome.NotAnImage, "Please upload a JPG, PNG or WebP image.", role);

        Directory.CreateDirectory(Folder);
        var name = $"{userId}-{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Folder, name), bytes, ct);

        var old = customer?.ProfilePhoto ?? provider?.ProfilePhoto;
        var url = UrlFolder + name;
        if (customer is not null) customer.ProfilePhoto = url; else provider!.ProfilePhoto = url;
        await _db.SaveChangesAsync(ct);

        DeleteFile(old);
        _logger.LogInformation("User {UserId} uploaded a new profile photo ({Bytes} bytes).", userId, bytes.Length);
        return new(PhotoOutcome.Saved, "Your profile photo was updated.", role);
    }

    public async Task<PhotoResult> RemoveAsync(int userId, CancellationToken ct = default)
    {
        var (customer, provider) = await FindAsync(userId, ct);
        if (customer is null && provider is null) return new(PhotoOutcome.NotAllowed, string.Empty, string.Empty);
        var role = customer is not null ? Roles.Customer : Roles.ServiceProvider;

        var old = customer?.ProfilePhoto ?? provider?.ProfilePhoto;
        if (customer is not null) customer.ProfilePhoto = null; else provider!.ProfilePhoto = null;
        await _db.SaveChangesAsync(ct);

        DeleteFile(old);
        return new(PhotoOutcome.Removed, "Your profile photo was removed.", role);
    }

    /// ".jpg" / ".png" / ".webp" from the file's own signature, or null when it is not one of those.
    public static string? DetectImage(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                          && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ".png";
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                           && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return ".webp";
        return null;
    }

    /// Only a plain file name inside /uploads/avatars/ counts as ours (never a path that climbs out of the folder).
    private static bool IsOurs(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.StartsWith(UrlFolder, StringComparison.Ordinal)
        && url.Length > UrlFolder.Length
        && url[UrlFolder.Length..].IndexOfAny(new[] { '/', '\\' }) < 0
        && !url.Contains("..", StringComparison.Ordinal);

    private void DeleteFile(string? url)
    {
        if (!IsOurs(url)) return;
        try
        {
            var path = Path.Combine(Folder, url![UrlFolder.Length..]);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the old profile photo {Url}.", url);     // harmless: just an unused file
        }
    }
}
