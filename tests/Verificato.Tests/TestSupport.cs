using System.Collections.Concurrent;
using Verificato.Core;
using Verificato.Infrastructure;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Verificato.Tests;

public sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "verificato-tests", Guid.NewGuid().ToString());
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(Path, true);
    }
    public async Task<SqliteVerificationStore> Store()
    {
        var store = new SqliteVerificationStore(System.IO.Path.Combine(Path, "data"));
        await store.InitializeAsync();
        return store;
    }
    public Project Project() => ProjectValidation.Validate(new("Fixture", Path, "http://127.0.0.1:5199"));
}
public sealed class CollectingSink : IObservationSink
{
    public ConcurrentQueue<Observation> Items { get; } = new();
    public bool Emit(Observation observation) { Items.Enqueue(observation); return true; }
    public long Dropped(Guid runId) => 0;
    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}
