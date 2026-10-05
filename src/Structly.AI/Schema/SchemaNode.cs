using System.Text.RegularExpressions;

namespace Structly.AI;

sealed class SchemaNode(Type type, SchemaKind kind, bool nullable)
{
    public Type Type { get; } = type;
    public SchemaKind Kind { get; } = kind;
    public bool Nullable { get; } = nullable;
    public string? Description { get; set; }
    public string? Format { get; set; }
    public string[]? EnumValues { get; set; }
    public string? Vocabulary { get; set; }
    public SchemaNode? Item { get; set; }
    public bool IsSet { get; set; }
    public List<SchemaMember> Members { get; } = [];
    public int MinLength { get; set; } = -1;
    public int MaxLength { get; set; } = -1;
    public string? Pattern { get; set; }
    public Regex? Regex { get; set; }
    public double Minimum { get; set; } = Double.NaN;
    public double Maximum { get; set; } = Double.NaN;
    public int MinItems { get; set; } = -1;
    public int MaxItems { get; set; } = -1;
}
