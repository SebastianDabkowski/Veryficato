using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verificato.Core;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

public enum RunKind { Interactive, CodeAnalysis }
public enum RunState { Starting, Running, Stopping, Completed, Failed, Interrupted }
public enum RunOutcome { NoIssuesDetected, IssuesDetected, Inconclusive }
public enum Completeness { CompleteWithinScope, Partial, Unavailable }
public enum Severity { Info, Warning, Error, Critical }
public enum Category { JavaScript, Console, Http, Resource, Performance, Compilation, Analyzer, CodeQuality, System }

public sealed record Project(Guid Id, string Name, string SourcePath, string ApplicationUrl,
    string[] AllowedOrigins, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ProjectInput(string Name, string SourcePath, string ApplicationUrl, string[]? AllowedOrigins = null);
public sealed record Run(Guid Id, Guid ProjectId, RunKind Kind, RunState State, RunOutcome Outcome,
    Completeness Completeness, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, Project Configuration,
    string? Failure = null, string? StopReason = null, string? Limitations = null, string EngineVersion = "0.1.0")
{
    public bool IsActive => State is RunState.Starting or RunState.Running or RunState.Stopping;
}

public sealed record Observation(
    Guid RunId, string RuleId, Category Category, Severity Severity, string Title, string Message,
    string? Url = null, string? FilePath = null, int? Line = null, int? Column = null,
    string? Stack = null, double? Value = null, string? Unit = null, bool IsIssue = true,
    DateTimeOffset? OccurredAt = null, Guid Id = default, long Sequence = 0);
public sealed record Issue(Guid Id, Guid RunId, string Fingerprint, string RuleId, Category Category,
    Severity Severity, string Title, string Message, string? Url, string? FilePath, int? Line, int? Column,
    string? Stack, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, long OccurrenceCount);
public sealed record RunSummary(long EventCount, long IssueCount, long ErrorCount, long WarningCount,
    long RequestCount, long MeasuredRequestCount, double? MedianResponseMs, double? P95ResponseMs, long DroppedObservations);
public sealed record RunDetails(Run Run, RunSummary Summary, IReadOnlyList<Issue> Issues,
    IReadOnlyList<Observation> Metrics);
public sealed record EventPage(IReadOnlyList<Observation> Items, long NextSequence, bool HasMore);
public sealed record SystemStatus(string Version, bool BrowserInstalled, string DataDirectory,
    string AnalysisMode, int MaxSessionMinutes, int MaxObservations);

public interface IVerificationStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken ct = default);
    Task<Project?> GetProjectAsync(Guid id, CancellationToken ct = default);
    Task SaveProjectAsync(Project project, CancellationToken ct = default);
    Task DeleteProjectAsync(Guid id, CancellationToken ct = default);
    Task<Run> CreateRunAsync(Project project, RunKind kind, CancellationToken ct = default);
    Task SaveRunAsync(Run run, CancellationToken ct = default);
    Task<Run?> GetRunAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Run>> GetRunsAsync(Guid projectId, int limit = 50, CancellationToken ct = default);
    Task DeleteRunAsync(Guid id, CancellationToken ct = default);
    Task<Observation> AppendAsync(Observation observation, CancellationToken ct = default);
    Task<EventPage> GetEventsAsync(Guid runId, long after = 0, int limit = 200, CancellationToken ct = default);
    Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default);
    Task MarkInterruptedAsync(CancellationToken ct = default);
}

public interface IObservationSink
{
    bool Emit(Observation observation);
    long Dropped(Guid runId);
    Task FlushAsync(CancellationToken ct = default);
}
public sealed record AnalysisResult(Completeness Completeness, string? Limitation = null);
public interface ICodeAnalyzer
{
    string Id { get; }
    Task<AnalysisResult> AnalyzeAsync(Run run, IObservationSink sink, CancellationToken ct);
}
public interface IBrowserSessionRunner
{
    bool IsInstalled { get; }
    Task RunAsync(Run run, IObservationSink sink, Func<Task> onReady, CancellationToken ct);
}
