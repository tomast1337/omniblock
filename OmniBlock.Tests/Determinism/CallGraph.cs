using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OmniBlock.Tests.Determinism;

/// <summary>
///     Compiles the <c>OmniBlock</c> sources and builds a whole-project call graph, so reachability
///     from the movement frontier can be decided statically.
///     <para>
///         Deliberately over-approximates. Virtual and interface calls fan out to every override and
///         implementation in the compilation, and a lambda's or local function's calls are attributed
///         to the method enclosing it. Both make the reachable set larger than any single execution,
///         which is the safe direction: a purity proof wants false positives, not false negatives.
///     </para>
///     <para>
///         What it cannot see: delegates stored in fields, reflection, and behavior selected from
///         JSON at load time. The <c>DeterminismGuard</c> runtime tripwire covers those.
///     </para>
/// </summary>
internal sealed class CallGraph
{
    /// <summary>Fully qualified, no parameters: <c>OmniBlock.Entities.Entity.Move</c>.</summary>
    private static readonly SymbolDisplayFormat s_nameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

    private static readonly Lazy<CallGraph> s_instance = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<IMethodSymbol, List<Edge>> _edges;
    private readonly Dictionary<IMethodSymbol, List<Reference>> _references;

    private CallGraph(
        Dictionary<IMethodSymbol, List<Edge>> edges,
        Dictionary<IMethodSymbol, List<Reference>> references,
        ImmutableArray<IMethodSymbol> allMethods,
        int unresolvedInvocations,
        int totalInvocations,
        ImmutableArray<string> compilationErrors)
    {
        _edges = edges;
        _references = references;
        AllMethods = allMethods;
        UnresolvedInvocations = unresolvedInvocations;
        TotalInvocations = totalInvocations;
        CompilationErrors = compilationErrors;
    }

    /// <summary>Built once per test run — parsing and binding the tree costs real seconds.</summary>
    public static CallGraph Instance => s_instance.Value;

    public ImmutableArray<IMethodSymbol> AllMethods { get; }

    /// <summary>
    ///     Invocations whose target symbol did not bind. The anti-vacuity metric: a broken
    ///     compilation resolves nothing, finds nothing, and passes. Asserted in
    ///     <see cref="StepPurityTests.Analysis_is_not_vacuous" />.
    /// </summary>
    public int UnresolvedInvocations { get; }

    public int TotalInvocations { get; }

    public ImmutableArray<string> CompilationErrors { get; }

    public static string NameOf(ISymbol symbol) => symbol.ToDisplayString(s_nameFormat);

    /// <summary>Every method whose fully qualified name matches, across all overloads.</summary>
    public IEnumerable<IMethodSymbol> Resolve(string qualifiedName) =>
        AllMethods.Where(m => NameOf(m) == qualifiedName);

    /// <summary>
    ///     Breadth-first walk from <paramref name="roots" />, retaining the discovering edge for
    ///     each method so a violation can be reported as a full call path rather than a bare name.
    /// </summary>
    /// <summary>
    ///     Breadth-first walk from <paramref name="roots" />, retaining the discovering edge for
    ///     each method so a violation can be reported as a full call path rather than a bare name.
    ///     <para>
    ///         A method matching <see cref="StepFrontier.CutPoints" /> is recorded as reached but is
    ///         not expanded, so the walk stops at declared deferral boundaries.
    ///     </para>
    /// </summary>
    public Reachability Walk(IEnumerable<IMethodSymbol> roots)
    {
        Dictionary<IMethodSymbol, Edge?> discoveredBy = new(SymbolEqualityComparer.Default);
        HashSet<string> cutAt = [];
        Queue<IMethodSymbol> queue = new();

        foreach (IMethodSymbol root in roots)
        {
            if (discoveredBy.TryAdd(root, null))
            {
                queue.Enqueue(root);
            }
        }

        while (queue.Count > 0)
        {
            IMethodSymbol current = queue.Dequeue();

            string currentName = NameOf(current);
            StepFrontier.CutPoint? cut = StepFrontier.CutPoints.Cast<StepFrontier.CutPoint?>()
                .FirstOrDefault(c => c!.Value.Matches(currentName));

            if (cut is not null)
            {
                // Recorded, so a stale cut point can be detected, but never expanded.
                cutAt.Add(cut.Value.Pattern);
                continue;
            }

            if (!_edges.TryGetValue(current, out List<Edge>? outgoing))
            {
                continue;
            }

            foreach (Edge edge in outgoing)
            {
                if (discoveredBy.TryAdd(edge.Target, edge))
                {
                    queue.Enqueue(edge.Target);
                }
            }
        }

        return new Reachability(discoveredBy, _references, cutAt);
    }

    private static CallGraph Build()
    {
        CSharpCompilation compilation = CompileOmniBlock(out ImmutableArray<string> errors);

        ImmutableArray<INamedTypeSymbol> allTypes = [.. EnumerateTypes(compilation.Assembly.GlobalNamespace)];
        ImmutableArray<IMethodSymbol> allMethods =
        [
            .. allTypes
                .SelectMany(t => t.GetMembers().OfType<IMethodSymbol>())
                .Select(m => (IMethodSymbol)m.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
        ];

        Dictionary<IMethodSymbol, List<Edge>> edges = new(SymbolEqualityComparer.Default);
        Dictionary<IMethodSymbol, List<Reference>> references = new(SymbolEqualityComparer.Default);

        void AddEdge(IMethodSymbol from, IMethodSymbol to, EdgeKind kind, Location? at)
        {
            if (!edges.TryGetValue(from, out List<Edge>? list))
            {
                edges[from] = list = [];
            }

            list.Add(new Edge(from, (IMethodSymbol)to.OriginalDefinition, kind, at));
        }

        // --- Virtual dispatch: a call to a base or interface member reaches every implementation.
        foreach (INamedTypeSymbol type in allTypes)
        {
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
            {
                for (IMethodSymbol? overridden = method.OverriddenMethod;
                     overridden is not null;
                     overridden = overridden.OverriddenMethod)
                {
                    AddEdge((IMethodSymbol)overridden.OriginalDefinition, method, EdgeKind.Override, null);
                }
            }

            if (type.IsAbstract && type.TypeKind == TypeKind.Interface)
            {
                continue;
            }

            foreach (INamedTypeSymbol iface in type.AllInterfaces)
            {
                foreach (ISymbol member in iface.GetMembers())
                {
                    if (member is not IMethodSymbol interfaceMethod)
                    {
                        continue;
                    }

                    if (type.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol impl)
                    {
                        AddEdge((IMethodSymbol)interfaceMethod.OriginalDefinition, impl, EdgeKind.InterfaceImpl, null);
                    }
                }
            }
        }

        // --- Direct calls, plus every symbol referenced in each body (for banned-symbol matching).
        int unresolved = 0;
        int total = 0;

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);

            foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
            {
                if (BodyOf(node) is not { } body || DeclaredMethod(model, node) is not { } owner)
                {
                    continue;
                }

                IMethodSymbol from = (IMethodSymbol)owner.OriginalDefinition;

                foreach (SyntaxNode inner in body.DescendantNodesAndSelf())
                {
                    if (inner is not (InvocationExpressionSyntax or ObjectCreationExpressionSyntax
                        or MemberAccessExpressionSyntax or IdentifierNameSyntax
                        or ImplicitObjectCreationExpressionSyntax))
                    {
                        continue;
                    }

                    bool isCall = inner is InvocationExpressionSyntax
                        or ObjectCreationExpressionSyntax
                        or ImplicitObjectCreationExpressionSyntax;

                    if (isCall)
                    {
                        total++;
                    }

                    SymbolInfo info = model.GetSymbolInfo(inner);
                    ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

                    if (symbol is null)
                    {
                        if (isCall)
                        {
                            unresolved++;
                        }

                        continue;
                    }

                    // Recorded for banned-symbol matching: covers property reads such as
                    // Entity.Random and World.Broadcaster, which are not invocations.
                    if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol)
                    {
                        if (!references.TryGetValue(from, out List<Reference>? refs))
                        {
                            references[from] = refs = [];
                        }

                        refs.Add(new Reference(NameOf(symbol.OriginalDefinition), inner.GetLocation()));
                    }

                    switch (symbol)
                    {
                        case IMethodSymbol called:
                            AddEdge(from, called, EdgeKind.Call, inner.GetLocation());
                            break;

                        // A property access runs its accessor, so the graph must traverse it.
                        case IPropertySymbol property:
                            if (property.GetMethod is { } getter)
                            {
                                AddEdge(from, getter, EdgeKind.PropertyGet, inner.GetLocation());
                            }

                            if (property.SetMethod is { } setter)
                            {
                                AddEdge(from, setter, EdgeKind.PropertySet, inner.GetLocation());
                            }

                            break;
                    }
                }
            }
        }

        return new CallGraph(edges, references, allMethods, unresolved, total, errors);
    }

    private static CSharpCompilation CompileOmniBlock(out ImmutableArray<string> errors)
    {
        string sourceRoot = Path.Combine(RepoRoot(), "OmniBlock");

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);

        List<SyntaxTree> trees = [];
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(file);
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(stream), parseOptions, file));
        }

        // OmniBlock.csproj sets ImplicitUsings=enable, and the SDK-generated global usings file
        // lives under obj/. Synthesised here instead so the analysis does not depend on build state.
        trees.Add(CSharpSyntaxTree.ParseText(
            """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """,
            parseOptions,
            "ImplicitGlobalUsings.g.cs"));

        // The test host already has every OmniBlock package dependency loaded, so its trusted
        // platform assemblies are the reference set. OmniBlock's own output is excluded: its types
        // come from the sources above, and referencing both would make every one ambiguous.
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        List<MetadataReference> references =
        [
            .. trusted
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Where(p => !Path.GetFileName(p).StartsWith("OmniBlock", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "OmniBlock.DeterminismAnalysis",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));

        errors =
        [
            .. compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Where(d => !StepFrontier.ToleratedDiagnostics.Contains(d.Id))
                .Select(d => $"{d.Id} {d.Location.GetLineSpan()}: {d.GetMessage()}")
                .Take(25)
        ];

        return compilation;
    }

    /// <summary>
    ///     Repo root from this file's compile-time path, so the test does not depend on the working
    ///     directory the runner happens to use.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Directory.GetParent(thisFile)!.Parent!.Parent!.FullName;

    private static SyntaxNode? BodyOf(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax m => m.Body ?? (SyntaxNode?)m.ExpressionBody,
        AccessorDeclarationSyntax a => a.Body ?? (SyntaxNode?)a.ExpressionBody,
        LocalFunctionStatementSyntax f => f.Body ?? (SyntaxNode?)f.ExpressionBody,
        PropertyDeclarationSyntax p => p.ExpressionBody,
        _ => null,
    };

    private static IMethodSymbol? DeclaredMethod(SemanticModel model, SyntaxNode node) =>
        node switch
        {
            // An expression-bodied property declares a property; its body is the getter.
            PropertyDeclarationSyntax => (model.GetDeclaredSymbol(node) as IPropertySymbol)?.GetMethod,
            _ => model.GetDeclaredSymbol(node) as IMethodSymbol,
        };

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceOrTypeSymbol root)
    {
        foreach (ISymbol member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (INamedTypeSymbol nested in EnumerateTypes(ns))
                    {
                        yield return nested;
                    }

                    break;

                case INamedTypeSymbol type:
                    yield return type;

                    foreach (INamedTypeSymbol nested in EnumerateTypes(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    internal enum EdgeKind
    {
        Call,
        PropertyGet,
        PropertySet,
        Override,
        InterfaceImpl,
    }

    internal readonly record struct Edge(IMethodSymbol From, IMethodSymbol Target, EdgeKind Kind, Location? At);

    internal readonly record struct Reference(string Name, Location At);

    /// <summary>Result of a <see cref="Walk" />: the reachable set plus how each was reached.</summary>
    internal sealed class Reachability(
        Dictionary<IMethodSymbol, CallGraph.Edge?> discoveredBy,
        Dictionary<IMethodSymbol, List<Reference>> references,
        HashSet<string> cutPointsHit)
    {
        public int Count => discoveredBy.Count;

        public IEnumerable<IMethodSymbol> Methods => discoveredBy.Keys;

        /// <summary>Cut-point patterns the walk actually stopped at. Used to detect stale ones.</summary>
        public IReadOnlySet<string> CutPointsHit => cutPointsHit;

        public IReadOnlyList<Reference> ReferencesFrom(IMethodSymbol method) =>
            references.TryGetValue(method, out List<Reference>? refs) ? refs : [];

        /// <summary>
        ///     Call path from a root down to <paramref name="method" />, root first. This is what
        ///     makes a failure actionable — the name of the offending method alone rarely says how
        ///     the movement path got there.
        /// </summary>
        public string DescribePath(IMethodSymbol method)
        {
            List<string> path = [];
            IMethodSymbol? current = method;

            while (current is not null && discoveredBy.TryGetValue(current, out CallGraph.Edge? edge))
            {
                path.Add(edge is null
                    ? $"{NameOf(current)}  [root]"
                    : $"{NameOf(current)}  ({edge.Value.Kind}{Describe(edge.Value.At)})");

                current = edge?.From;
            }

            path.Reverse();
            return string.Join($"{Environment.NewLine}    -> ", path);
        }

        private static string Describe(Location? location)
        {
            if (location is null || location == Location.None)
            {
                return string.Empty;
            }

            FileLinePositionSpan span = location.GetLineSpan();
            return $" at {Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}";
        }
    }
}
