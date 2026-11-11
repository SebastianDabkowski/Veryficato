using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Verificato.Core;

namespace Verificato.Host;

public sealed class VerificationHub : Hub
{
    public Task Subscribe(Guid runId) => Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString());
    public Task Unsubscribe(Guid runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString());
}

public sealed class ObservationPump(IVerificationStore store, IHubContext<VerificationHub> hub,
    ILogger<ObservationPump> logger) : BackgroundService, IObservationSink
{
    public const int MaxObservations = 100_000;
    public const long MaxDataBytes = 100_000_000;
    private sealed record Entry(Observation? Item = null, TaskCompletionSource? Barrier = null);
    private readonly Channel<Entry> channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(4096)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<Guid, long> dropped = new();
    private readonly ConcurrentDictionary<Guid, long> counts = new();
    private readonly ConcurrentDictionary<Guid, Exception> failures = new();
    private readonly ConcurrentDictionary<Guid, long> bytes = new();
    private readonly ConcurrentDictionary<Guid, byte> notified = new();
    public event Action<Guid>? LimitReached;
    public event Action<Guid>? StorageFailed;

    public bool Emit(Observation observation)
    {
        observation = Privacy.Sanitize(observation);
        var count = counts.AddOrUpdate(observation.RunId, 1, (_, n) => n + 1);
        var size = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(observation, JsonDefaults.Options));
        var totalBytes = bytes.AddOrUpdate(observation.RunId, size, (_, n) => n + size);
        if (count > MaxObservations || totalBytes > MaxDataBytes)
        {
            dropped.AddOrUpdate(observation.RunId, 1, (_, n) => n + 1);
            if (notified.TryAdd(observation.RunId, 0)) LimitReached?.Invoke(observation.RunId);
            return false;
        }
        // Reserve queue space for errors and warnings when informational traffic becomes noisy.
        if ((observation.Severity == Severity.Info && channel.Reader.Count >= 3000) ||
            !channel.Writer.TryWrite(new(observation)))
        {
            dropped.AddOrUpdate(observation.RunId, 1, (_, n) => n + 1);
            return false;
        }
        return true;
    }
    public long Dropped(Guid runId) => dropped.GetValueOrDefault(runId);
    public Exception? Failure(Guid runId) => failures.GetValueOrDefault(runId);
    public void Forget(Guid runId)
    {
        counts.TryRemove(runId, out _); dropped.TryRemove(runId, out _); failures.TryRemove(runId, out _);
        bytes.TryRemove(runId, out _); notified.TryRemove(runId, out _);
    }
    public async Task FlushAsync(CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await channel.Writer.WriteAsync(new(Barrier: completion), ct);
        await completion.Task.WaitAsync(ct);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(stoppingToken))
            {
                var changed = new Dictionary<Guid, long>();
                for (var i = 0; i < 100 && channel.Reader.TryRead(out var entry); i++)
                {
                    if (entry.Barrier is not null) { entry.Barrier.TrySetResult(); continue; }
                    var item = entry.Item!;
                    try
                    {
                        var saved = await store.AppendAsync(item, stoppingToken);
                        changed[item.RunId] = saved.Sequence;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures[item.RunId] = ex;
                        StorageFailed?.Invoke(item.RunId);
                        logger.LogError("Nie można zapisać obserwacji uruchomienia {RunId}. Typ błędu: {Type}", item.RunId, ex.GetType().Name);
                    }
                }
                foreach (var (id, sequence) in changed)
                    await hub.Clients.Group(id.ToString()).SendAsync("ObservationsAvailable", new { runId = id, sequence }, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out var entry)) entry.Barrier?.TrySetCanceled();
        }
    }
}
