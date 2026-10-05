using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Structly.AI.Analyzers;

sealed class ContractValidator(System.Threading.CancellationToken cancellationToken, bool preserve)
{
    const string _json = "System.Text.Json.Serialization.";
    const string _schema = "Structly.AI.";
    readonly HashSet<ITypeSymbol> _ancestors = new(SymbolEqualityComparer.Default);
    int _properties;
    int _enums;
    int _characters;

    public Issue? Validate(ITypeSymbol type)
    {
        try
        {
            Visit(type, "$", 0);
            if(!SymbolEqualityComparer.Default.Equals(Unwrap(type), type) || Kind(type) != "Object")
                Fail("$", "Root", "Use a concrete object DTO as the non-null output root.");

            return null;
        }
        catch(Issue issue)
        {
            return issue;
        }
    }

    void Visit(ITypeSymbol declared, string path, int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = Unwrap(declared);
        RejectOverrides(type, path);
        var kind = Kind(type);
        if(kind is "Object" or "Array" && ++depth > 10)
            Fail(path, "DepthLimit", "Strict schemas support at most 10 object/array nesting levels.");

        Description(type, path);
        if(kind is "String" or "Boolean" or "Integer" or "Number")
            return;

        if(kind == "Enum")
        {
            if(Attribute(type, "System.FlagsAttribute") is not null)
                Fail(path, "EnumFlags", "Use a non-flags enum with unique values.");

            var fields = type.GetMembers().OfType<IFieldSymbol>().Where(x => x.HasConstantValue).ToArray();
            _enums += fields.Length;
            if(_enums > 1000)
                Fail(path, "EnumLimit", "Strict schemas support at most 1,000 emitted enum entries.");

            if(fields.Length == 0 || fields.Select(x => x.ConstantValue).Distinct().Count() != fields.Length)
                Fail(path, "EnumValues", "Enums must be nonempty without numeric aliases.");

            var names = fields.Select(x => Argument(Attribute(x, _json + "JsonStringEnumMemberNameAttribute")) as string ?? x.Name).ToArray();
            if(names.Any(String.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != fields.Length)
                Fail(path, "EnumNames", "Enum wire names must be nonblank and ordinally unique.");

            var characters = 0;
            foreach(var name in names)
            {
                characters += CountString(name, path);
                if(fields.Length > 250 && characters > 15000)
                    Fail(path, "EnumStringLimit", "Enums with more than 250 values support at most 15,000 string characters.");
            }

            return;
        }

        if(kind == "Array")
        {
            var item = CollectionItem(type, path);
            if(!_ancestors.Add(type))
                Fail(path, "Cycle", "Recursive collections cannot produce a finite inline schema.");

            Visit(item, path + "[]", depth);
            _ancestors.Remove(type);
            return;
        }

        if(type is not INamedTypeSymbol named || type.SpecialType == SpecialType.System_Object
            || type.TypeKind is TypeKind.Interface or TypeKind.Delegate || type.IsAbstract
            || type.SpecialType != SpecialType.None && type.SpecialType != SpecialType.System_Nullable_T
            || FullName(type) is "System.Text.Json.JsonElement" or "System.TimeSpan" or "System.TimeOnly" or "System.Uri"
            || IsOrInherits(type, "System.Text.Json.Nodes.JsonNode") || named.IsTupleType
            || FullName(type).StartsWith("System.Tuple", StringComparison.Ordinal)
            || FullName(type).StartsWith("System.ValueTuple", StringComparison.Ordinal)
            || Interfaces(type).Any(x => FullName(x.OriginalDefinition) == "System.Collections.Generic.IAsyncEnumerable<T>"))
        {
            Fail(path, "UnsupportedType", "Use a concrete DTO or a supported scalar/collection.");
            return;
        }

        if(!_ancestors.Add(type))
            Fail(path, "Cycle", "Recursive DTOs cannot produce a finite inline schema.");

        foreach(var field in Hierarchy(named).SelectMany(x => x.GetMembers().OfType<IFieldSymbol>()).Where(x => !x.IsStatic
            && (SymbolEqualityComparer.Default.Equals(x.ContainingType, named) || x.DeclaredAccessibility != Accessibility.Private)))
            if(!Ignored(field) && (Attribute(field, _json + "JsonIncludeAttribute") is not null || Attribute(field, _json + "JsonExtensionDataAttribute") is not null))
                Fail(path, "FieldInclusion", "Fields are excluded; use a public serializable property instead.");

        var properties = new List<IPropertySymbol>();
        foreach(var property in Hierarchy(named).SelectMany(x => x.GetMembers().OfType<IPropertySymbol>()).Where(x => !x.IsStatic))
        {
            if(Ignored(property))
                continue;

            if(property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
            {
                if(Attribute(property, _json + "JsonIncludeAttribute") is not null)
                    Fail(path, "MemberAccess", "Nonpublic included properties are unsupported.");

                continue;
            }

            if(properties.Any(x => Overrides(x, property)))
                continue;

            properties.Add(property);
        }

        var constructors = named.InstanceConstructors;
        var attributed = constructors.Where(x => Attribute(x, _json + "JsonConstructorAttribute") is not null).ToArray();
        var publicConstructors = constructors.Where(x => x.DeclaredAccessibility == Accessibility.Public).ToArray();
        IMethodSymbol? constructor = null;
        if(attributed.Length > 1)
            Fail(path, "Constructor", "Only one JsonConstructor is allowed.");

        if(attributed.Length == 1)
            constructor = attributed[0];
        else if(!type.IsValueType)
        {
            constructor = publicConstructors.FirstOrDefault(x => x.Parameters.Length == 0);
            if(constructor is null && publicConstructors.Length == 1)
                constructor = publicConstructors[0];

            if(constructor is null)
                Fail(path, "Constructor", "Use a public parameterless constructor, one public constructor, or JsonConstructor.");
        }

        var bound = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        foreach(var parameter in constructor?.Parameters ?? [])
        {
            var candidates = properties.Where(x => String.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                && SymbolEqualityComparer.Default.Equals(x.Type, parameter.Type)).ToArray();
            if(candidates.Length != 1)
                Fail(path, "ConstructorBinding", "Every constructor parameter must bind to one included property of the same CLR name and type.");

            bound.Add(candidates[0]);
        }

        var namesSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach(var property in properties.OrderBy(x => Argument(Attribute(x, _json + "JsonPropertyOrderAttribute")) as int? ?? 0)
            .ThenBy(Name, StringComparer.Ordinal))
        {
            if(++_properties > 5000)
                Fail(path, "PropertyLimit", "Strict schemas support at most 5,000 emitted object properties.");

            var name = Name(property);
            var memberPath = path + "." + name;
            if(String.IsNullOrWhiteSpace(name) || !namesSeen.Add(name))
                Fail(memberPath, "MemberName", "Serialized property names must be nonblank and unique, including inherited members.");

            CountString(name, memberPath);
            if(property.IsIndexer)
                Fail(memberPath, "Indexer", "Indexers are unsupported; use a list property.");

            if(property.SetMethod is not null && property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                Fail(memberPath, "MemberAccess", "Use a public setter/init or a constructor-bound get-only property.");

            if(property.SetMethod is null && !bound.Contains(property))
                Fail(memberPath, "ComputedProperty", "Get-only properties must bind to the selected JSON constructor.");

            var ignore = Attribute(property, _json + "JsonIgnoreAttribute");
            if(ignore is not null && Named(ignore, "Condition", 1) != 0)
                Fail(memberPath, "ConditionalIgnore", "Strict required contracts only support JsonIgnore Always or Never.");

            RejectOverrides(property, memberPath);
            Visit(property.Type, memberPath, depth);
            Description(property, memberPath);
            Constraints(property, memberPath);
        }

        if(properties.Count == 0)
            Fail(path, "EmptyObject", "Object DTOs must contain at least one supported property.");

        _ancestors.Remove(type);
    }

    ITypeSymbol CollectionItem(ITypeSymbol type, string path)
    {
        if(type is IArrayTypeSymbol array)
        {
            if(array.Rank != 1 || array.ElementType.SpecialType == SpecialType.System_Byte)
                Fail(path, "Collection", "Use a one-dimensional supported collection; byte[] serializes as base64.");

            return array.ElementType;
        }

        var interfaces = Interfaces(type).ToArray();
        var contracts = interfaces.Where(x => FullName(x.OriginalDefinition) == "System.Collections.Generic.IEnumerable<T>")
            .Select(x => x.TypeArguments[0]).Distinct(SymbolEqualityComparer.Default).ToArray();
        if(contracts.Length != 1 || interfaces.Any(x => FullName(x.OriginalDefinition) is "System.Collections.IDictionary"
            or "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>"))
            Fail(path, "Collection", "Dictionaries and ambiguous/non-generic collections are unsupported; use a list DTO.");

        var standard = FullName(((INamedTypeSymbol)type).OriginalDefinition) is "System.Collections.Generic.List<T>"
            or "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IList<T>"
            or "System.Collections.Generic.ICollection<T>" or "System.Collections.Generic.IEnumerable<T>"
            or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.ISet<T>";
        if(!standard)
        {
            if(type.IsAbstract || type.TypeKind == TypeKind.Interface
                || !interfaces.Any(x => FullName(x.OriginalDefinition) == "System.Collections.Generic.ICollection<T>"
                    && SymbolEqualityComparer.Default.Equals(x.TypeArguments[0], contracts[0]))
                || !((INamedTypeSymbol)type).InstanceConstructors.Any(x => x.DeclaredAccessibility == Accessibility.Public && x.Parameters.Length == 0))
                Fail(path, "CollectionConstruction", "Custom collections require one IEnumerable<T>, ICollection<T> and a public parameterless constructor.");

            if(Hierarchy((INamedTypeSymbol)type).SelectMany(x => x.GetMembers().OfType<IPropertySymbol>()).Any(x => Attribute(x, _json + "JsonIncludeAttribute") is not null))
                Fail(path, "SerializationOverride", "Custom collection inclusion overrides are unsupported.");
        }

        return (ITypeSymbol)contracts[0]!;
    }

    void Constraints(IPropertySymbol property, string path)
    {
        var type = Unwrap(property.Type);
        var kind = Kind(type);
        if(Attribute(property, _schema + "DynamicVocabularyAttribute") is { } vocabulary)
        {
            var target = kind == "Array" ? Unwrap(CollectionItem(type, path)) : type;
            if(target.SpecialType != SpecialType.System_String || String.IsNullOrWhiteSpace(Argument(vocabulary) as string))
                Fail(path, "VocabularyTarget", "DynamicVocabulary requires a nonblank set name and a string or string collection.");
        }

        if(Attribute(property, _schema + "StringConstraintAttribute") is { } text)
        {
            if(type.SpecialType != SpecialType.System_String)
                Fail(path, "ConstraintTarget", "StringConstraint requires a string property.");

            Bounds(Named(text, "MinLength", -1), Named(text, "MaxLength", -1), path);
            var format = Named<string?>(text, "Format", null);
            if(format is not null && format is not ("date-time" or "time" or "date" or "duration" or "email" or "hostname" or "ipv4" or "ipv6" or "uuid"))
                Fail(path, "Format", "Use a supported JSON Schema format.");

            var pattern = Named<string?>(text, "Pattern", null);
            if(pattern is not null)
            {
                if(String.IsNullOrWhiteSpace(pattern) || pattern.Length > 4096 || pattern.Any(x => x > 127)
                    || pattern.Contains('(') || pattern.Contains(')') || pattern.Contains("-[") || Regex.IsMatch(pattern, @"\\[A-Za-z0-9]"))
                    Fail(path, "Pattern", "Use an ASCII portable pattern; groups, shorthand classes and backreferences are unsupported.");

                try
                {
                    _ = new Regex(pattern, RegexOptions.ECMAScript | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                }
                catch(ArgumentException)
                {
                    Fail(path, "Pattern", "Pattern must be a valid portable regular expression.");
                }
            }
        }

        if(Attribute(property, _schema + "NumberConstraintAttribute") is { } number)
        {
            if(kind is not ("Integer" or "Number"))
                Fail(path, "ConstraintTarget", "NumberConstraint requires a numeric property.");

            var min = Named(number, "Minimum", Double.NaN);
            var max = Named(number, "Maximum", Double.NaN);
            if(Double.IsInfinity(min) || Double.IsInfinity(max) || !Double.IsNaN(min) && !Double.IsNaN(max) && min > max)
                Fail(path, "NumberBounds", "Numeric bounds must be finite and minimum must not exceed maximum.");
        }

        if(Attribute(property, _schema + "CollectionConstraintAttribute") is { } collection)
        {
            if(kind != "Array")
                Fail(path, "ConstraintTarget", "CollectionConstraint requires a collection property.");

            Bounds(Named(collection, "MinItems", -1), Named(collection, "MaxItems", -1), path);
        }
    }

    static void Bounds(int min, int max, string path)
    {
        if(min < -1 || max < -1 || min >= 0 && max >= 0 && min > max)
            Fail(path, "Bounds", "Bounds must be nonnegative (or -1 for unspecified), with minimum no greater than maximum.");
    }

    static void Description(ISymbol symbol, string path)
    {
        var schema = Attribute(symbol, _schema + "SchemaAttribute");
        var name = Named<string?>(schema, "Name", null);
        if(name is not null && !StructuredSchemaAnalyzer.ValidSchemaName(name))
            Fail(path, "SchemaName", "Schema names require 1–64 ASCII letters, digits, underscores or hyphens.");

        var description = Named<string?>(schema, "Description", null) ?? Argument(Attribute(symbol, "System.ComponentModel.DescriptionAttribute")) as string;
        if(description is not null && String.IsNullOrWhiteSpace(description))
            Fail(path, "Description", "Supplied descriptions must be nonblank.");
    }

    static void RejectOverrides(ISymbol symbol, string path)
    {
        var converter = Attribute(symbol, _json + "JsonConverterAttribute");
        var type = symbol is IPropertySymbol property ? property.Type : symbol as ITypeSymbol;
        if(type is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            type = nullable.TypeArguments[0];

        var converterType = Argument(converter) as INamedTypeSymbol;
        var supported = type?.TypeKind == TypeKind.Enum && converterType is not null &&
            converterType.ContainingNamespace.ToDisplayString() == "System.Text.Json.Serialization" &&
            converterType.Name == "JsonStringEnumConverter" && (converterType.Arity == 0 ||
            converterType.Arity == 1 && SymbolEqualityComparer.Default.Equals(converterType.TypeArguments[0], type));
        if(converter is not null && !supported)
            Fail(path, "SerializationOverride", "Only default built-in string-enum converters on enum types or properties are supported.");

        if(new[] { "JsonExtensionDataAttribute", "JsonIncludeAttribute", "JsonPolymorphicAttribute",
            "JsonDerivedTypeAttribute", "JsonNumberHandlingAttribute", "JsonObjectCreationHandlingAttribute", }.Any(x => Attribute(symbol, _json + x) is not null))
            Fail(path, "SerializationOverride", "Custom converters, inclusion, extension data, polymorphism, number handling and population overrides are unsupported.");
    }

    int CountString(string value, string path)
    {
        var count = 0;
        for(var i = 0; i < value.Length; i++, count++)
            if(Char.IsHighSurrogate(value[i]) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]))
                i++;

        _characters += count;
        if(_characters > 120000)
            Fail(path, "StringLimit", "Strict schemas support at most 120,000 relevant string characters.");

        return count;
    }

    string Name(IPropertySymbol property)
        => Argument(Attribute(property, _json + "JsonPropertyNameAttribute")) as string
            ?? (preserve ? property.MetadataName : CamelCase(property.MetadataName));

    static string CamelCase(string name)
    {
        if(name.Length == 0 || !Char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();
        for(var i = 0; i < chars.Length; i++)
        {
            if(i == 1 && !Char.IsUpper(chars[i]))
                break;

            var next = i + 1 < chars.Length;
            if(i > 0 && next && !Char.IsUpper(chars[i + 1]))
            {
                if(chars[i + 1] == ' ')
                    chars[i] = Char.ToLowerInvariant(chars[i]);

                break;
            }

            chars[i] = Char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    static string Kind(ITypeSymbol type)
    {
        if(type.SpecialType == SpecialType.System_String || FullName(type) is "System.Guid" or "System.DateOnly" or "System.DateTime" or "System.DateTimeOffset")
            return "String";

        if(type.SpecialType == SpecialType.System_Boolean)
            return "Boolean";

        if(type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64)
            return "Integer";

        if(type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal)
            return "Number";

        if(type.TypeKind == TypeKind.Enum)
            return "Enum";

        if(type is IArrayTypeSymbol || Interfaces(type).Any(x => FullName(x) == "System.Collections.IEnumerable"))
            return "Array";

        return "Object";
    }

    static string FullName(ITypeSymbol type) => type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
    static ITypeSymbol Unwrap(ITypeSymbol type) => type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] : type;
    static IEnumerable<INamedTypeSymbol> Interfaces(ITypeSymbol type) => type is INamedTypeSymbol named ? named.AllInterfaces.Prepend(named) : [];
    static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol type)
    {
        for(var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            yield return current;
    }
    static bool IsOrInherits(ITypeSymbol type, string name) => type is INamedTypeSymbol named && Hierarchy(named).Any(x => FullName(x) == name);
    static bool Overrides(IPropertySymbol derived, IPropertySymbol property)
    {
        for(var current = derived; current is not null; current = current.OverriddenProperty)
            if(SymbolEqualityComparer.Default.Equals(current, property))
                return true;

        return false;
    }
    static bool Ignored(ISymbol symbol) => Attribute(symbol, _json + "JsonIgnoreAttribute") is { } ignore && Named(ignore, "Condition", 1) == 1;
    static AttributeData? Attribute(ISymbol symbol, string name)
    {
        for(ISymbol? current = symbol; current is not null; current = current switch
        {
            INamedTypeSymbol type => type.BaseType,
            IPropertySymbol property => property.OverriddenProperty,
            _ => null,
        })
        {
            var found = current.GetAttributes().FirstOrDefault(x => x.AttributeClass?.ToDisplayString() == name);
            if(found is not null)
                return found;
        }

        return null;
    }
    static object? Argument(AttributeData? attribute) => attribute?.ConstructorArguments.FirstOrDefault().Value;
    static T Named<T>(AttributeData? attribute, string name, T fallback)
        => attribute?.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value is T value ? value : fallback;
    static void Fail(string path, string code, string message) => throw new Issue(path, code, message);
}
