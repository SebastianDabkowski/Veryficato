using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Verificato.Browser;
using Verificato.Core;

namespace Verificato.Tests;

public sealed class BrowserTests
{
    [Fact, Trait("Category", "Browser")]
    public async Task CapturesJavascriptConsoleHttpNetworkAndMetricsInRealChromium()
    {
        var previous = Environment.GetEnvironmentVariable("VERIFICATO_HEADLESS");
        Environment.SetEnvironmentVariable("VERIFICATO_HEADLESS", "1");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var fixture = builder.Build();
        fixture.MapGet("/", () => Results.Content("""
            <!doctype html><html><head><title>Browser fixture</title></head><body><h1>Verificato fixture</h1>
            <script>
              console.warn('fixture warning'); console.error('fixture console error');
              setTimeout(() => { throw new Error('fixture javascript error'); }, 150);
              Promise.allSettled([
                fetch('/missing').then(r => r.text()), fetch('/broken').then(r => r.text()),
                fetch('/slow').then(r => r.text()), fetch('http://127.0.0.1:1/failure')
              ]).then(() => setTimeout(() => location.href='http://not-allowed.invalid/', 500));
            </script></body></html>
            """, "text/html"));
        fixture.MapGet("/missing", () => Results.NotFound());
        fixture.MapGet("/broken", () => Results.StatusCode(500));
        fixture.MapGet("/slow", async () => { await Task.Delay(1200); return Results.Text("slow response"); });
        fixture.MapGet("/disconnect", async (HttpContext context) =>
        {
            context.Response.ContentLength = 10_000;
            await context.Response.WriteAsync("partial body");
            await context.Response.Body.FlushAsync();
            context.Abort();
        });
        await fixture.StartAsync();
        try
        {
            var url = fixture.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var directory = new TestDirectory();
            var project = directory.Project() with { ApplicationUrl = url, AllowedOrigins = [url, "http://127.0.0.1:1"] };
            var run = new Run(Guid.NewGuid(), project.Id, RunKind.Interactive, RunState.Starting,
                RunOutcome.Inconclusive, Completeness.CompleteWithinScope, DateTimeOffset.UtcNow, null, project);
            var sink = new CollectingSink();
            var runner = new PlaywrightSessionRunner();
            Assert.True(runner.IsInstalled, "Install Chromium first with scripts/install-browser.ps1");
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = runner.RunAsync(run, sink, () => { ready.SetResult(); return Task.CompletedTask; }, stop.Token);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline && !sink.Items.Any(x => x.RuleId == "scope.navigation")) await Task.Delay(100);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Contains(sink.Items, x => x.RuleId == "javascript.unhandled" && x.Message.Contains("fixture javascript error"));
            Assert.Contains(sink.Items, x => x.RuleId == "console.warning");
            Assert.Contains(sink.Items, x => x.RuleId == "console.error");
            Assert.Contains(sink.Items, x => x.Title == "HTTP 404");
            Assert.Contains(sink.Items, x => x.Title == "HTTP 500");
            Assert.Contains(sink.Items, x => x.RuleId == "resource.failed");
            Assert.True(sink.Items.Any(x => x.RuleId == "performance.slow-request"),
                string.Join("\n", sink.Items.Select(x => $"{x.RuleId} {x.Url} {x.Value} {x.Message}")));
            Assert.Contains(sink.Items, x => x.RuleId == "http.request" && x.Value >= 1000);
            Assert.Contains(sink.Items, x => x.RuleId == "performance.dcl");
            Assert.Contains(sink.Items, x => x.RuleId == "scope.navigation");
            Assert.DoesNotContain(sink.Items, x => x.Url?.Contains("not-allowed.invalid") == true);
        }
        finally { await fixture.StopAsync(); Environment.SetEnvironmentVariable("VERIFICATO_HEADLESS", previous); }
    }
}
