using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OmniBlock.Generators;

/// <summary>
///     Emits the serialization half of every <c>[WireMessage]</c> type, plus the registration list
///     for them.
///     <para>
///         <c>docs/network-rewrite.md</c> §6: "keep the hand-rolled binary writing — it is fast and
///         allocation-free — stop writing it by hand". The generated bodies do exactly what the
///         hand-written ones did, call for call. What changes is that <c>Read</c>, <c>Write</c> and
///         <c>Size</c> come from one traversal of one declaration, so the two ways they used to go
///         wrong are gone: a field written but not read, and a size that does not match the bytes.
///     </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MessageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(
            static ctx => ctx.AddSource("WireAttributes.g.cs", WireAttributeSource.Source));

        IncrementalValuesProvider<(MessageModel? Model, EquatableArray<DiagnosticInfo> Diagnostics)> results =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                WireAttributeSource.MessageAttributeName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => Build(ctx));

        context.RegisterSourceOutput(
            results,
            static (ctx, result) =>
            {
                foreach (DiagnosticInfo info in result.Diagnostics)
                {
                    ctx.ReportDiagnostic(info.ToDiagnostic());
                }

                if (result.Model is { } model)
                {
                    ctx.AddSource($"{model.FullName}.g.cs", Emit(model));
                }
            });

        context.RegisterSourceOutput(
            results.Select(static (result, _) => result.Model).Where(static m => m is not null).Collect(),
            static (ctx, models) => ctx.AddSource("GeneratedMessages.g.cs", EmitRegistrations(models)));
    }

    private static (MessageModel?, EquatableArray<DiagnosticInfo>) Build(GeneratorAttributeSyntaxContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.TargetSymbol;
        var declaration = (ClassDeclarationSyntax)ctx.TargetNode;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        if (!declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(DiagnosticInfo.Create(WireDiagnostics.MustBePartial, type, type.Name));
        }

        if (type.ContainingType is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(WireDiagnostics.MustBeTopLevel, type, type.Name));
        }

        if (!InheritsMessage(type))
        {
            diagnostics.Add(DiagnosticInfo.Create(WireDiagnostics.MustDeriveFromMessage, type, type.Name));
        }

        AttributeData attribute = ctx.Attributes[0];
        string key = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string k
            ? k
            : string.Empty;
        int version = 1;

        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == "Version" && named.Value.Value is int v)
            {
                version = v;
            }
        }

        int separator = key.IndexOf(':');
        if (separator <= 0 || separator == key.Length - 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(WireDiagnostics.MalformedKey, type, key));
            return (null, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
        }

        var fields = ImmutableArray.CreateBuilder<FieldModel>();

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IPropertySymbol property)
            {
                continue;
            }

            AttributeData? fieldAttribute = property.GetAttributes().FirstOrDefault(
                a => a.AttributeClass?.ToDisplayString() == WireAttributeSource.FieldAttributeName);

            if (fieldAttribute is null)
            {
                continue;
            }

            if (property.SetMethod is null || property.IsStatic)
            {
                diagnostics.Add(DiagnosticInfo.Create(WireDiagnostics.FieldNeedsSetter, property, property.Name));
                continue;
            }

            int encoding = WireCodec.EncodingFixed;
            int maxLength = 0;

            foreach (KeyValuePair<string, TypedConstant> named in fieldAttribute.NamedArguments)
            {
                if (named.Key == "Encoding" && named.Value.Value is int e)
                {
                    encoding = e;
                }
                else if (named.Key == "MaxLength" && named.Value.Value is int max)
                {
                    maxLength = max;
                }
            }

            if (WireCodec.Describe(property.Type, encoding, maxLength, property.Name) is not { } snippets)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    WireDiagnostics.UnsupportedFieldType,
                    property,
                    property.Type.ToDisplayString(),
                    property.Name));
                continue;
            }

            fields.Add(new FieldModel(property.Name, snippets.Read, snippets.Write, snippets.Size));
        }

        var model = new MessageModel(
            type.ContainingNamespace.ToDisplayString(),
            type.Name,
            type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            key.Substring(0, separator),
            key.Substring(separator + 1),
            version,
            new EquatableArray<FieldModel>(fields.ToImmutable()));

        return (model, new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    private static bool InheritsMessage(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "OmniBlock.Network.Messages.Message")
            {
                return true;
            }
        }

        return false;
    }

    private static string Emit(MessageModel model)
    {
        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("using OmniBlock;");
        source.AppendLine();
        source.AppendLine($"namespace {model.Namespace};");
        source.AppendLine();
        source.AppendLine($"partial class {model.TypeName}");
        source.AppendLine("{");

        // The identity is emitted rather than hand-written so that the key in the attribute is the
        // single place it is stated. Two spellings of one name is how a peer ends up silently
        // dropping a message it does in fact implement.
        source.AppendLine("    /// <summary>Stable identity. Generated from the <c>[WireMessage]</c> key.</summary>");
        source.AppendLine(
            $"    public static readonly global::OmniBlock.ResourceLocation Id = "
            + $"new(global::OmniBlock.Namespace.Get(\"{model.KeyNamespace}\"), \"{model.KeyPath}\");");
        source.AppendLine();
        source.AppendLine("    public override global::OmniBlock.ResourceLocation Key => Id;");
        source.AppendLine();
        source.AppendLine($"    public override int SchemaVersion => {model.Version};");
        source.AppendLine();

        if (model.Fields.Count == 0)
        {
            source.AppendLine("    /// <summary>No payload: this message's arrival is the whole content.</summary>");
        }
        else
        {
            source.Append("    /// <summary>Wire order: ");
            for (int i = 0; i < model.Fields.Count; i++)
            {
                source.Append(i == 0 ? string.Empty : ", ").Append(model.Fields[i].Name);
            }

            source.AppendLine(".</summary>");
        }

        source.AppendLine("    public override void Read(global::System.IO.Stream stream)");
        source.AppendLine("    {");
        foreach (FieldModel field in model.Fields)
        {
            source.AppendLine($"        {field.Name} = {field.Read};");
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public override void Write(global::System.IO.Stream stream)");
        source.AppendLine("    {");
        foreach (FieldModel field in model.Fields)
        {
            source.AppendLine($"        {field.Write}");
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public override int Size()");
        source.AppendLine("    {");

        if (model.Fields.Count == 0)
        {
            source.AppendLine("        return 0;");
        }
        else
        {
            source.AppendLine("        return");
            for (int i = 0; i < model.Fields.Count; i++)
            {
                source.Append("            ")
                    .Append(i == 0 ? string.Empty : "+ ")
                    .Append(model.Fields[i].Size)
                    .AppendLine(i == model.Fields.Count - 1 ? ";" : string.Empty);
            }
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>
    ///     Emits the list of every generated message in the compilation.
    ///     <para>
    ///         A hand-maintained registration list is the failure <c>DefaultMessages</c> warns
    ///         about, one step removed: a message type that exists but was never registered is a
    ///         feature that quietly does not work, on whichever side forgot. Deriving the list from
    ///         the declarations makes forgetting impossible.
    ///     </para>
    /// </summary>
    private static string EmitRegistrations(ImmutableArray<MessageModel?> models)
    {
        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace OmniBlock.Network.Messages;");
        source.AppendLine();
        source.AppendLine("/// <summary>Every <c>[WireMessage]</c> type declared in this assembly.</summary>");
        source.AppendLine("internal static class GeneratedMessages");
        source.AppendLine("{");
        source.AppendLine("    public static void RegisterAll(MessageRegistry registry)");
        source.AppendLine("    {");

        // Sorted so the emitted file is stable against the order the pipeline happened to visit
        // declarations in. The wire IDs do not depend on this — the registry sorts keys itself — but
        // a generated file that reshuffles between builds makes every diff of it noise.
        foreach (MessageModel model in models
            .Where(m => m is not null)
            .Select(m => m!)
            .OrderBy(m => m.FullName, StringComparer.Ordinal))
        {
            source.AppendLine(
                $"        registry.Register(global::{model.FullName}.Id, {model.Version}, "
                + $"static () => new global::{model.FullName}());");
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }
}
