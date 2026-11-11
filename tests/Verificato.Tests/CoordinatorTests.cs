using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Verificato.Core;
using Verificato.Host;

namespace Verificato.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task StartIsIdempotentAndConcurrentSessionIsRejected()
    {
        using var directory = new TestDirectory(); using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        using var services = new ServiceCollection().AddLogging().AddSignalR().Services.BuildServiceProvider();
        var hub = services.GetRequiredService<IHubContext<VerificationHub>>();
        using var pump = new ObservationPump(db, hub, NullLogger<ObservationPump>.Instance);
        var coordinator = new RunCoordinator(db, new WaitingBrowser(), new PartialAnalyzer(), pump, hub, NullLogger<RunCoordinator>.Instance);
        await pump.StartAsync(CancellationToken.None); await coordinator.StartAsync(CancellationToken.None);
        try
        {
            var run = await coordinator.StartRunAsync(project.Id, RunKind.Interactive, "same-command");
            var replay = await coordinator.StartRunAsync(project.Id, RunKind.Interactive, "same-command");
            Assert.Equal(run.Id, replay.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartRunAsync(project.Id, RunKind.Interactive, "another-command"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DeleteProjectAsync(project.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DeleteRunAsync(run.Id));
            await coordinator.StopRunAsync(run.Id);
            var saved = await WaitForTerminal(db, run.Id);
            Assert.Equal(RunState.Completed, saved.State);
            await coordinator.StopRunAsync(run.Id);
            Assert.Equal(RunState.Completed, (await db.GetRunAsync(run.Id))!.State);
        }
        finally { await coordinator.StopAsync(CancellationToken.None); await pump.StopAsync(CancellationToken.None); }
    }
    [Fact]
    public async Task PartialAnalysisNeverLooksLikeFullSuccess()
    {
        using var directory = new TestDirectory(); using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        using var services = new ServiceCollection().AddLogging().AddSignalR().Services.BuildServiceProvider();
        var hub = services.GetRequiredService<IHubContext<VerificationHub>>();
        using var pump = new ObservationPump(db, hub, NullLogger<ObservationPump>.Instance);
        var coordinator = new RunCoordinator(db, new WaitingBrowser(), new PartialAnalyzer(), pump, hub, NullLogger<RunCoordinator>.Instance);
        await pump.StartAsync(CancellationToken.None); await coordinator.StartAsync(CancellationToken.None);
        try
        {
            var run = await coordinator.StartRunAsync(project.Id, RunKind.CodeAnalysis, null);
            var saved = await WaitForTerminal(db, run.Id);
            Assert.Equal(Completeness.Partial, saved.Completeness);
            Assert.Equal(RunOutcome.Inconclusive, saved.Outcome);
            Assert.Null(saved.Failure); Assert.Equal("unsupported fixture", saved.Limitations);
        }
        finally { await coordinator.StopAsync(CancellationToken.None); await pump.StopAsync(CancellationToken.None); }
    }
    [Fact]
    public void QueueIsBoundedAndReservesRoomForIssues()
    {
        using var directory = new TestDirectory();
        using var db = new Verificato.Infrastructure.SqliteVerificationStore(directory.Path);
        using var services = new ServiceCollection().AddLogging().AddSignalR().Services.BuildServiceProvider();
        using var pump = new ObservationPump(db, services.GetRequiredService<IHubContext<VerificationHub>>(), NullLogger<ObservationPump>.Instance);
        var id = Guid.NewGuid();
        for (var i = 0; i < 3500; i++) pump.Emit(new(id, "info", Category.System, Severity.Info, "info", "event", IsIssue: false));
        Assert.True(pump.Dropped(id) >= 500);
        Assert.True(pump.Emit(new(id, "error", Category.JavaScript, Severity.Error, "important", "error")));
    }
    private static async Task<Run> WaitForTerminal(IVerificationStore db, Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var run = (await db.GetRunAsync(id, timeout.Token))!;
            if (!run.IsActive) return run;
            await Task.Delay(20, timeout.Token);
        }
    }
    private sealed class WaitingBrowser : IBrowserSessionRunner
    {
        public bool IsInstalled => true;
        public async Task RunAsync(Run run, IObservationSink sink, Func<Task> onReady, CancellationToken ct)
        { await onReady(); await Task.Delay(Timeout.Infinite, ct); }
    }
    private sealed class PartialAnalyzer : ICodeAnalyzer
    {
        public string Id => "fixture";
        public Task<AnalysisResult> AnalyzeAsync(Run run, IObservationSink sink, CancellationToken ct) =>
            Task.FromResult(new AnalysisResult(Completeness.Partial, "unsupported fixture"));
    }
}
