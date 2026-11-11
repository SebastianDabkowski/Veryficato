using System.Diagnostics;
using System.Text.Json.Serialization;
using Verificato.Analysis.CSharp;
using Verificato.Browser;
using Verificato.Core;
using Verificato.Host;
using Verificato.Infrastructure;

var bundledBrowsers = Path.Combine(AppContext.BaseDirectory, "browsers");
if (Directory.Exists(bundledBrowsers) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")))
    Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", bundledBrowsers);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
var port = builder.Configuration.GetValue("port", 5078);
if (port is < 1024 or > 65535) throw new ArgumentException("Port musi być w zakresie 1024–65535.");
var origin = $"http://127.0.0.1:{port}";
var development = builder.Configuration.GetValue("dev", false);
var allowedOrigins = development ? new[] { origin, "http://127.0.0.1:5180" } : [origin];
var uiOrigin = development ? "http://127.0.0.1:5180" : origin;
var dataDirectory = builder.Configuration["data-dir"] ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Verificato");
builder.WebHost.UseUrls(origin);
builder.WebHost.ConfigureKestrel(options => { options.AddServerHeader = false; options.Limits.MaxRequestBodySize = 64 * 1024; });
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));
builder.Services.AddSingleton<LocalAccess>();
builder.Services.AddSingleton(new SqliteVerificationStore(dataDirectory));
builder.Services.AddSingleton<IVerificationStore>(sp => sp.GetRequiredService<SqliteVerificationStore>());
builder.Services.AddSingleton<IArchitectureStore>(sp => sp.GetRequiredService<SqliteVerificationStore>());
builder.Services.AddSingleton<IArchitectureAnalyzer, ArchitectureAnalyzer>();
builder.Services.AddSingleton<IBrowserSessionRunner, PlaywrightSessionRunner>();
builder.Services.AddSingleton<ICodeAnalyzer, RoslynCodeAnalyzer>();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 4096)
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<ObservationPump>();
builder.Services.AddSingleton<IObservationSink>(sp => sp.GetRequiredService<ObservationPump>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ObservationPump>());
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RunCoordinator>());

Directory.CreateDirectory(dataDirectory);
using var instanceLock = new FileStream(Path.Combine(dataDirectory, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var app = builder.Build();
var store = app.Services.GetRequiredService<IVerificationStore>();
await store.InitializeAsync();
await store.MarkInterruptedAsync();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var status = ex switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode,
            KeyNotFoundException => 404, ArgumentException => 400,
            InvalidOperationException => 409, _ => 500
        };
        var title = status == 500 ? "Błąd lokalnego serwera. Sprawdź miejsce na dysku i log terminala." : Privacy.Clean(ex.Message);
        if (status == 500) app.Logger.LogError("Błąd żądania {Path}: {Type}", context.Request.Path, ex.GetType().Name);
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
});
app.UseMiddleware<LocalSecurityMiddleware>([allowedOrigins]);

app.MapPost("/api/v1/auth/bootstrap", (BootstrapInput input, HttpContext context, LocalAccess access) =>
{
    if (!allowedOrigins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.OrdinalIgnoreCase))
        return Results.Problem(statusCode: 403, title: "Niedozwolony origin bootstrap.");
    var csrf = input.Token is { Length: <= 128 } ? access.Exchange(input.Token, context.Response) : null;
    return csrf is null ? Results.Problem(statusCode: 401, title: "Link uruchomienia wygasł lub został już użyty. Uruchom host ponownie.") : Results.Ok(new { csrf });
});
app.MapGet("/api/v1/auth/session", (HttpContext context, LocalAccess access) => Results.Ok(new { csrf = access.Authenticate(context.Request) }));
app.MapPost("/api/v1/auth/logout", (HttpContext context, LocalAccess access) => { access.Logout(context.Request, context.Response); return Results.NoContent(); });
var api = app.MapGroup("/api/v1");
api.MapGet("/projects", async (IVerificationStore db) => Results.Ok(await db.GetProjectsAsync()));
api.MapPost("/projects", async (ProjectInput input, IVerificationStore db) =>
{
    var project = ProjectValidation.Validate(input);
    await db.SaveProjectAsync(project);
    return Results.Created($"/api/v1/projects/{project.Id}", project);
});
api.MapGet("/projects/{id:guid}", async (Guid id, IVerificationStore db) =>
    await db.GetProjectAsync(id) is { } project ? Results.Ok(project) : Results.NotFound());
api.MapPatch("/projects/{id:guid}", async (Guid id, ProjectInput input, RunCoordinator coordinator) =>
    Results.Ok(await coordinator.UpdateProjectAsync(id, input)));
api.MapDelete("/projects/{id:guid}", async (Guid id, RunCoordinator coordinator) =>
{
    await coordinator.DeleteProjectAsync(id);
    return Results.NoContent();
});
api.MapPost("/projects/{id:guid}/browser-sessions", async (Guid id, HttpRequest request, RunCoordinator coordinator) =>
{
    var run = await coordinator.StartRunAsync(id, RunKind.Interactive, Idempotency(request));
    return Results.Accepted($"/api/v1/runs/{run.Id}", run);
});
api.MapPost("/projects/{id:guid}/analysis-runs", async (Guid id, HttpRequest request, RunCoordinator coordinator) =>
{
    var run = await coordinator.StartRunAsync(id, RunKind.CodeAnalysis, Idempotency(request));
    return Results.Accepted($"/api/v1/runs/{run.Id}", run);
});
api.MapGet("/projects/{id:guid}/runs", async (Guid id, int? limit, IVerificationStore db) => Results.Ok(await db.GetRunsAsync(id, limit ?? 50)));
api.MapGet("/projects/{id:guid}/dashboard", async (Guid id, IVerificationStore db) =>
{
    var runs = await db.GetRunsAsync(id);
    var lastBrowser = runs.FirstOrDefault(x => x.Kind == RunKind.Interactive);
    var lastAnalysis = runs.FirstOrDefault(x => x.Kind == RunKind.CodeAnalysis);
    return Results.Ok(new
    {
        interactive = lastBrowser is null ? null : await db.GetDetailsAsync(lastBrowser.Id),
        analysis = lastAnalysis is null ? null : await db.GetDetailsAsync(lastAnalysis.Id)
    });
});
api.MapGet("/runs/{id:guid}", async (Guid id, IVerificationStore db) =>
    await db.GetDetailsAsync(id) is { } details ? Results.Ok(details) : Results.NotFound());
api.MapPost("/runs/{id:guid}/stop", async (Guid id, RunCoordinator coordinator, IVerificationStore db) =>
{
    if (await db.GetRunAsync(id) is null) return Results.NotFound();
    await coordinator.StopRunAsync(id);
    return Results.Accepted($"/api/v1/runs/{id}");
});
api.MapGet("/runs/{id:guid}/events", async (Guid id, long? afterSequence, int? limit, IVerificationStore db) =>
{
    if (await db.GetRunAsync(id) is null) return Results.NotFound();
    return Results.Ok(await db.GetEventsAsync(id, Math.Max(afterSequence ?? 0, 0), limit ?? 200));
});
api.MapGet("/runs/{id:guid}/issues", async (Guid id, IVerificationStore db) =>
    await db.GetDetailsAsync(id) is { } details ? Results.Ok(details.Issues) : Results.NotFound());
api.MapGet("/runs/{id:guid}/metrics", async (Guid id, IVerificationStore db) =>
    await db.GetDetailsAsync(id) is { } details ? Results.Ok(new { details.Metrics, details.Summary }) : Results.NotFound());
api.MapDelete("/runs/{id:guid}", async (Guid id, RunCoordinator coordinator) =>
{
    await coordinator.DeleteRunAsync(id);
    return Results.NoContent();
});
api.MapGet("/projects/{id:guid}/architecture", async (Guid id, Guid? snapshotId, IArchitectureStore db, CancellationToken ct) =>
    await db.GetArchitectureAsync(id, snapshotId, ct) is { } document ? Results.Ok(document) : Results.NotFound());
api.MapGet("/projects/{id:guid}/architecture/{snapshotId:guid}/files/{fileId}",
    async (Guid id, Guid snapshotId, string fileId, int? line, IVerificationStore projects, IArchitectureStore snapshots, IArchitectureAnalyzer analyzer, CancellationToken ct) =>
{
    var project = await projects.GetProjectAsync(id, ct) ?? throw new KeyNotFoundException("Projekt nie istnieje.");
    var snapshot = await snapshots.GetArchitectureAsync(id, snapshotId, ct) ?? throw new KeyNotFoundException("Migawka architektury nie istnieje.");
    return Results.Ok(await analyzer.ReadSourceAsync(project, snapshot, fileId, line ?? 1, ct));
});
api.MapGet("/system/status", (IBrowserSessionRunner browser) => Results.Ok(new SystemStatus("0.1.0", browser.IsInstalled,
    Path.GetFullPath(dataDirectory), "Roslyn — ograniczony tryb tylko do odczytu, bez MSBuild/restore", 60, ObservationPump.MaxObservations)));
app.MapHub<VerificationHub>("/hubs/verification");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
    { context.Response.StatusCode = 404; return; }
    var index = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "index.html");
    if (File.Exists(index)) { context.Response.ContentType = "text/html; charset=utf-8"; await context.Response.SendFileAsync(index); }
    else await context.Response.WriteAsync("Verificato: zbuduj UI (npm --prefix src/Verificato.UI run build) i przebuduj host lub uruchom scripts/dev.ps1.");
});

var access = app.Services.GetRequiredService<LocalAccess>();
var launchUrl = $"{uiOrigin}/#bootstrap={access.BootstrapToken}";
app.Lifetime.ApplicationStarted.Register(() =>
{
    app.Logger.LogInformation("Verificato działa lokalnie: {Origin}. Dane: {DataDirectory}", uiOrigin, Path.GetFullPath(dataDirectory));
    if (builder.Configuration.GetValue("print-bootstrap", false))
        Console.WriteLine($"Jednorazowy sekret — nie udostępniaj tego linku: {launchUrl}");
    if (!builder.Configuration.GetValue("no-open", false))
    {
        try { Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true }); }
        catch { app.Logger.LogWarning("Nie można otworzyć UI. Uruchom ponownie z --print-bootstrap true i otwórz jednorazowy link ręcznie."); }
    }
});
await app.RunAsync();

static string? Idempotency(HttpRequest request)
{
    var key = request.Headers["Idempotency-Key"].FirstOrDefault();
    if (key is { Length: > 128 }) throw new ArgumentException("Zbyt długi klucz idempotencji.");
    return key;
}
public sealed record BootstrapInput(string? Token);
public partial class Program;
