using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Structly.AI;

enum SchemaKind { Object, Array, String, Boolean, Integer, Number, Enum }

sealed record SchemaMember(string Name, SchemaNode Node);

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

static class SchemaResolver
{
    static readonly HashSet<Type> _integerTypes = [typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong)];
    static readonly HashSet<Type> _numberTypes = [typeof(float), typeof(double), typeof(decimal)];
    static readonly HashSet<Type> _collectionTypes = [typeof(List<>), typeof(IReadOnlyList<>), typeof(IList<>), typeof(ICollection<>), typeof(IEnumerable<>), typeof(IReadOnlyCollection<>), typeof(HashSet<>), typeof(ISet<>)];

    public static SchemaNode Resolve(Type type, JsonSerializerOptions serializer)
    {
        var node = Build(type, null, false, "$", [], serializer, 0, new ResolutionBudget());
        if (node.Kind != SchemaKind.Object || Nullable.GetUnderlyingType(type) is not null)
            Fail("$", "Root", "Use a concrete object DTO as the non-null output root.");
        return node;
    }

    static SchemaNode Build(Type declaredType, NullabilityInfo? nullability, bool nullable, string path,
        HashSet<Type> ancestors, JsonSerializerOptions serializer, int depth, ResolutionBudget budget)
    {
        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        nullable |= Nullable.GetUnderlyingType(declaredType) is not null;
        if (!type.IsValueType && nullability is not null)
            nullable |= nullability.ReadState != NullabilityState.NotNull || nullability.WriteState == NullabilityState.Nullable;
        RejectOverrides(type, path);
        SchemaKind kind;
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(DateOnly) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
            kind = SchemaKind.String;
        else if (type == typeof(bool)) kind = SchemaKind.Boolean;
        else if (_integerTypes.Contains(type)) kind = SchemaKind.Integer;
        else if (_numberTypes.Contains(type)) kind = SchemaKind.Number;
        else if (type.IsEnum) kind = SchemaKind.Enum;
        else if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type)) kind = SchemaKind.Array;
        else kind = SchemaKind.Object;
        if (kind is SchemaKind.Object or SchemaKind.Array && ++depth > 10)
            Fail(path, "DepthLimit", "Strict schemas support at most 10 object/array nesting levels.");
        var node = new SchemaNode(type, kind, nullable);
        node.Description = GetDescription(type, path);
        if (kind == SchemaKind.String)
        {
            node.Format = type == typeof(Guid) ? "uuid" : type == typeof(DateOnly) ? "date"
                : type == typeof(DateTime) || type == typeof(DateTimeOffset) ? "date-time" : null;
            return node;
        }
        if (kind is SchemaKind.Boolean or SchemaKind.Integer or SchemaKind.Number) return node;
        if (kind == SchemaKind.Enum)
        {
            if (type.IsDefined(typeof(FlagsAttribute))) Fail(path, "EnumFlags", "Use a non-flags enum with unique values.");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(x => x.MetadataToken).ToArray();
            budget.Enums += fields.Length;
            if (budget.Enums > 1000)
                Fail(path, "EnumLimit", "Strict schemas support at most 1,000 emitted enum entries.");
            var values = fields.Select(x => x.GetRawConstantValue()).ToArray();
            if (fields.Length == 0 || values.Distinct().Count() != fields.Length)
                Fail(path, "EnumValues", "Enums must be nonempty without numeric aliases.");
            node.EnumValues = fields.Select(x => x.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? x.Name)
                .ToArray();
            if (node.EnumValues.Any(String.IsNullOrWhiteSpace) || node.EnumValues.Distinct(StringComparer.Ordinal).Count() != fields.Length)
                Fail(path, "EnumNames", "Enum wire names must be nonblank and ordinally unique.");
            var enumCharacters = 0;
            foreach (var name in node.EnumValues)
            {
                enumCharacters += budget.CountString(name, path);
                if (fields.Length > 250 && enumCharacters > 15000)
                    Fail(path, "EnumStringLimit", "Enums with more than 250 values support at most 15,000 string characters.");
            }
            return node;
        }
        if (kind == SchemaKind.Array)
        {
            if (type == typeof(byte[]) || type.IsArray && type.GetArrayRank() != 1)
                Fail(path, "Collection", "Use a one-dimensional supported collection; byte[] serializes as base64.");
            var contracts = type.GetInterfaces().Append(type).Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(x => x.GetGenericArguments()[0]).Distinct().ToArray();
            if (contracts.Length != 1 || type.ContainsGenericParameters || typeof(IDictionary).IsAssignableFrom(type)
                || type.GetInterfaces().Append(type).Any(x => x.IsGenericType && (x.GetGenericTypeDefinition() == typeof(IDictionary<,>) || x.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))))
                Fail(path, "Collection", "Dictionaries and ambiguous/non-generic collections are unsupported; use a list DTO.");
            var itemType = type.IsArray ? type.GetElementType()! : contracts[0];
            if (!type.IsArray && !(type.IsGenericType && _collectionTypes.Contains(type.GetGenericTypeDefinition())))
            {
                var population = typeof(ICollection<>).MakeGenericType(itemType);
                if (type.IsAbstract || type.IsInterface || !population.IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) is null
                    || serializer.GetTypeInfo(type).CreateObject is null)
                    Fail(path, "CollectionConstruction", "Custom collections require one IEnumerable<T>, ICollection<T> and a public parameterless constructor supported by System.Text.Json.");
                if (type.GetProperties().Any(x => x.IsDefined(typeof(JsonIncludeAttribute))))
                    Fail(path, "SerializationOverride", "Custom collection inclusion overrides are unsupported.");
            }
            node.IsSet = typeof(ISet<>).MakeGenericType(itemType).IsAssignableFrom(type);
            var itemNullability = type.IsArray ? nullability?.ElementType : nullability?.GenericTypeArguments.FirstOrDefault();
            if (!ancestors.Add(type)) Fail(path, "Cycle", "Recursive collections cannot produce a finite inline schema.");
            node.Item = Build(itemType, itemNullability, !itemType.IsValueType && itemNullability is null, path + "[]", ancestors, serializer, depth, budget);
            ancestors.Remove(type);
            return node;
        }
        if (type == typeof(object) || type == typeof(JsonElement) || typeof(JsonNode).IsAssignableFrom(type)
            || type.IsPrimitive || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || typeof(Delegate).IsAssignableFrom(type)
            || type == typeof(TimeSpan) || type == typeof(TimeOnly) || type == typeof(Uri)
            || type.FullName?.StartsWith("System.Tuple", StringComparison.Ordinal) == true
            || type.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true
            || type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)))
            Fail(path, "UnsupportedType", $"{type.Name} is unsupported; use a concrete DTO or a supported scalar/collection.");
        if (!ancestors.Add(type)) Fail(path, "Cycle", "Recursive DTOs cannot produce a finite inline schema.");
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            if (field.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always
                && (field.IsDefined(typeof(JsonIncludeAttribute)) || field.IsDefined(typeof(JsonExtensionDataAttribute))))
                Fail(path, "FieldInclusion", "Fields are excluded; use a public serializable property instead.");
        var properties = new List<PropertyInfo>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(x => x.MetadataToken))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always) continue;
                if (property.GetMethod?.IsPublic != true)
                {
                    if (property.IsDefined(typeof(JsonIncludeAttribute))) Fail(path, "MemberAccess", "Nonpublic included properties are unsupported.");
                    continue;
                }
                if (properties.Any(p => p.GetMethod!.GetBaseDefinition() == property.GetMethod.GetBaseDefinition())) continue;
                properties.Add(property);
            }
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var attributed = constructors.Where(x => x.IsDefined(typeof(JsonConstructorAttribute))).ToArray();
        var publicConstructors = constructors.Where(x => x.IsPublic).ToArray();
        ConstructorInfo? constructor = null;
        if (attributed.Length > 1) Fail(path, "Constructor", "Only one JsonConstructor is allowed.");
        if (attributed.Length == 1) constructor = attributed[0];
        else if (!type.IsValueType)
        {
            constructor = publicConstructors.FirstOrDefault(x => x.GetParameters().Length == 0);
            if (constructor is null && publicConstructors.Length == 1) constructor = publicConstructors[0];
            if (constructor is null) Fail(path, "Constructor", "Use a public parameterless constructor, one public constructor, or JsonConstructor.");
        }
        var bound = new HashSet<PropertyInfo>();
        foreach (var parameter in constructor?.GetParameters() ?? [])
        {
            var candidates = properties.Where(x => String.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) && x.PropertyType == parameter.ParameterType).ToArray();
            if (candidates.Length != 1) Fail(path, "ConstructorBinding", "Every constructor parameter must bind to one included property of the same CLR name and type.");
            bound.Add(candidates[0]);
        }
        var context = new NullabilityInfoContext();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in properties.OrderBy(x => x.GetCustomAttribute<JsonPropertyOrderAttribute>()?.Order ?? 0))
        {
            if (++budget.Properties > 5000)
                Fail(path, "PropertyLimit", "Strict schemas support at most 5,000 emitted object properties.");
            var name = Name(property, serializer);
            var memberPath = path + "." + name;
            if (String.IsNullOrWhiteSpace(name) || !names.Add(name)) Fail(memberPath, "MemberName", "Serialized property names must be nonblank and unique, including inherited members.");
            budget.CountString(name, memberPath);
            if (property.GetIndexParameters().Length > 0) Fail(memberPath, "Indexer", "Indexers are unsupported; use a list property.");
            if (property.SetMethod is not null && !property.SetMethod.IsPublic)
                Fail(memberPath, "MemberAccess", "Use a public setter/init or a constructor-bound get-only property.");
            if (property.SetMethod is null && !bound.Contains(property))
                Fail(memberPath, "ComputedProperty", "Get-only properties must bind to the selected JSON constructor.");
            var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
            if (ignore is not null && ignore.Condition != JsonIgnoreCondition.Never)
                Fail(memberPath, "ConditionalIgnore", "Strict required contracts only support JsonIgnore Always or Never.");
            RejectOverrides(property, memberPath);
            var info = context.Create(property);
            var child = Build(property.PropertyType, info, !property.PropertyType.IsValueType && (property.IsDefined(typeof(MaybeNullAttribute)) || property.IsDefined(typeof(AllowNullAttribute))),
                memberPath, ancestors, serializer, depth, budget);
            child.Description = GetDescription(property, memberPath) ?? child.Description;
            ApplyConstraints(child, property, memberPath);
            node.Members.Add(new(name, child));
        }
        if (node.Members.Count == 0) Fail(path, "EmptyObject", "Object DTOs must contain at least one supported property.");
        ancestors.Remove(type);
        return node;
    }

    static string Name(PropertyInfo property, JsonSerializerOptions serializer)
        => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? serializer.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;

    static string? GetDescription(MemberInfo member, string path)
    {
        var schema = member.GetCustomAttribute<SchemaAttribute>();
        if (schema?.Name is not null && !Regex.IsMatch(schema.Name, "\\A[A-Za-z0-9_-]{1,64}\\z"))
            Fail(path, "SchemaName", "SchemaAttribute.Name must contain 1–64 ASCII letters, digits, underscores or hyphens.");
        var description = schema?.Description ?? member.GetCustomAttribute<DescriptionAttribute>()?.Description;
        if (description is not null && String.IsNullOrWhiteSpace(description)) Fail(path, "Description", "Supplied descriptions must be nonblank.");
        return description;
    }

    static void RejectOverrides(MemberInfo member, string path)
    {
        var converter = member.GetCustomAttribute<JsonConverterAttribute>();
        var declaredType = member is PropertyInfo property ? property.PropertyType : member as Type;
        var enumType = declaredType is null ? null : Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var converterType = converter?.ConverterType;
        if (converter is not null && !(converter.GetType() == typeof(JsonConverterAttribute) && enumType?.IsEnum == true &&
            (converterType == typeof(JsonStringEnumConverter) || converterType == typeof(JsonStringEnumConverter<>).MakeGenericType(enumType))))
            Fail(path, "SerializationOverride", "Only default built-in string-enum converters on enum types or properties are supported.");
        Type[] rejected = [typeof(JsonExtensionDataAttribute), typeof(JsonIncludeAttribute),
            typeof(JsonPolymorphicAttribute), typeof(JsonDerivedTypeAttribute), typeof(JsonNumberHandlingAttribute), typeof(JsonObjectCreationHandlingAttribute)];
        if (rejected.Any(x => member.IsDefined(x, true)))
            Fail(path, "SerializationOverride", "Custom converters, inclusion, extension data, polymorphism, number handling and population overrides are unsupported.");
    }

    static void ApplyConstraints(SchemaNode node, PropertyInfo property, string path)
    {
        if (property.GetCustomAttribute<DynamicVocabularyAttribute>() is { } vocabulary)
        {
            var target = node.Kind == SchemaKind.Array ? node.Item! : node;
            if (target.Type != typeof(string) || String.IsNullOrWhiteSpace(vocabulary.SetName))
                Fail(path, "VocabularyTarget", "DynamicVocabulary requires a nonblank set name and a string or string collection.");
            target.Vocabulary = vocabulary.SetName;
        }
        if (property.GetCustomAttribute<StringConstraintAttribute>() is { } text)
        {
            if (node.Type != typeof(string)) Fail(path, "ConstraintTarget", "StringConstraint requires a string property.");
            CheckBounds(text.MinLength, text.MaxLength, path);
            node.MinLength = text.MinLength;
            node.MaxLength = text.MaxLength;
            if (text.Format is not null && !FormatRules.Formats.Contains(text.Format))
                Fail(path, "Format", "Use a supported format: date-time, time, date, duration, email, hostname, ipv4, ipv6 or uuid.");
            node.Format = text.Format;
            if (text.Pattern is not null)
            {
                // Deliberately small portable subset: no groups/extensions, backreferences,
                // shorthand/Unicode classes or escaped anchors; only literal escapes.
                if (String.IsNullOrWhiteSpace(text.Pattern) || text.Pattern.Length > 4096
                    || text.Pattern.Any(x => x > 127) || text.Pattern.Contains('(') || text.Pattern.Contains(')') || text.Pattern.Contains("-[", StringComparison.Ordinal)
                    || Regex.IsMatch(text.Pattern, @"\\[A-Za-z0-9]"))
                    Fail(path, "Pattern", "Use an ASCII portable pattern with character classes, literal escapes, alternation and quantifiers; groups, shorthand classes and backreferences are unsupported.");
                try { node.Regex = new Regex(text.Pattern, RegexOptions.ECMAScript | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { Fail(path, "Pattern", "Pattern must be a valid portable regular expression."); }
                node.Pattern = text.Pattern;
            }
        }
        if (property.GetCustomAttribute<NumberConstraintAttribute>() is { } number)
        {
            if (node.Kind is not SchemaKind.Integer and not SchemaKind.Number) Fail(path, "ConstraintTarget", "NumberConstraint requires a numeric property.");
            if (Double.IsInfinity(number.Minimum) || Double.IsInfinity(number.Maximum)
                || !Double.IsNaN(number.Minimum) && !Double.IsNaN(number.Maximum) && number.Minimum > number.Maximum)
                Fail(path, "NumberBounds", "Numeric bounds must be finite and minimum must not exceed maximum.");
            node.Minimum = number.Minimum;
            node.Maximum = number.Maximum;
        }
        if (property.GetCustomAttribute<CollectionConstraintAttribute>() is { } collection)
        {
            if (node.Kind != SchemaKind.Array) Fail(path, "ConstraintTarget", "CollectionConstraint requires a collection property.");
            CheckBounds(collection.MinItems, collection.MaxItems, path);
            node.MinItems = collection.MinItems;
            node.MaxItems = collection.MaxItems;
        }
    }

    static void CheckBounds(int min, int max, string path)
    {
        if (min < -1 || max < -1 || min >= 0 && max >= 0 && min > max)
            Fail(path, "Bounds", "Bounds must be nonnegative (or -1 for unspecified), with minimum no greater than maximum.");
    }

    [DoesNotReturn]
    public static void Fail(string path, string code, string message)
        => throw new StructuredSchemaException([new(path, code, message)]);

    sealed class ResolutionBudget
    {
        public int Properties;
        public int Enums;
        int _characters;

        public int CountString(string value, string path)
        {
            var count = SchemaWriter.ScalarCount(value, 120000);
            _characters += count;
            if (_characters > 120000)
                Fail(path, "StringLimit", "Strict schemas support at most 120,000 relevant string characters.");
            return count;
        }
    }
}
