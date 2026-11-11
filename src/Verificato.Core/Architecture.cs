namespace Verificato.Core;

public sealed record ArchitectureDocument(Guid Id, Guid ProjectId, Guid RunId, string Name, string SourceRoot,
    DateTimeOffset GeneratedAt, string SourceFingerprint, IReadOnlyList<ArchitectureContainer> Containers,
    IReadOnlyList<ArchitectureComponent> Components, IReadOnlyList<ArchitectureRelationship> Relationships,
    IReadOnlyList<UnresolvedProjectReference> UnresolvedReferences, IReadOnlyList<string> Warnings,
    string SchemaVersion = "1", string Generator = "verificato.csharp-architecture/0.1");
public sealed record ArchitectureContainer(string Id, string Name, string Technology, string Description,
    IReadOnlyList<string> ComponentIds, bool IsInferred = true);
public sealed record ArchitectureComponent(string Id, string Name, string ProjectFile, string ProjectFileId, string ProjectFileHash, string Technology,
    string Kind, bool IsTest, string Description, string DescriptionBasis, IReadOnlyList<string> Namespaces,
    IReadOnlyList<ArchitectureFile> Files, IReadOnlyList<ArchitecturePackage> Packages);
public sealed record ArchitectureFile(string Id, string Path, string Hash, int Lines,
    IReadOnlyList<ArchitectureType> Types);
public sealed record ArchitectureType(string Name, string FullName, string Namespace, string Kind, string Accessibility,
    string? Summary, int Line, int EndLine, IReadOnlyList<string> BaseTypes, IReadOnlyList<ArchitectureMember> Members);
public sealed record ArchitectureMember(string Name, string Kind, string Signature, string? Summary, int Line);
public sealed record ArchitecturePackage(string Name, string? Version, string? Condition);
public sealed record ArchitectureRelationship(string Id, string SourceId, string TargetId, string Description,
    string Technology, string EvidenceFile, int Line, string? Condition);
public sealed record UnresolvedProjectReference(string SourceId, string Include, string Reason, int Line);
public sealed record SourceCodeLine(int Number, string Text);
public sealed record SourceCodePreview(string FileId, string Path, string Hash, int TotalLines,
    int StartLine, IReadOnlyList<SourceCodeLine> Lines);

public interface IArchitectureAnalyzer
{
    Task<ArchitectureDocument> AnalyzeAsync(Project project, Guid runId, CancellationToken ct);
    Task<SourceCodePreview> ReadSourceAsync(Project project, ArchitectureDocument snapshot, string fileId,
        int line, CancellationToken ct);
}
public interface IArchitectureStore
{
    Task SaveArchitectureAsync(ArchitectureDocument document, CancellationToken ct = default);
    Task<ArchitectureDocument?> GetArchitectureAsync(Guid projectId, Guid? snapshotId = null, CancellationToken ct = default);
}
