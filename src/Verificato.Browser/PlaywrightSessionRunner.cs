using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Verificato.Core;

namespace Verificato.Browser;

public sealed class PlaywrightSessionRunner : IBrowserSessionRunner
{
    public bool IsInstalled
    {
        get
        {
            using var playwright = Playwright.CreateAsync().GetAwaiter().GetResult();
            return File.Exists(playwright.Chromium.ExecutablePath);
        }
    }

    public async Task RunAsync(Run run, IObservationSink sink, Func<Task> onReady, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = Environment.GetEnvironmentVariable("VERIFICATO_HEADLESS") == "1",
            Timeout = 15_000
        });
        ct.ThrowIfCancellationRequested();
        var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block, AcceptDownloads = false,
            ViewportSize = null, IgnoreHTTPSErrors = false
        });
        var scope = new OriginScope(run.Configuration.AllowedOrigins);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageCount = 0;
        var pages = new ConcurrentDictionary<IPage, byte>();
        var operations = new ConcurrentDictionary<IPage, Task>();
        var metricScript = """
            (() => {
              window.__verificato = {cls: 0};
              for (const type of ['paint','largest-contentful-paint','layout-shift']) {
                try { new PerformanceObserver(list => {
                  for (const e of list.getEntries()) {
                    if (e.entryType === 'paint' && e.name === 'first-contentful-paint') window.__verificato.fcp = e.startTime;
                    if (e.entryType === 'largest-contentful-paint') window.__verificato.lcp = e.startTime;
                    if (e.entryType === 'layout-shift' && !e.hadRecentInput) window.__verificato.cls = (window.__verificato.cls || 0) + e.value;
                  }
                }).observe({type, buffered:true}); } catch {}
              }
            })();
            """;
        await context.AddInitScriptAsync(metricScript);
        // Navigation is restricted; external subresources may load, but their details are never persisted.
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            if (request.IsNavigationRequest && !scope.Contains(request.Url) && request.Url != "about:blank")
            {
                sink.Emit(new(run.Id, "scope.navigation", Category.System, Severity.Warning,
                    "Nawigacja poza zakresem zablokowana", "Dodaj origin w ustawieniach projektu, aby go testować.", IsIssue: false));
                await route.AbortAsync();
            }
            else await route.ContinueAsync();
        });

        context.Page += (_, page) => Attach(page);
        browser.Disconnected += (_, _) => ended.TrySetResult();
        context.Response += (_, response) =>
        {
            if (!scope.Contains(response.Url) || response.Status < 400) return;
            sink.Emit(new(run.Id, "http.status", Category.Http, Severity.Error,
                $"HTTP {response.Status}", $"{response.Request.Method} → {response.Status} {response.StatusText}", response.Url));
        };
        context.RequestFailed += (_, request) =>
        {
            if (!scope.Contains(request.Url)) return;
            var failure = request.Failure ?? "Network failure";
            var cancelled = failure.Contains("ABORTED", StringComparison.OrdinalIgnoreCase);
            sink.Emit(new(run.Id, cancelled ? "resource.cancelled" : "resource.failed", Category.Resource,
                cancelled ? Severity.Info : Severity.Error, cancelled ? "Żądanie anulowane" : "Błąd ładowania zasobu",
                failure, request.Url, IsIssue: !cancelled));
        };
        context.RequestFinished += (_, request) =>
        {
            if (!scope.Contains(request.Url)) return;
            var timing = request.Timing;
            var duration = timing.ResponseEnd >= 0 ? timing.ResponseEnd : (double?)null;
            sink.Emit(new(run.Id, "http.request", Category.Performance, Severity.Info,
                "Zakończone żądanie", $"{request.Method} · {request.ResourceType}", request.Url,
                Value: duration, Unit: "ms", IsIssue: false));
            if (duration > 1000)
                sink.Emit(new(run.Id, "performance.slow-request", Category.Performance, Severity.Warning,
                    "Wolna odpowiedź HTTP", "Czas żądania przekroczył próg 1000 ms.", request.Url));
        };

        void Attach(IPage page)
        {
            if (!pages.TryAdd(page, 0)) return;
            Interlocked.Increment(ref pageCount);
            page.Close += (_, _) =>
            {
                pages.TryRemove(page, out _);
                operations.TryRemove(page, out _);
                if (Interlocked.Decrement(ref pageCount) == 0) ended.TrySetResult();
            };
            page.PageError += (_, error) =>
            {
                if (scope.Contains(page.Url))
                    sink.Emit(new(run.Id, "javascript.unhandled", Category.JavaScript, Severity.Error,
                        "Nieobsłużony wyjątek JavaScript", error, page.Url, Stack: error));
            };
            page.Console += (_, message) =>
            {
                if (!scope.Contains(page.Url) || message.Type is not ("error" or "warning")) return;
                var location = Regex.Match(message.Location, @"^(.*):(\d+):(\d+)$");
                var sourceUrl = location.Success && !string.IsNullOrEmpty(location.Groups[1].Value) ? location.Groups[1].Value : page.Url;
                if (!scope.Contains(sourceUrl)) return;
                // Chromium duplicates HTTP/network failures in the console; retain evidence but do not count a second issue.
                var networkEcho = message.Text.StartsWith("Failed to load resource:", StringComparison.OrdinalIgnoreCase);
                sink.Emit(new(run.Id, $"console.{message.Type}", Category.Console,
                    message.Type == "error" ? Severity.Error : Severity.Warning,
                    message.Type == "error" ? "Błąd konsoli" : "Ostrzeżenie konsoli", message.Text, sourceUrl,
                    Line: location.Success ? int.Parse(location.Groups[2].Value) + 1 : null,
                    Column: location.Success ? int.Parse(location.Groups[3].Value) + 1 : null, IsIssue: !networkEcho));
            };
            page.Crash += (_, _) =>
            {
                sink.Emit(new(run.Id, "browser.crash", Category.System, Severity.Error,
                    "Awaria karty Chromium", "Karta przeglądarki uległa awarii."));
                ended.TrySetException(new InvalidOperationException("Awaria karty Chromium."));
            };
            page.Load += (_, _) => operations[page] = CaptureMetricsAsync(page);
        }

        async Task CaptureMetricsAsync(IPage page)
        {
            if (!scope.Contains(page.Url)) return;
            try
            {
                var values = await page.EvaluateAsync<Dictionary<string, double>>("""
                    () => {
                      const n = performance.getEntriesByType('navigation')[0];
                      const m = window.__verificato || {};
                      const out = {};
                      if (n && n.domContentLoadedEventEnd > 0) out.dcl = n.domContentLoadedEventEnd;
                      if (n && n.loadEventEnd > 0) out.load = n.loadEventEnd;
                      if (m.fcp != null) out.fcp = m.fcp;
                      if (m.lcp != null) out.lcp = m.lcp;
                      if (m.cls != null) out.cls = m.cls;
                      return out;
                    }
                    """).WaitAsync(TimeSpan.FromSeconds(2));
                foreach (var (name, value) in values)
                    sink.Emit(new(run.Id, $"performance.{name}", Category.Performance, Severity.Info,
                        name.ToUpperInvariant(), "Pomiar dokumentu w sesji laboratoryjnej.", page.Url,
                        Value: value, Unit: name == "cls" ? "score" : "ms", IsIssue: false));
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException) { /* Navigation/close can destroy the execution context. */ }
        }

        var first = await context.NewPageAsync();
        Attach(first);
        try
        {
            await first.GotoAsync(run.Configuration.ApplicationUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 }).WaitAsync(ct);
            await onReady();
            using var registration = ct.Register(() => ended.TrySetCanceled(ct));
            await ended.Task;
        }
        finally
        {
            foreach (var page in pages.Keys) await CaptureMetricsAsync(page);
            await Task.WhenAll(operations.Values.ToArray());
            await context.CloseAsync();
        }
    }
}
