using Microsoft.AspNetCore.Http;
using Verificato.Core;
using Verificato.Host;

namespace Verificato.Tests;

public sealed class PolicyTests
{
    [Theory]
    [InlineData("http://localhost:5173/a", true)]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("http://localhost:5174/", false)]
    [InlineData("https://localhost:5173/", false)]
    [InlineData("http://127.0.0.1:5173/", false)]
    [InlineData("http://localhost.evil.test:5173/", false)]
    [InlineData("file:///etc/passwd", false)]
    public void ScopeIsExactOrigin(string url, bool expected) =>
        Assert.Equal(expected, new OriginScope(["http://localhost:5173"]).Contains(url));

    [Fact]
    public void RedactsUrlsCredentialsAndSecretsBeforeFingerprinting()
    {
        var observation = new Observation(Guid.NewGuid(), "test", Category.Http, Severity.Error, "Error",
            "GET http://user:pass@localhost:5000/api?token=abc#secret Bearer abc.def password=unsafe api_key=key",
            "http://user:pass@localhost:5000/api?token=abc#secret");
        var clean = Privacy.Sanitize(observation);
        Assert.Equal("http://localhost:5000/api", clean.Url);
        Assert.DoesNotContain("unsafe", clean.Message);
        Assert.DoesNotContain("abc", clean.Message);
        Assert.DoesNotContain("user:pass", clean.Message);
        Assert.DoesNotContain("#secret", clean.Message);
        Assert.Equal(Privacy.Fingerprint(clean), Privacy.Fingerprint(Privacy.Sanitize(observation with { Url = "http://localhost:5000/api?token=another" })));
    }
    [Theory]
    [InlineData("ftp://localhost/test")]
    [InlineData("http://user:pass@localhost/")]
    [InlineData("http://localhost/?token=secret")]
    [InlineData("http://localhost/#secret")]
    public void RejectsUnsafeProjectUrl(string url)
    {
        using var directory = new TestDirectory();
        Assert.Throws<ArgumentException>(() => ProjectValidation.Validate(new("Test", directory.Path, url)));
    }
    [Theory]
    [InlineData("{\"token\":\"private value with spaces\"}", "private value")]
    [InlineData("Authorization: Basic c2VjcmV0", "c2VjcmV0")]
    [InlineData("Cookie: session=private-session; other=private-other", "private-")]
    [InlineData("apiKey='private value'", "private value")]
    public void RedactsStructuredAndHeaderSecrets(string input, string secret) =>
        Assert.DoesNotContain(secret, Privacy.Clean(input));

    [Fact]
    public void BootstrapIsOneTimeAndCookieHttpOnly()
    {
        var access = new LocalAccess();
        var context = new DefaultHttpContext();
        var token = access.BootstrapToken;
        var csrf = access.Exchange(token, context.Response);
        Assert.NotNull(csrf);
        Assert.Null(access.Exchange(token, new DefaultHttpContext().Response));
        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        var request = new DefaultHttpContext().Request;
        request.Headers.Cookie = cookie.Split(';')[0];
        Assert.Equal(csrf, access.Authenticate(request));
    }
    [Fact]
    public void CoreHasNoAdapterDependencies()
    {
        var references = typeof(Project).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(references, x => x is not null && (x.Contains("Playwright") || x.Contains("Sqlite") || x.Contains("CodeAnalysis") || x.Contains("AspNetCore")));
    }
}
