using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Structly.AI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StructuredSchemaAnalyzer : DiagnosticAnalyzer
{
    static readonly DiagnosticDescriptor _invalid = new("STAI001", "Invalid structured output contract",
        "{0}: {1} — {2}", "Structly.AI.Schema", DiagnosticSeverity.Error, true,
        description: "Use a concrete, constructible DTO supported by the strict schema contract.",
        helpLinkUri: "https://github.com/menk-dev/structly-ai/blob/main/docs/SCHEMAS.md");
    static readonly DiagnosticDescriptor _legacy = new("STAI002", "Legacy schema attribute",
        "{0}: use {1} with Structly.AI", "Structly.AI.Schema", DiagnosticSeverity.Warning, true,
        description: "Replace legacy StructuredLlm schema attributes with the supported contract attributes.",
        helpLinkUri: "https://github.com/menk-dev/structly-ai/blob/main/docs/SCHEMAS.md");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_invalid, _legacy];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSyntaxNodeAction(AnalyzeLegacyAttribute, SyntaxKind.Attribute);
    }

    static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        ITypeSymbol? type = null;
        bool? preserve = null;
        if(method.ContainingType.ToDisplayString() == "Structly.AI.StructuredTask" && method.Name == "Create" && method.TypeArguments.Length == 1)
        {
            type = method.TypeArguments[0];
            // Only inline options establish a naming policy statically. Runtime settings remain authoritative.
            if(UnwrapOperation(invocation.Arguments.FirstOrDefault()?.Value) is IObjectCreationOperation options)
            {
                preserve = false;
                var serialization = Assignment(options, "SerializationProfile");
                if(serialization is IObjectCreationOperation profile)
                {
                    var naming = Assignment(profile, "Naming");
                    preserve = naming is null ? false : naming.ConstantValue.HasValue && naming.ConstantValue.Value is int value
                        ? value == 1 : null;
                }
                else if(serialization is not null)
                    preserve = null;

                foreach(var name in new[] { "SchemaName", "Description" })
                {
                    var setting = Assignment(options, name);
                    if(setting?.ConstantValue.HasValue == true && setting.ConstantValue.Value is string text
                        && (name == "SchemaName" ? !ValidSchemaName(text) : String.IsNullOrWhiteSpace(text)))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_invalid, setting.Syntax.GetLocation(), "$", name,
                            name == "SchemaName" ? "Use 1–64 ASCII letters, digits, underscores or hyphens." : "Descriptions must be nonblank."));
                        return;
                    }
                }
            }
        }
        else if(method.ContainingType.OriginalDefinition.ToDisplayString() == "Structly.AI.StructuredTask<T>"
            && method.Name is "CreateSchema" or "CreateOutputSpecification" or "ReadOutput")
            type = method.ContainingType.TypeArguments[0];
        else if(method.ContainingType.ToDisplayString() == "Structly.AI.OpenAI.OpenAiClient"
            && method.Name is "ExecuteAsync" or "PrewarmAsync" && method.TypeArguments.Length == 1)
            type = method.TypeArguments[0];

        if(type is null || ContainsUnresolvedType(type))
            return;

        var issue = new ContractValidator(context.CancellationToken, preserve ?? false).Validate(type);
        if(issue is null)
            return;

        if(preserve is null)
        {
            var alternative = new ContractValidator(context.CancellationToken, true).Validate(type);
            if(alternative is null || alternative.Code != issue.Code)
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(_invalid, invocation.Syntax.GetLocation(), issue.Path, issue.Code, issue.Message));
    }

    static IOperation? Assignment(IObjectCreationOperation creation, string name)
        => UnwrapOperation(creation.Initializer?.Initializers.OfType<ISimpleAssignmentOperation>()
            .FirstOrDefault(x => x.Target is IPropertyReferenceOperation property && property.Property.Name == name)?.Value);

    static IOperation? UnwrapOperation(IOperation? operation)
        => operation is IConversionOperation conversion ? UnwrapOperation(conversion.Operand) : operation;

    static bool ContainsUnresolvedType(ITypeSymbol type)
        => type.TypeKind is TypeKind.TypeParameter or TypeKind.Error
            || type is IArrayTypeSymbol array && ContainsUnresolvedType(array.ElementType)
            || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsUnresolvedType);

    static void AnalyzeLegacyAttribute(SyntaxNodeAnalysisContext context)
    {
        var syntax = (AttributeSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(syntax, context.CancellationToken).Symbol as IMethodSymbol;
        var name = symbol?.ContainingType.Name ?? syntax.Name.ToString().Split('.').Last();
        if(name.EndsWith("Attribute", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - 9);

        // Match known legacy names, including unresolved attributes during migration; unrelated names are ignored.
        var replacement = name switch
        {
            "StructuredLlmRequired" => "required C# members (all output properties are required)",
            "StructuredLlmName" => "JsonPropertyNameAttribute",
            "StructuredLlmIgnore" => "JsonIgnoreAttribute",
            "StructuredLlmNullable" => "nullable types, MaybeNullAttribute or AllowNullAttribute",
            "StructuredLlmStringLength" => "StringConstraintAttribute",
            "StructuredLlmArrayLength" => "CollectionConstraintAttribute",
            "StructuredLlmNumberRange" => "NumberConstraintAttribute",
            "StructuredLlmEnumName" => "JsonStringEnumMemberNameAttribute",
            "StructuredLlmDescription" => "DescriptionAttribute or SchemaAttribute",
            _ => null,
        };
        if(replacement is not null)
            context.ReportDiagnostic(Diagnostic.Create(_legacy, syntax.GetLocation(), name, replacement));
    }

    internal static bool ValidSchemaName(string name) => Regex.IsMatch(name, "\\A[A-Za-z0-9_-]{1,64}\\z");

}
