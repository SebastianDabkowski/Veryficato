using Microsoft.AspNetCore.SignalR;
using Verificato.Core;

namespace Verificato.Host;

public sealed class RunCoordinator(IVerificationStore store, IBrowserSessionRunner browser, ICodeAnalyzer analyzer,
    ObservationPump sink, IHubContext<VerificationHub> hub, ILogger<RunCoordinator> logger,
    IArchitectureAnalyzer? architecture = null, IArchitectureStore? architectureStore = null) : IHostedService
{
    private sealed class Active(Run run)
    {
        public Run Run = run;
        public CancellationTokenSource Cancellation = new(TimeSpan.FromMinutes(run.Kind == RunKind.CodeAnalysis ? 10 : 60));
        public Task Work = Task.CompletedTask;
        public string StopReason = "Browser closed";
    }
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<RunKind, Active> active = [];
    private readonly Dictionary<string, (Guid ProjectId, RunKind Kind, Guid RunId)> idempotency = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sink.LimitReached += LimitReached;
        sink.StorageFailed += StorageFailed;
        return Task.CompletedTask;
    }
    public async Task<Run> StartRunAsync(Guid projectId, RunKind kind, string? key)
    {
        await gate.WaitAsync();
        try
        {
            if (key is not null && idempotency.TryGetValue(key, out var prior))
            {
                if (prior.ProjectId != projectId || prior.Kind != kind) throw new InvalidOperationException("Klucz idempotencji użyty dla innego polecenia.");
                return await store.GetRunAsync(prior.RunId) ?? throw new InvalidOperationException("Poprzednie uruchomienie usunięto.");
            }
            var project = await store.GetProjectAsync(projectId) ?? throw new KeyNotFoundException("Projekt nie istnieje.");
            ProjectValidation.Validate(new(project.Name, project.SourcePath, project.ApplicationUrl, project.AllowedOrigins), project);
            if (active.ContainsKey(kind)) throw new InvalidOperationException("Uruchomienie tego typu już trwa. Najpierw je zakończ.");
            if (kind == RunKind.Interactive && !browser.IsInstalled)
                throw new ArgumentException("Brak Chromium. Uruchom scripts/install-browser.ps1 lub scripts/install-browser.sh.");
            var run = await store.CreateRunAsync(project, kind);
            var entry = new Active(run);
            active[kind] = entry;
            if (key is not null)
            {
                if (idempotency.Count >= 1000) idempotency.Remove(idempotency.Keys.First());
                idempotency[key] = (projectId, kind, run.Id);
            }
            entry.Work = Task.Run(() => ExecuteAsync(entry));
            return run;
        }
        finally { gate.Release(); }
    }
    public async Task StopRunAsync(Guid id, string reason = "User stopped")
    {
        await gate.WaitAsync();
        try
        {
            var entry = active.Values.FirstOrDefault(x => x.Run.Id == id);
            if (entry is null || !entry.Run.IsActive) return;
            entry.StopReason = reason;
            entry.Cancellation.Cancel();
            if (entry.Run.State is not RunState.Stopping)
            {
                entry.Run = entry.Run with { State = RunState.Stopping };
                await store.SaveRunAsync(entry.Run);
                await Publish(entry.Run);
            }
        }
        finally { gate.Release(); }
    }
    public async Task<Project> UpdateProjectAsync(Guid id, ProjectInput input)
    {
        await gate.WaitAsync();
        try
        {
            if (active.Values.Any(x => x.Run.ProjectId == id)) throw new InvalidOperationException("Zatrzymaj aktywne wykonania przed edycją.");
            var project = await store.GetProjectAsync(id) ?? throw new KeyNotFoundException("Projekt nie istnieje.");
            var updated = ProjectValidation.Validate(input, project);
            await store.SaveProjectAsync(updated);
            return updated;
        }
        finally { gate.Release(); }
    }
    public async Task DeleteProjectAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            if (active.Values.Any(x => x.Run.ProjectId == id)) throw new InvalidOperationException("Zatrzymaj aktywne wykonania przed usunięciem.");
            await store.DeleteProjectAsync(id);
        }
        finally { gate.Release(); }
    }
    public async Task DeleteRunAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            if ((await store.GetRunAsync(id))?.IsActive == true) throw new InvalidOperationException("Nie można usunąć aktywnego wykonania.");
            await store.DeleteRunAsync(id);
        }
        finally { gate.Release(); }
    }
    private void LimitReached(Guid id) => _ = StopRunAsync(id, "Observation/data limit reached");
    private void StorageFailed(Guid id) => _ = StopRunAsync(id, "Storage failure");

    private async Task ExecuteAsync(Active entry)
    {
        var completeness = Completeness.CompleteWithinScope;
        string? limitation = null;
        string? failure = null;
        try
        {
            if (entry.Run.Kind == RunKind.Interactive)
                await browser.RunAsync(entry.Run, sink, () => SetRunningAsync(entry), entry.Cancellation.Token);
            else
            {
                await SetRunningAsync(entry);
                var result = await analyzer.AnalyzeAsync(entry.Run, sink, entry.Cancellation.Token);
                completeness = result.Completeness;
                limitation = result.Limitation;
                if (architecture is not null && architectureStore is not null)
                {
                    sink.Emit(new(entry.Run.Id, "architecture.started", Category.System, Severity.Info,
                        "Budowanie architektury C4 · C3", "Inwentaryzacja projektów, typów i referencji. Bez wykonywania MSBuild.", IsIssue: false));
                    var document = await architecture.AnalyzeAsync(entry.Run.Configuration, entry.Run.Id, entry.Cancellation.Token);
                    await architectureStore.SaveArchitectureAsync(document, entry.Cancellation.Token);
                    sink.Emit(new(entry.Run.Id, "architecture.completed", Category.System, Severity.Info,
                        "Diagram architektury gotowy", $"Komponenty: {document.Components.Count}; relacje: {document.Relationships.Count}. Otwórz zakładkę Architektura C4.", IsIssue: false));
                }
                entry.StopReason = "Analysis completed";
            }
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
        {
            if (entry.StopReason == "Browser closed") entry.StopReason = "Time limit reached";
            if (entry.Run.Kind == RunKind.CodeAnalysis || entry.StopReason.Contains("limit", StringComparison.OrdinalIgnoreCase)) completeness = Completeness.Partial;
        }
        catch (Exception ex)
        {
            failure = Privacy.Clean(ex.Message, 1500);
            completeness = Completeness.Partial;
            logger.LogWarning("Uruchomienie {RunId} nie powiodło się: {ExceptionType}", entry.Run.Id, ex.GetType().Name);
        }
        try
        {
            using var flushTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await sink.FlushAsync(flushTimeout.Token);
            if (sink.Failure(entry.Run.Id) is not null) failure = "Nie można zapisać wszystkich wyników. Sprawdź wolne miejsce i uprawnienia katalogu danych.";
            var lost = sink.Dropped(entry.Run.Id);
            if (lost > 0)
            {
                completeness = Completeness.Partial;
                await store.AppendAsync(new(entry.Run.Id, "system.dropped", Category.System, Severity.Warning,
                    "Niepełne dane sesji", $"Pominięte obserwacje: {lost}.", Value: lost, Unit: "events", IsIssue: false));
            }
            await gate.WaitAsync();
            try
            {
                var details = await store.GetDetailsAsync(entry.Run.Id);
                var outcome = failure is not null || completeness != Completeness.CompleteWithinScope
                    ? RunOutcome.Inconclusive : details?.Summary.IssueCount > 0 ? RunOutcome.IssuesDetected : RunOutcome.NoIssuesDetected;
                entry.Run = entry.Run with
                {
                    State = failure is null ? RunState.Completed : RunState.Failed,
                    Outcome = outcome, Completeness = completeness, EndedAt = DateTimeOffset.UtcNow,
                    Failure = failure, StopReason = entry.StopReason, Limitations = limitation
                };
                await store.SaveRunAsync(entry.Run);
                await Publish(entry.Run);
            }
            finally { gate.Release(); }
        }
        catch (Exception ex)
        {
            logger.LogError("Błąd finalizacji {RunId}: {ExceptionType}. Po restarcie uruchomienie otrzyma status Interrupted.", entry.Run.Id, ex.GetType().Name);
        }
        finally
        {
            await gate.WaitAsync();
            try { active.Remove(entry.Run.Kind); }
            finally { gate.Release(); }
            entry.Cancellation.Dispose();
            sink.Forget(entry.Run.Id);
        }
    }
    private async Task SetRunningAsync(Active entry)
    {
        await gate.WaitAsync();
        try
        {
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            entry.Run = entry.Run with { State = RunState.Running };
            await store.SaveRunAsync(entry.Run);
            await Publish(entry.Run);
        }
        finally { gate.Release(); }
    }
    private Task Publish(Run run) => hub.Clients.Group(run.Id.ToString()).SendAsync("RunStateChanged", run);
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        sink.LimitReached -= LimitReached;
        sink.StorageFailed -= StorageFailed;
        Active[] pending;
        await gate.WaitAsync(cancellationToken);
        try { pending = active.Values.ToArray(); }
        finally { gate.Release(); }
        foreach (var item in pending) await StopRunAsync(item.Run.Id, "Host stopped");
        await Task.WhenAll(pending.Select(x => x.Work)).WaitAsync(cancellationToken);
    }
}
