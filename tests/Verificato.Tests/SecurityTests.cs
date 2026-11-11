using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Verificato.Host;

namespace Verificato.Tests;

public sealed class SecurityTests
{
    private const string Origin = "http://127.0.0.1:5078";
    [Theory]
    [InlineData("localhost", "", 403)]
    [InlineData("evil.test", "", 403)]
    [InlineData("127.0.0.1", "http://evil.test", 403)]
    [InlineData("127.0.0.1", "", 401)]
    public async Task RejectsUnauthorizedHostOriginAndRequests(string host, string origin, int expected)
    {
        var context = Context(host, origin);
        await new LocalSecurityMiddleware(_ => throw new Exception("Should not run"), [Origin]).InvokeAsync(context, new LocalAccess());
        Assert.Equal(expected, context.Response.StatusCode);
    }
    [Fact]
    public async Task RequiresCsrfAndOriginForMutations()
    {
        var access = new LocalAccess();
        var bootstrap = Context();
        var csrf = access.Exchange(access.BootstrapToken, bootstrap.Response)!;
        var cookie = bootstrap.Response.Headers.SetCookie.ToString().Split(';')[0];
        foreach (var valid in new[] { false, true })
        {
            var context = Context(origin: Origin);
            context.Request.Method = "POST";
            context.Request.Headers.Cookie = cookie;
            if (valid) context.Request.Headers["X-Verificato-CSRF"] = csrf;
            var called = false;
            await new LocalSecurityMiddleware(_ => { called = true; return Task.CompletedTask; }, [Origin]).InvokeAsync(context, access);
            Assert.Equal(valid, called);
            if (!valid) Assert.Equal(403, context.Response.StatusCode);
        }
    }
    [Fact]
    public async Task HubDoesNotAllowUnauthenticatedTransport()
    {
        var context = Context(origin: Origin);
        context.Request.Path = "/hubs/verification";
        await new LocalSecurityMiddleware(_ => throw new Exception("Should not run"), [Origin]).InvokeAsync(context, new LocalAccess());
        Assert.Equal(401, context.Response.StatusCode);
    }
    private static DefaultHttpContext Context(string host = "127.0.0.1", string origin = "")
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider()
        };
        context.Request.Host = new HostString(host, 5078);
        context.Request.Path = "/api/v1/projects";
        context.Request.Method = "GET";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        if (origin.Length > 0) context.Request.Headers.Origin = origin;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
