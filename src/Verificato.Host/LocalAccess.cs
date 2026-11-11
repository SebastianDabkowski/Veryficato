using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Verificato.Host;

public sealed class LocalAccess
{
    private sealed record Session(string Csrf, DateTimeOffset ExpiresAt);
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly object bootstrapLock = new();
    private string? bootstrap = Token();
    private readonly DateTimeOffset bootstrapExpires = DateTimeOffset.UtcNow.AddMinutes(10);
    public string BootstrapToken => bootstrap ?? throw new InvalidOperationException("Bootstrap został już wykorzystany.");
    public const string CookieName = "verificato-session";
    public string? Exchange(string candidate, HttpResponse response)
    {
        lock (bootstrapLock)
        {
            if (bootstrap is null || DateTimeOffset.UtcNow > bootstrapExpires || !Equal(candidate, bootstrap)) return null;
            bootstrap = null;
        }
        var sessionToken = Token();
        var csrf = Token();
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        sessions[sessionToken] = new(csrf, expires);
        response.Cookies.Append(CookieName, sessionToken, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = false, IsEssential = true,
            Path = "/", MaxAge = TimeSpan.FromHours(12)
        });
        return csrf;
    }
    public string? Authenticate(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var key) || !sessions.TryGetValue(key, out var session)) return null;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow) { sessions.TryRemove(key, out _); return null; }
        return session.Csrf;
    }
    public void Logout(HttpRequest request, HttpResponse response)
    {
        if (request.Cookies.TryGetValue(CookieName, out var key)) sessions.TryRemove(key, out _);
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Strict });
    }
    public static bool Equal(string a, string b) => a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
    private static string Token() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}

public sealed class LocalSecurityMiddleware(RequestDelegate next, string[] allowedOrigins)
{
    public async Task InvokeAsync(HttpContext context, LocalAccess access)
    {
        var request = context.Request;
        var remote = context.Connection.RemoteIpAddress;
        if ((remote is not null && !IPAddress.IsLoopback(remote)) ||
            !allowedOrigins.Contains($"http://{request.Host.Value}", StringComparer.OrdinalIgnoreCase))
        { await Reject(context, 403, "Dozwolony wyłącznie lokalny host 127.0.0.1."); return; }
        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        { await Reject(context, 403, "Niedozwolony origin."); return; }
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' ws://{request.Host.Value}; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
        if (!request.Path.StartsWithSegments("/api") && !request.Path.StartsWithSegments("/hubs"))
        { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        if (request.Path == "/api/v1/auth/bootstrap" && HttpMethods.IsPost(request.Method))
        { await next(context); return; }
        var csrf = access.Authenticate(request);
        if (csrf is null) { await Reject(context, 401, "Otwórz Verificato za pomocą lokalnego launchera."); return; }
        var changing = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
        // SignalR carries server notifications only. Require an allowed Origin for all hub transports.
        if (request.Path.StartsWithSegments("/hubs"))
        {
            if (origin.Length == 0) { await Reject(context, 403, "Brak originu połączenia."); return; }
        }
        else if (changing && (origin.Length == 0 || !LocalAccess.Equal(request.Headers["X-Verificato-CSRF"].ToString(), csrf)))
        { await Reject(context, 403, "Nieprawidłowy token CSRF."); return; }
        await next(context);
    }
    private static Task Reject(HttpContext context, int code, string message) =>
        Results.Problem(statusCode: code, title: message).ExecuteAsync(context);
}
