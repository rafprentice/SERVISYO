namespace Servisyo.Web.Services;

/// Database v2.11: the rules for completing a job with proof. The browser checks the same things first (friendlier),
/// but only these checks on the server decide.
public static class CompletionRules
{
    public const int MaxMessageLength = 1000;                      // BOOKINGS.COMPLETION_MESSAGE NVARCHAR(1000)
    public const long MaxPhotoBytes = 5 * 1024 * 1024;             // 5 MB: a normal phone photo
    /// The whole request (photo + message + hidden fields); a bigger request is refused before it is read.
    public const long MaxRequestBytes = MaxPhotoBytes + 64 * 1024;
    public static readonly IReadOnlyList<string> AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

    public const string MessageRequired = "Completion message is required. Please input a message before submitting.";
    public const string MessageTooLong = "Please keep the completion message to 1,000 characters or fewer.";
    public const string PhotoRequired = "Please select a proof photo from your file manager.";
    public const string PhotoTooLarge = "That photo is too large. Please choose one under 5 MB.";
    public const string PhotoNotAnImage = "Please choose a JPG, PNG or WebP photo.";
    public const string ProofRequired = "To mark a booking completed, add a completion message and a proof photo.";
}

/// What was wrong with the proof (null = that part is fine). Shown under the matching field of the modal.
public sealed record CompletionErrors(string? Message, string? Photo)
{
    public bool Any => Message is not null || Photo is not null;
}

/// Where the proof photos are kept: wwwroot/uploads/completions (outside Git and outside the deploy zip, like the
/// profile photos). The file name is made by the server ("{bookingId}_{guid}.jpg"), never taken from the browser.
public interface ICompletionPhotoStore
{
    /// Saves the photo and returns its address, e.g. "/uploads/completions/31_6f1c...e2.jpg".
    Task<string> SaveAsync(int bookingId, byte[] bytes, string extension, CancellationToken ct = default);

    /// Deletes a saved photo again (used when the booking could not be saved). Never throws.
    void Delete(string? url);
}

public sealed class CompletionPhotoStore : ICompletionPhotoStore
{
    public const string UrlFolder = "/uploads/completions/";

    private readonly string _folder;
    private readonly ILogger<CompletionPhotoStore> _logger;

    public CompletionPhotoStore(string folder, ILogger<CompletionPhotoStore>? logger = null)
    {
        _folder = folder;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CompletionPhotoStore>.Instance;
    }

    /// The real folder: wwwroot/uploads/completions.
    public static CompletionPhotoStore ForWebRoot(IWebHostEnvironment env, ILogger<CompletionPhotoStore>? logger = null) =>
        new(Path.Combine(env.WebRootPath, "uploads", "completions"), logger);

    public async Task<string> SaveAsync(int bookingId, byte[] bytes, string extension, CancellationToken ct = default)
    {
        if (!CompletionRules.AllowedExtensions.Contains(extension)) throw new ArgumentException("Not an allowed image type.", nameof(extension));
        Directory.CreateDirectory(_folder);
        var name = $"{bookingId}_{Guid.NewGuid()}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(_folder, name), bytes, ct);
        return UrlFolder + name;
    }

    public void Delete(string? url)
    {
        if (!IsOurs(url)) return;
        try
        {
            var path = Path.Combine(_folder, url![UrlFolder.Length..]);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the proof photo {Url}.", url);    // harmless: just an unused file
        }
    }

    /// Only a plain file name inside /uploads/completions/ counts as ours (never a path that climbs out of the folder).
    /// The pages show a photo only when this is true.
    public static bool IsOurs(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.StartsWith(UrlFolder, StringComparison.Ordinal)
        && url.Length > UrlFolder.Length
        && url[UrlFolder.Length..].IndexOfAny(new[] { '/', '\\' }) < 0
        && !url.Contains("..", StringComparison.Ordinal);
}
