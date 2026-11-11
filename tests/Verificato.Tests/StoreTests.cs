using Verificato.Core;

namespace Verificato.Tests;

public sealed class StoreTests
{
    [Fact]
    public async Task PersistsProjectAndKeepsRunConfigurationSnapshot()
    {
        using var directory = new TestDirectory();
        using (var db = await directory.Store())
        {
            var project = directory.Project();
            await db.SaveProjectAsync(project);
            var run = await db.CreateRunAsync(project, RunKind.Interactive);
            await db.SaveProjectAsync(project with { Name = "Changed" });
            Assert.Equal("Fixture", (await db.GetRunAsync(run.Id))!.Configuration.Name);
        }
        using var reopened = await directory.Store();
        Assert.Equal("Changed", Assert.Single(await reopened.GetProjectsAsync()).Name);
    }
    [Fact]
    public async Task GroupsIssuesButDoesNotDoubleCountReplayedEvents()
    {
        using var directory = new TestDirectory();
        using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.Interactive);
        var item = new Observation(run.Id, "js", Category.JavaScript, Severity.Error, "Error", "boom", Id: Guid.NewGuid());
        await db.AppendAsync(item); await db.AppendAsync(item);
        await db.AppendAsync(item with { Id = Guid.NewGuid() });
        var details = (await db.GetDetailsAsync(run.Id))!;
        Assert.Equal(2, details.Summary.EventCount);
        Assert.Equal(2, Assert.Single(details.Issues).OccurrenceCount);
        Assert.Equal(1, details.Summary.ErrorCount);
    }
    [Fact]
    public async Task SequencesConcurrentEventsAndPagesWithoutGaps()
    {
        using var directory = new TestDirectory();
        using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.Interactive);
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => db.AppendAsync(new(run.Id, "test", Category.Console, Severity.Info, "Event", i.ToString(), IsIssue: false))));
        var page = await db.GetEventsAsync(run.Id, 0, 20);
        Assert.True(page.HasMore); Assert.Equal(20, page.NextSequence);
        var next = await db.GetEventsAsync(run.Id, page.NextSequence, 20);
        Assert.False(next.HasMore); Assert.Equal(40, next.NextSequence);
        Assert.Equal(Enumerable.Range(1, 40).Select(i => (long)i), page.Items.Concat(next.Items).Select(x => x.Sequence));
    }
    [Fact]
    public async Task RecoversInterruptedRunsAndCascadesDeletion()
    {
        using var directory = new TestDirectory();
        using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.Interactive);
        await db.AppendAsync(new(run.Id, "error", Category.Http, Severity.Error, "HTTP 500", "failed"));
        await db.MarkInterruptedAsync();
        Assert.Equal(RunState.Interrupted, (await db.GetRunAsync(run.Id))!.State);
        await db.DeleteProjectAsync(project.Id);
        Assert.Null(await db.GetRunAsync(run.Id));
        Assert.Empty((await db.GetEventsAsync(run.Id)).Items);
    }
    [Fact]
    public async Task ComputesResponsePercentilesAndKeepsMissingMeasurementsMissing()
    {
        using var directory = new TestDirectory(); using var db = await directory.Store();
        var project = directory.Project(); await db.SaveProjectAsync(project);
        var run = await db.CreateRunAsync(project, RunKind.Interactive);
        foreach (var value in new double?[] { null, 10, 20, 30, 40, 100 })
            await db.AppendAsync(new(run.Id, "http.request", Category.Performance, Severity.Info, "HTTP", "GET", Value: value, Unit: "ms", IsIssue: false));
        var summary = (await db.GetDetailsAsync(run.Id))!.Summary;
        Assert.Equal(6, summary.RequestCount); Assert.Equal(5, summary.MeasuredRequestCount);
        Assert.Equal(30, summary.MedianResponseMs); Assert.Equal(100, summary.P95ResponseMs);
    }
}
