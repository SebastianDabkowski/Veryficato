using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Verificato.Core;

public static partial class Privacy
{
    [GeneratedRegex(@"(?i)(bearer\s+)[\w.\-+/=]+")]
    private static partial Regex Bearer();
    [GeneratedRegex("""(?i)((?:"?(?:password|passwd|token|secret|api[_-]?key|authorization|cookie)"?)\s*[=:]\s*)(?:"[^"]*"|'[^']*'|[^\s,;]+)""")]
    private static partial Regex Secrets();
    [GeneratedRegex(@"(?i)(\b(?:authorization|cookie)\s*:\s*)[^\r\n]+")]
    private static partial Regex Headers();
    [GeneratedRegex(@"https?://[^\s<>\""']+")]
    private static partial Regex Urls();

    public static string? SafeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path);
    }
    public static string Clean(string? value, int maxLength = 4000)
    {
        var text = value ?? "";
        text = Urls().Replace(text, m => SafeUrl(m.Value) ?? "[url]");
        text = Bearer().Replace(text, "$1[redacted]");
        text = Headers().Replace(text, "$1[redacted]");
        text = Secrets().Replace(text, "$1[redacted]");
        return text.Length > maxLength ? text[..maxLength] + "…" : text;
    }
    public static Observation Sanitize(Observation item) => item with
    {
        Title = Clean(item.Title, 240), Message = Clean(item.Message), Url = SafeUrl(item.Url),
        Stack = item.Stack is null ? null : Clean(item.Stack, 8000),
        FilePath = item.FilePath is null ? null : Clean(item.FilePath, 1000),
        Value = item.Value.HasValue && !double.IsFinite(item.Value.Value) ? null : item.Value,
        OccurredAt = item.OccurredAt ?? DateTimeOffset.UtcNow,
        Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id
    };
    public static string Fingerprint(Observation item)
    {
        var input = $"{item.RuleId}|{item.Title}|{item.Message}|{item.Url}|{item.FilePath}|{item.Line}|{item.Column}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}

public static class ProjectValidation
{
    public static Project Validate(ProjectInput input, Project? existing = null)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120)
            throw new ArgumentException("Nazwa projektu musi mieć od 1 do 120 znaków.");
        if (string.IsNullOrWhiteSpace(input.SourcePath) || !Path.IsPathFullyQualified(input.SourcePath))
            throw new ArgumentException("Podaj bezwzględną ścieżkę do katalogu źródeł.");
        var path = Path.GetFullPath(input.SourcePath);
        if (!Directory.Exists(path)) throw new ArgumentException("Katalog źródeł nie istnieje.");
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Ścieżka źródeł nie może przechodzić przez link symboliczny/reparse point.");
        var uri = ValidateUrl(input.ApplicationUrl);
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("URL aplikacji nie może zawierać parametrów ani fragmentu (potencjalnych sekretów).");
        var origins = (input.AllowedOrigins ?? []).Select(x => ValidateUrl(x).GetLeftPart(UriPartial.Authority))
            .Append(uri.GetLeftPart(UriPartial.Authority)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (origins.Length > 20) throw new ArgumentException("Maksymalnie 20 dozwolonych originów.");
        var now = DateTimeOffset.UtcNow;
        return new Project(existing?.Id ?? Guid.NewGuid(), input.Name.Trim(), path, uri.AbsoluteUri,
            origins, existing?.CreatedAt ?? now, now);
    }
    private static Uri ValidateUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("URL musi używać HTTP(S) i nie może zawierać danych logowania.");
        return uri;
    }
}

public sealed class OriginScope(IEnumerable<string> origins)
{
    private readonly HashSet<string> allowed = new(origins, StringComparer.OrdinalIgnoreCase);
    public bool Contains(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && allowed.Contains(uri.GetLeftPart(UriPartial.Authority));
}
