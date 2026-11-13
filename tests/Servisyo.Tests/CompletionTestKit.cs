using Microsoft.AspNetCore.Http;
using Servisyo.Web.Services;

namespace Servisyo.Tests;

/// Database v2.11: what a provider sends in "Confirm Service Completion", for tests (nothing is written to disk).
internal static class CompletionTestKit
{
    public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3 };
    public static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3 };
    public const string Message = "Fixed the leak and tested it for 30 minutes.";

    public static IFormFile Photo(byte[]? bytes = null, string name = "proof.png", string type = "image/png")
    {
        var b = bytes ?? Png;
        return new FormFile(new MemoryStream(b), 0, b.Length, "proofPhoto", name) { Headers = new HeaderDictionary(), ContentType = type };
    }
}

/// Keeps the "saved" proof photos in memory, so tests can see what was saved and what was deleted again.
internal sealed class MemoryPhotoStore : ICompletionPhotoStore
{
    public List<string> Saved { get; } = new();
    public List<string> Deleted { get; } = new();

    public Task<string> SaveAsync(int bookingId, byte[] bytes, string extension, CancellationToken ct = default)
    {
        var url = $"{CompletionPhotoStore.UrlFolder}{bookingId}_{Guid.NewGuid()}{extension}";
        Saved.Add(url);
        return Task.FromResult(url);
    }

    public void Delete(string? url)
    {
        if (url is not null) Deleted.Add(url);
    }
}
