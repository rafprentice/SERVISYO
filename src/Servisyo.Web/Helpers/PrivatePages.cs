namespace Servisyo.Web.Helpers;

/// Pages and answers for a signed-in user (a provider's days off, bookings, profile, ...) belong to that one account.
/// They are sent with "Cache-Control: no-store", so the browser never keeps a copy: after logging out (or when another
/// account logs in on the same computer) the Back button or a cached page cannot show the previous account's data -
/// every page is fetched again from the server for whoever is signed in now.
/// Responses that already say how to cache themselves (e.g. the fingerprinted CSS/JS files) are left alone.
public static class PrivatePages
{
    public const string NoStore = "no-store";

    public static void MarkNoStore(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.WebSockets.IsWebSocketRequest) return;
        if (context.Response.Headers.ContainsKey("Cache-Control")) return;
        context.Response.Headers.CacheControl = NoStore;
    }

    /// app.UsePrivatePages() - after UseAuthentication, so the signed-in user is known.
    public static IApplicationBuilder UsePrivatePages(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                MarkNoStore(context);
                return Task.CompletedTask;
            });
            await next();
        });
}
