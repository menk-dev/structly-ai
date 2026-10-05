using Microsoft.CodeAnalysis;
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

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_invalid];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
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

    internal static bool ValidSchemaName(string name) => Regex.IsMatch(name, "\\A[A-Za-z0-9_-]{1,64}\\z");

}
