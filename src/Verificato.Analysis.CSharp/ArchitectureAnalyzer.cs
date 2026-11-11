using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Verificato.Core;

namespace Verificato.Analysis.CSharp;

/// <summary>Structural inventory only: never evaluates MSBuild, loads plugins, restores or executes source.</summary>
public sealed class ArchitectureAnalyzer : IArchitectureAnalyzer
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", "bin", "obj", "node_modules", "dist", "artifacts", ".vs", ".idea", "packages", ".data" };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private sealed record ProjectData(string AbsolutePath, string Id, string Hash, XDocument Xml);

    public async Task<ArchitectureDocument> AnalyzeAsync(Project project, Guid runId, CancellationToken ct)
    {
        ProjectValidation.Validate(new(project.Name, project.SourcePath, project.ApplicationUrl, project.AllowedOrigins), project);
        var root = Path.GetFullPath(project.SourcePath);
        var warnings = new List<string>();
        var inventory = Enumerate(root, ct);
        var projectPaths = inventory.Where(x => x.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(PathComparer).ToArray();
        if (projectPaths.Length > 200) throw new InvalidOperationException("Limit architektury: 200 projektów C#. Zawęź katalog źródeł.");
        var projects = new List<ProjectData>();
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long projectBytes = 0;
        foreach (var path in projectPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var text = await ReadBoundedAsync(root, path, 1_000_000, ct);
                projectBytes += Encoding.UTF8.GetByteCount(text);
                if (projectBytes > 10_000_000) throw new InvalidOperationException("Limit architektury: 10 MB metadanych .csproj.");
                fingerprint.AppendData(Encoding.UTF8.GetBytes(Relative(root, path) + "\0" + text + "\0"));
                using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var xml = XDocument.Load(reader, LoadOptions.SetLineInfo);
                if (xml.Root?.Name.LocalName != "Project") { Warn($"{Relative(root, path)}: nieprawidłowy element Project."); continue; }
                projects.Add(new(path, StableId(Relative(root, path)), Hash(text), xml));
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            { Warn($"{Relative(root, path)}: pominięto nieczytelny lub niebezpieczny XML projektu ({ex.GetType().Name})."); }
        }
        var filesByProject = projects.ToDictionary(p => p.Id, _ => new List<ArchitectureFile>());
        var sources = inventory.Where(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Order(PathComparer).ToArray();
        if (sources.Length > 10_000) throw new InvalidOperationException("Limit architektury: 10 000 plików C#.");
        long totalBytes = 0;
        var totalTypes = 0;
        var totalMembers = 0;
        foreach (var path in sources)
        {
            ct.ThrowIfCancellationRequested();
            var owners = projects.Where(p => IsInside(Path.GetDirectoryName(p.AbsolutePath)!, path)).ToArray();
            if (owners.Length == 0) continue;
            var deepest = owners.Max(p => Path.GetDirectoryName(p.AbsolutePath)!.Length);
            owners = owners.Where(p => Path.GetDirectoryName(p.AbsolutePath)!.Length == deepest).ToArray();
            if (owners.Length > 1) Warn("Wspólny katalog kilku .csproj: przypisanie źródeł jest niejednoznaczne; pokazano je w każdym z tych komponentów.");
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2_000_000 || totalBytes + info.Length > 50_000_000)
                { Warn("Pominięto część źródeł: limit 2 MB/plik lub 50 MB/diagram."); continue; }
                var text = await ReadBoundedAsync(root, path, 2_000_000, ct);
                totalBytes += Encoding.UTF8.GetByteCount(text);
                var relative = Relative(root, path);
                var hash = Hash(text);
                fingerprint.AppendData(Encoding.UTF8.GetBytes(relative + "\0" + hash + "\0"));
                var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp14), path, cancellationToken: ct);
                var syntax = await tree.GetRootAsync(ct);
                if (syntax.ContainsDirectives && syntax.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsKind(SyntaxKind.IfDirectiveTrivia)))
                    Warn("Inwentaryzacja C# 14 bez symboli preprocesora: deklaracje ukryte za #if mogą nie być widoczne.");
                if (syntax.ContainsDiagnostics) Warn("Część plików zawiera diagnostykę składni; ich inwentaryzacja typów może być niepełna.");
                var declarations = syntax.DescendantNodes().Where(x => x is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).ToArray();
                totalTypes += declarations.Length;
                if (totalTypes > 30_000) throw new InvalidOperationException("Limit architektury: 30 000 deklaracji typów.");
                if (declarations.Take(200).Any(node => node.ChildNodes().OfType<MemberDeclarationSyntax>().Count() > 200))
                    Warn("Ograniczono listy członków typów do 200 pozycji. Pełne deklaracje pozostają dostępne w podglądzie plików.");
                var types = declarations.Take(200).Select(DescribeType).ToArray();
                totalMembers += types.Sum(x => x.Members.Count);
                if (totalMembers > 60_000) throw new InvalidOperationException("Limit architektury: 60 000 deklaracji członków typów.");
                if (declarations.Length > types.Length) Warn($"{relative}: ograniczono podgląd do 200 deklaracji typów.");
                foreach (var owner in owners)
                    filesByProject[owner.Id].Add(new(StableId(owner.Id + "|" + relative), relative, hash, tree.GetText(ct).Lines.Count, types));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Warn($"{Relative(root, path)}: nie można odczytać źródła ({ex.GetType().Name})."); }
        }
        var components = new List<ArchitectureComponent>();
        var relationships = new List<ArchitectureRelationship>();
        var unresolved = new List<UnresolvedProjectReference>();
        var byPath = projects.ToDictionary(p => p.AbsolutePath, PathComparer);
        foreach (var p in projects)
        {
            ct.ThrowIfCancellationRequested();
            string? Property(string name) => p.Xml.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
            var sdk = p.Xml.Root?.Attribute("Sdk")?.Value ?? "MSBuild";
            var frameworks = Property("TargetFramework") ?? Property("TargetFrameworks");
            var packages = p.Xml.Descendants().Where(x => x.Name.LocalName == "PackageReference")
                .Take(500).Select(x => new ArchitecturePackage(Privacy.Clean(x.Attribute("Include")?.Value ?? x.Attribute("Update")?.Value ?? "?", 200),
                    Privacy.Clean(x.Attribute("Version")?.Value ?? x.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value, 100), Condition(x))).ToArray();
            if (p.Xml.Descendants().Count(x => x.Name.LocalName == "PackageReference") > packages.Length)
                Warn($"{Path.GetFileName(p.AbsolutePath)}: ograniczono listę do 500 pakietów.");
            var test = Property("IsTestProject")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                packages.Any(x => x.Name == "Microsoft.NET.Test.Sdk");
            var kind = test ? "Test" : sdk.Contains("Web", StringComparison.OrdinalIgnoreCase) ? "WebApplication" :
                Property("OutputType") is "Exe" or "WinExe" ? "Executable" : "Library";
            var name = Path.GetFileNameWithoutExtension(p.AbsolutePath);
            var files = filesByProject[p.Id];
            var namespaces = files.SelectMany(f => f.Types).Select(t => t.Namespace).Where(x => x.Length > 0).Distinct().Order().Take(100).ToArray();
            var docs = files.SelectMany(f => f.Types).Where(t => t.Accessibility == "public" && t.Summary is not null).Select(t => t.Summary!).Take(2).ToArray();
            var description = docs.Length > 0 ? Privacy.Clean(string.Join(" ", docs), 450) :
                $"{files.Count} plików C#, {files.Sum(f => f.Types.Count)} deklaracji typów." +
                (namespaces.Length > 0 ? $" Przestrzenie nazw: {string.Join(", ", namespaces.Take(3))}." : " Punkt wejścia lub deklaracje w globalnej przestrzeni nazw.");
            components.Add(new(p.Id, name, Relative(root, p.AbsolutePath), StableId(p.Id + "|project"), p.Hash, "C# · " + Privacy.Clean(frameworks ?? sdk, 200), kind, test,
                description, docs.Length > 0 ? "XML documentation publicznych typów" : "Inwentaryzacja składni Roslyn; bez interpretacji odpowiedzialności biznesowej", namespaces, files, packages));
            if (p.Xml.Descendants().Any(x => x.Name.LocalName is "Import" or "Target" or "Compile") || p.Xml.Descendants().Any(x => x.Attribute("Condition") is not null))
                Warn($"{name}: niestandardowe/warunkowe MSBuild. Pliki przypisano według katalogów, nie ewaluacji Compile/Remove.");
            var referenceIndex = 0;
            foreach (var reference in p.Xml.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
            {
                if (relationships.Count + unresolved.Count >= 5000) throw new InvalidOperationException("Limit architektury: 5000 referencji projektowych.");
                var index = referenceIndex++;
                var include = reference.Attribute("Include")?.Value ?? "";
                var line = ((IXmlLineInfo)reference).LineNumber;
                string? reason = null;
                string? target = null;
                if (include.IndexOfAny(['$', '%', '*', '?', ';']) >= 0 || string.IsNullOrWhiteSpace(include))
                    reason = "Wymaga ewaluacji MSBuild; cel nie został zgadnięty.";
                else
                {
                    try
                    {
                        target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(p.AbsolutePath)!, include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
                        if (!IsInside(root, target)) reason = "Poza zatwierdzonym katalogiem źródeł; nie odczytano celu.";
                        else if (!byPath.ContainsKey(target)) reason = "Cel nie istnieje, jest linkiem lub nie został bezpiecznie zinwentaryzowany.";
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    { reason = "Nieprawidłowa ścieżka referencji."; }
                }
                if (reason is not null)
                    unresolved.Add(new(p.Id, Privacy.Clean(include, 500), reason, line));
                else
                    relationships.Add(new(StableId(p.Id + "|" + byPath[target!].Id + "|" + line + "|" + index), p.Id, byPath[target!].Id,
                        "Odwołuje się do", "C# / ProjectReference", Relative(root, p.AbsolutePath), line, Condition(reference)));
            }
        }
        if (unresolved.Count > 0) Warn($"Nierozwiązane ProjectReference: {unresolved.Count}. Nie dodano fikcyjnych komponentów ani połączeń.");
        if (components.Count == 0) Warn("Nie znaleziono obsługiwanych .csproj w zatwierdzonym katalogu.");
        Warn("C3: projekcja 1 projekt C# = 1 komponent. Granice kontenerów wywnioskowano z projektów startowych i referencji; wymagają potwierdzenia. ProjectReference to zależność deklarowana, nie dowód wywołania runtime.");
        Warn("Bez MSBuild/restore/generatorów. Inwentaryzacja fizycznych katalogów pomija bin/obj/linki; warunki, pliki linkowane i reguły Compile mogą zmienić faktyczny skład kompilacji. Brak deklaracji nie dowodzi braku zależności dynamicznych.");
        var containers = BuildContainers(components, relationships);
        return new(Guid.NewGuid(), project.Id, runId, project.Name, root, DateTimeOffset.UtcNow,
            Convert.ToHexString(fingerprint.GetHashAndReset()), containers, components, relationships, unresolved, warnings);

        void Warn(string text) { if (warnings.Count < 200 && !warnings.Contains(text)) warnings.Add(Privacy.Clean(text, 1000)); }
    }

    public async Task<SourceCodePreview> ReadSourceAsync(Project project, ArchitectureDocument snapshot, string fileId, int line, CancellationToken ct)
    {
        if (snapshot.ProjectId != project.Id || !string.Equals(Path.GetFullPath(project.SourcePath), Path.GetFullPath(snapshot.SourceRoot), PathComparison))
            throw new InvalidOperationException("Katalog projektu zmienił się. Odśwież diagram przed podglądem kodu.");
        var projectFile = snapshot.Components.FirstOrDefault(x => x.ProjectFileId == fileId);
        var file = snapshot.Components.SelectMany(x => x.Files).FirstOrDefault(x => x.Id == fileId)
            ?? (projectFile is null ? null : new ArchitectureFile(projectFile.ProjectFileId, projectFile.ProjectFile, projectFile.ProjectFileHash, 0, []))
            ?? throw new KeyNotFoundException("Plik nie należy do tej migawki architektury.");
        var path = Path.GetFullPath(Path.Combine(snapshot.SourceRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        string text;
        try { text = await ReadBoundedAsync(snapshot.SourceRoot, path, 2_000_000, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Plik nie jest już dostępny w dopuszczalnym zakresie. Odśwież diagram lub sprawdź uprawnienia."); }
        if (Hash(text) != file.Hash) throw new InvalidOperationException("Plik zmienił się od analizy. Odśwież diagram, aby lokalizacje odpowiadały kodowi.");
        var lines = Microsoft.CodeAnalysis.Text.SourceText.From(text).Lines;
        var start = Math.Max(1, Math.Clamp(line, 1, lines.Count) - 10);
        return new(file.Id, file.Path, file.Hash, lines.Count, start,
            lines.Skip(start - 1).Take(120).Select((value, i) => new SourceCodeLine(start + i, value.ToString() is { Length: > 4000 } longLine ? longLine[..4000] + "…" : value.ToString())).ToArray());
    }

    private static List<string> Enumerate(string root, CancellationToken ct)
    {
        var result = new List<string>();
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        var count = 0;
        while (pending.TryPop(out var item))
        {
            ct.ThrowIfCancellationRequested();
            if (item.Depth > 64) throw new InvalidOperationException("Maksymalna głębokość analizy: 64 katalogi.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(item.Path))
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 60_000) throw new InvalidOperationException("Limit inwentaryzacji: 60 000 wpisów. Zawęź katalog.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0)
                { if (!Excluded.Contains(Path.GetFileName(entry))) pending.Push((entry, item.Depth + 1)); }
                else if (entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || entry.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) result.Add(entry);
            }
        }
        return result;
    }
    private static async Task<string> ReadBoundedAsync(string root, string path, long maxBytes, CancellationToken ct)
    {
        EnsureSafeFile(root, path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > maxBytes) throw new IOException("Plik przekracza limit odczytu.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return await reader.ReadToEndAsync(ct);
    }
    private static void EnsureSafeFile(string root, string path)
    {
        if (!IsInside(root, path)) throw new ArgumentException("Plik poza zatwierdzonym zakresem.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Odczyt linku jest zabroniony.");
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Ścieżka przechodzi przez link/reparse point.");
    }
    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison);
    }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static string StableId(string value) => Hash(value)[..24].ToLowerInvariant();
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string? Condition(XElement element)
    {
        var conditions = element.AncestorsAndSelf().Select(x => x.Attribute("Condition")?.Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return conditions.Length == 0 ? null : Privacy.Clean(string.Join(" && ", conditions), 500);
    }
    private static ArchitectureType DescribeType(SyntaxNode node)
    {
        var declaration = node as BaseTypeDeclarationSyntax;
        var name = declaration?.Identifier.ValueText ?? ((DelegateDeclarationSyntax)node).Identifier.ValueText;
        var namespaces = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(x => x.Name.ToString());
        var parents = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(x => x.Identifier.ValueText);
        var fullName = string.Join(".", namespaces.Concat(parents).Append(name));
        var modifiers = declaration?.Modifiers ?? ((DelegateDeclarationSyntax)node).Modifiers;
        var explicitAccess = modifiers.Where(m => m.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword).Select(m => m.ValueText).ToArray();
        var accessibility = explicitAccess.Length > 0 ? string.Join(" ", explicitAccess) :
            node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault() is InterfaceDeclarationSyntax ? "public" : parents.Any() ? "private" : "internal";
        var kind = node switch { InterfaceDeclarationSyntax => "interface", RecordDeclarationSyntax => "record", StructDeclarationSyntax => "struct", EnumDeclarationSyntax => "enum", DelegateDeclarationSyntax => "delegate", _ => "class" };
        var members = node.ChildNodes().OfType<MemberDeclarationSyntax>().Where(x => x is not BaseTypeDeclarationSyntax).Select(DescribeMember).OfType<ArchitectureMember>().Take(200).ToArray();
        var location = node.GetLocation().GetLineSpan();
        return new(name, fullName, string.Join(".", namespaces), kind, accessibility, Summary(node), location.StartLinePosition.Line + 1,
            location.EndLinePosition.Line + 1, declaration?.BaseList?.Types.Select(x => x.Type.ToString()).ToArray() ?? [], members);
    }
    private static ArchitectureMember? DescribeMember(MemberDeclarationSyntax member)
    {
        var (name, kind, signature) = member switch
        {
            MethodDeclarationSyntax m => (m.Identifier.ValueText, "method", $"{m.ReturnType} {m.Identifier}{m.TypeParameterList}({string.Join(", ", m.ParameterList.Parameters.Select(p => p.WithDefault(null).ToString()))})"),
            ConstructorDeclarationSyntax m => (m.Identifier.ValueText, "constructor", $"{m.Identifier}({string.Join(", ", m.ParameterList.Parameters.Select(p => p.WithDefault(null).ToString()))})"),
            PropertyDeclarationSyntax p => (p.Identifier.ValueText, "property", $"{p.Type} {p.Identifier}"),
            FieldDeclarationSyntax f => (string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText)), "field", $"{f.Declaration.Type} {string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.ValueText))}"),
            EventDeclarationSyntax e => (e.Identifier.ValueText, "event", $"event {e.Type} {e.Identifier}"),
            EnumMemberDeclarationSyntax e => (e.Identifier.ValueText, "enum value", e.Identifier.ValueText),
            EventFieldDeclarationSyntax e => (string.Join(", ", e.Declaration.Variables.Select(v => v.Identifier.ValueText)), "event", $"event {e.Declaration.Type} {string.Join(", ", e.Declaration.Variables.Select(v => v.Identifier.ValueText))}"),
            IndexerDeclarationSyntax i => ("this", "indexer", $"{i.Type} this{i.ParameterList}"),
            OperatorDeclarationSyntax o => ("operator " + o.OperatorToken, "operator", $"{o.ReturnType} operator {o.OperatorToken}{o.ParameterList}"),
            ConversionOperatorDeclarationSyntax o => (o.Type.ToString(), "conversion", $"{o.ImplicitOrExplicitKeyword} operator {o.Type}{o.ParameterList}"),
            DestructorDeclarationSyntax d => ("~" + d.Identifier, "destructor", $"~{d.Identifier}()"),
            _ => ("", "", "")
        };
        return name.Length == 0 ? null : new(name, kind, Privacy.Clean(signature, 500), Summary(member), member.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
    }
    private static string? Summary(SyntaxNode node)
    {
        var doc = node.GetLeadingTrivia().Select(x => x.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().FirstOrDefault();
        var summary = doc?.Content.OfType<XmlElementSyntax>().FirstOrDefault(x => x.StartTag.Name.ToString() == "summary");
        if (summary is null) return null;
        var text = Regex.Replace(string.Join(" ", summary.DescendantNodes().OfType<XmlTextSyntax>().Select(x => x.TextTokens.ToString())), @"\s+", " ").Replace("///", "").Trim();
        return text.Length == 0 ? null : Privacy.Clean(text, 500);
    }
    private static IReadOnlyList<ArchitectureContainer> BuildContainers(List<ArchitectureComponent> components, List<ArchitectureRelationship> relationships)
    {
        var entries = components.Where(c => !c.IsTest && c.Kind is "WebApplication" or "Executable").ToArray();
        if (entries.Length == 0)
            return [new("logical", "Aplikacja · granica logiczna", "C# / .NET", "Nie znaleziono projektu startowego. Granica logiczna wymaga potwierdzenia.", components.Where(c => !c.IsTest).Select(c => c.Id).ToArray())];
        return entries.Select(entry =>
        {
            var reachable = new HashSet<string> { entry.Id };
            var pending = new Stack<string>(); pending.Push(entry.Id);
            while (pending.TryPop(out var id))
                foreach (var edge in relationships.Where(x => x.SourceId == id))
                    if (components.Any(c => c.Id == edge.TargetId && !c.IsTest) && reachable.Add(edge.TargetId)) pending.Push(edge.TargetId);
            return new ArchitectureContainer(entry.Id, entry.Name, entry.Technology,
                "Granica wywnioskowana z projektu startowego i ProjectReference; nie potwierdza wspólnego wdrożenia. Projekt startowy pokazano jako komponent kompozycji.", reachable.ToArray());
        }).ToArray();
    }
}
