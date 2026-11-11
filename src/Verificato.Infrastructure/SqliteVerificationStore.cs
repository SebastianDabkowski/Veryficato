using System.Text.Json;
using Microsoft.Data.Sqlite;
using Verificato.Core;

namespace Verificato.Infrastructure;

/// <summary>Single-writer local store. Schema changes are explicitly versioned with PRAGMA user_version.</summary>
public sealed class SqliteVerificationStore(string dataDirectory) : IVerificationStore, IArchitectureStore, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string DataDirectory { get; } = Path.GetFullPath(dataDirectory);
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(DataDirectory, "verificato.db"), ForeignKeys = true,
        DefaultTimeout = 10, Pooling = true
    }.ToString();

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataDirectory);
        await UseAsync(db =>
        {
            var version = Convert.ToInt32(Command(db, "PRAGMA user_version").ExecuteScalar());
            if (version > 2) throw new InvalidOperationException("Baza pochodzi z nowszej wersji Verificato.");
            if (version == 1)
            {
                // Online backup includes the WAL; never copy a live database file by itself.
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(DataDirectory, $"verificato.pre-v2-{Guid.NewGuid():N}.db"), Pooling = false
                }.ToString());
                backup.Open();
                db.BackupDatabase(backup);
            }
            Command(db, "PRAGMA journal_mode=WAL;").ExecuteNonQuery();
            using var transaction = db.BeginTransaction();
            Command(db, """
                CREATE TABLE IF NOT EXISTS Projects(Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS Runs(Id TEXT PRIMARY KEY, ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                    State TEXT NOT NULL, StartedAt TEXT NOT NULL, Json TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_Runs_Project ON Runs(ProjectId, StartedAt DESC);
                CREATE TABLE IF NOT EXISTS Observations(Id TEXT NOT NULL UNIQUE, RunId TEXT NOT NULL REFERENCES Runs(Id) ON DELETE CASCADE,
                    Sequence INTEGER NOT NULL, RuleId TEXT NOT NULL, Severity TEXT NOT NULL, Category TEXT NOT NULL,
                    Value REAL, IsIssue INTEGER NOT NULL, Json TEXT NOT NULL, PRIMARY KEY(RunId, Sequence));
                CREATE TABLE IF NOT EXISTS Issues(Id TEXT PRIMARY KEY, RunId TEXT NOT NULL REFERENCES Runs(Id) ON DELETE CASCADE,
                    Fingerprint TEXT NOT NULL, Severity TEXT NOT NULL, Json TEXT NOT NULL, UNIQUE(RunId, Fingerprint));
                CREATE INDEX IF NOT EXISTS IX_Observations_Rule ON Observations(RunId, RuleId);
                CREATE INDEX IF NOT EXISTS IX_Issues_Severity ON Issues(RunId, Severity);
                CREATE TABLE IF NOT EXISTS ArchitectureSnapshots(Id TEXT PRIMARY KEY,
                    ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                    RunId TEXT NOT NULL UNIQUE REFERENCES Runs(Id) ON DELETE CASCADE,
                    GeneratedAt TEXT NOT NULL, Json TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_Architecture_Project ON ArchitectureSnapshots(ProjectId, GeneratedAt DESC);
                PRAGMA user_version=2;
                """, transaction).ExecuteNonQuery();
            transaction.Commit();
            return true;
        }, ct);
    }

    public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default) =>
        UseAsync<IReadOnlyList<Project>>(db => ReadMany<Project>(Command(db, "SELECT Json FROM Projects ORDER BY json_extract(Json, '$.name') COLLATE NOCASE")), ct);
    public Task<Project?> GetProjectAsync(Guid id, CancellationToken ct = default) =>
        UseAsync(db => ReadOne<Project>(Command(db, "SELECT Json FROM Projects WHERE Id=$id", null, ("$id", id))), ct);
    public Task SaveProjectAsync(Project project, CancellationToken ct = default) =>
        ExecuteAsync("INSERT INTO Projects(Id,Json) VALUES($id,$json) ON CONFLICT(Id) DO UPDATE SET Json=$json", ct,
            ("$id", project.Id), ("$json", Serialize(project)));
    public Task DeleteProjectAsync(Guid id, CancellationToken ct = default) =>
        ExecuteAsync("DELETE FROM Projects WHERE Id=$id", ct, ("$id", id));
    public async Task<Run> CreateRunAsync(Project project, RunKind kind, CancellationToken ct = default)
    {
        var run = new Run(Guid.NewGuid(), project.Id, kind, RunState.Starting, RunOutcome.Inconclusive,
            Completeness.CompleteWithinScope, DateTimeOffset.UtcNow, null, project);
        await SaveRunAsync(run, ct);
        return run;
    }
    public Task SaveRunAsync(Run run, CancellationToken ct = default) =>
        ExecuteAsync("""
            INSERT INTO Runs(Id,ProjectId,State,StartedAt,Json) VALUES($id,$project,$state,$start,$json)
            ON CONFLICT(Id) DO UPDATE SET State=$state, Json=$json
            """, ct, ("$id", run.Id), ("$project", run.ProjectId), ("$state", run.State.ToString()),
            ("$start", run.StartedAt.ToString("O")), ("$json", Serialize(run)));
    public Task<Run?> GetRunAsync(Guid id, CancellationToken ct = default) =>
        UseAsync(db => ReadOne<Run>(Command(db, "SELECT Json FROM Runs WHERE Id=$id", null, ("$id", id))), ct);
    public Task<IReadOnlyList<Run>> GetRunsAsync(Guid projectId, int limit = 50, CancellationToken ct = default) =>
        UseAsync<IReadOnlyList<Run>>(db => ReadMany<Run>(Command(db,
            "SELECT Json FROM Runs WHERE ProjectId=$id ORDER BY StartedAt DESC LIMIT $limit", null,
            ("$id", projectId), ("$limit", Math.Clamp(limit, 1, 200)))), ct);
    public Task DeleteRunAsync(Guid id, CancellationToken ct = default) =>
        ExecuteAsync("DELETE FROM Runs WHERE Id=$id", ct, ("$id", id));

    public Task<Observation> AppendAsync(Observation raw, CancellationToken ct = default) => UseAsync(db =>
    {
        var item = Privacy.Sanitize(raw);
        var duplicate = ReadOne<Observation>(Command(db, "SELECT Json FROM Observations WHERE Id=$id", null, ("$id", item.Id)));
        if (duplicate is not null) return duplicate;
        using var tx = db.BeginTransaction();
        var sequence = Convert.ToInt64(Command(db, "SELECT COALESCE(MAX(Sequence),0)+1 FROM Observations WHERE RunId=$id", tx,
            ("$id", item.RunId)).ExecuteScalar());
        item = item with { Sequence = sequence };
        Command(db, """
            INSERT INTO Observations(Id,RunId,Sequence,RuleId,Severity,Category,Value,IsIssue,Json)
            VALUES($id,$run,$seq,$rule,$severity,$category,$value,$issue,$json)
            """, tx, ("$id", item.Id), ("$run", item.RunId), ("$seq", sequence), ("$rule", item.RuleId),
            ("$severity", item.Severity.ToString()), ("$category", item.Category.ToString()), ("$value", item.Value),
            ("$issue", item.IsIssue ? 1 : 0), ("$json", Serialize(item))).ExecuteNonQuery();
        if (item.IsIssue)
        {
            var fingerprint = Privacy.Fingerprint(item);
            var prior = ReadOne<Issue>(Command(db, "SELECT Json FROM Issues WHERE RunId=$run AND Fingerprint=$fp", tx,
                ("$run", item.RunId), ("$fp", fingerprint)));
            var issue = prior is null
                ? new Issue(Guid.NewGuid(), item.RunId, fingerprint, item.RuleId, item.Category, item.Severity,
                    item.Title, item.Message, item.Url, item.FilePath, item.Line, item.Column, item.Stack,
                    item.OccurredAt!.Value, item.OccurredAt.Value, 1)
                : prior with { LastSeenAt = item.OccurredAt!.Value, OccurrenceCount = prior.OccurrenceCount + 1 };
            Command(db, """
                INSERT INTO Issues(Id,RunId,Fingerprint,Severity,Json) VALUES($id,$run,$fp,$severity,$json)
                ON CONFLICT(RunId,Fingerprint) DO UPDATE SET Json=$json
                """, tx, ("$id", issue.Id), ("$run", issue.RunId), ("$fp", fingerprint),
                ("$severity", issue.Severity.ToString()), ("$json", Serialize(issue))).ExecuteNonQuery();
        }
        tx.Commit();
        return item;
    }, ct);

    public Task<EventPage> GetEventsAsync(Guid runId, long after = 0, int limit = 200, CancellationToken ct = default) => UseAsync(db =>
    {
        limit = Math.Clamp(limit, 1, 1000);
        var items = ReadMany<Observation>(Command(db,
            "SELECT Json FROM Observations WHERE RunId=$id AND Sequence>$seq ORDER BY Sequence LIMIT $limit", null,
            ("$id", runId), ("$seq", after), ("$limit", limit + 1)));
        var more = items.Count > limit;
        if (more) items.RemoveAt(items.Count - 1);
        return new EventPage(items, items.Count == 0 ? after : items[^1].Sequence, more);
    }, ct);

    public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default) => UseAsync(db =>
    {
        var run = ReadOne<Run>(Command(db, "SELECT Json FROM Runs WHERE Id=$id", null, ("$id", runId)));
        if (run is null) return null;
        var issues = ReadMany<Issue>(Command(db, "SELECT Json FROM Issues WHERE RunId=$id ORDER BY rowid", null, ("$id", runId)));
        var metrics = ReadMany<Observation>(Command(db, """
            SELECT Json FROM Observations WHERE RunId=$id AND Value IS NOT NULL AND RuleId != 'http.request'
            AND Sequence IN (SELECT MAX(Sequence) FROM Observations WHERE RunId=$id AND Value IS NOT NULL GROUP BY RuleId)
            ORDER BY Sequence
            """, null, ("$id", runId)));
        var count = Convert.ToInt64(Command(db, "SELECT COUNT(*) FROM Observations WHERE RunId=$id", null, ("$id", runId)).ExecuteScalar());
        var values = new List<double>();
        using (var reader = Command(db, "SELECT Value FROM Observations WHERE RunId=$id AND RuleId='http.request' AND Value IS NOT NULL ORDER BY Value", null, ("$id", runId)).ExecuteReader())
            while (reader.Read()) values.Add(reader.GetDouble(0));
        var dropped = metrics.Where(x => x.RuleId == "system.dropped").Select(x => (long)(x.Value ?? 0)).LastOrDefault();
        var summary = new RunSummary(count, issues.Count, issues.Count(x => x.Severity >= Severity.Error),
            issues.Count(x => x.Severity == Severity.Warning),
            Convert.ToInt64(Command(db, "SELECT COUNT(*) FROM Observations WHERE RunId=$id AND RuleId='http.request'", null, ("$id", runId)).ExecuteScalar()),
            values.Count, Percentile(values, .5), Percentile(values, .95), dropped);
        return new RunDetails(run, summary, issues, metrics);
    }, ct);

    public Task SaveArchitectureAsync(ArchitectureDocument document, CancellationToken ct = default)
    {
        using var buffer = new SnapshotBuffer(ct);
        JsonSerializer.Serialize(buffer, document, JsonDefaults.Options);
        var json = System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        return ExecuteAsync("""
            INSERT INTO ArchitectureSnapshots(Id,ProjectId,RunId,GeneratedAt,Json) VALUES($id,$project,$run,$time,$json)
            """, ct, ("$id", document.Id), ("$project", document.ProjectId), ("$run", document.RunId),
            ("$time", document.GeneratedAt.ToString("O")), ("$json", json));
    }
    public Task<ArchitectureDocument?> GetArchitectureAsync(Guid projectId, Guid? snapshotId = null, CancellationToken ct = default) =>
        UseAsync(db => ReadOne<ArchitectureDocument>(Command(db, """
            SELECT Json FROM ArchitectureSnapshots WHERE ProjectId=$project AND ($snapshot IS NULL OR Id=$snapshot)
            ORDER BY GeneratedAt DESC, Id DESC LIMIT 1
            """, null, ("$project", projectId), ("$snapshot", snapshotId))), ct);

    public async Task MarkInterruptedAsync(CancellationToken ct = default)
    {
        var active = await UseAsync(db => ReadMany<Run>(Command(db,
            "SELECT Json FROM Runs WHERE State IN ('Starting','Running','Stopping')")), ct);
        foreach (var run in active)
            await SaveRunAsync(run with { State = RunState.Interrupted, Outcome = RunOutcome.Inconclusive,
                Completeness = Completeness.Partial, EndedAt = DateTimeOffset.UtcNow, StopReason = "Host interrupted" }, ct);
    }
    private static double? Percentile(List<double> sorted, double p) => sorted.Count == 0 ? null :
        sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * p) - 1, 0, sorted.Count - 1)];
    private async Task<T> UseAsync<T>(Func<SqliteConnection, T> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var db = new SqliteConnection(ConnectionString);
            await db.OpenAsync(ct);
            return action(db);
        }
        finally { gate.Release(); }
    }
    private Task ExecuteAsync(string sql, CancellationToken ct, params (string, object?)[] args) =>
        UseAsync(db => Command(db, sql, null, args).ExecuteNonQuery(), ct);
    private static SqliteCommand Command(SqliteConnection db, string sql, SqliteTransaction? tx = null, params (string, object?)[] args)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        foreach (var (key, value) in args) command.Parameters.AddWithValue(key, value is Guid guid ? guid.ToString() : value ?? DBNull.Value);
        return command;
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonDefaults.Options);
    private static T? ReadOne<T>(SqliteCommand command) where T : class
    {
        using (command)
        {
            var json = command.ExecuteScalar() as string;
            return json is null ? null : JsonSerializer.Deserialize<T>(json, JsonDefaults.Options);
        }
    }
    private static List<T> ReadMany<T>(SqliteCommand command)
    {
        using (command)
        using (var reader = command.ExecuteReader())
        {
            var result = new List<T>();
            while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), JsonDefaults.Options)!);
            return result;
        }
    }
    private sealed class SnapshotBuffer(CancellationToken ct) : MemoryStream
    {
        private void Check(int count)
        {
            ct.ThrowIfCancellationRequested();
            if (Length + count > 25_000_000)
                throw new InvalidOperationException("Migawka architektury przekracza limit 25 MB. Zawęź katalog źródeł.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
    }
    public void Dispose() => gate.Dispose();
}
