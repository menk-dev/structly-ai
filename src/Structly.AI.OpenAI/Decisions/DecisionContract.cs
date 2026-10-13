using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Structly.AI.OpenAI;

sealed class DecisionContract
{
    public required JsonSerializerOptions Serializer { get; init; }
    public required IReadOnlyDictionary<PropertyInfo, string> Properties { get; init; }

    static bool HasNullableArgument(NullabilityInfo info) => info.GenericTypeArguments.Any(x =>
        x.ReadState == NullabilityState.Nullable || HasNullableArgument(x));

    public static DecisionContract Create(Type type)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            foreach(var property in info.Properties)
                property.IsRequired = false;
        });
        var serializer = new JsonSerializerOptions
        {
            TypeInfoResolver = resolver,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16,
        };
        serializer.Converters.Add(new DecisionSetJsonConverter());
        serializer.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        serializer.MakeReadOnly();
        var properties = new Dictionary<PropertyInfo, string>();
        if(DecisionQuestion.IsAnswer(type))
            return new() { Serializer = serializer, Properties = properties };

        if(type.IsAbstract || type.IsInterface || type.IsValueType || type.IsDefined(typeof(JsonConverterAttribute)) ||
            type.IsDefined(typeof(JsonPolymorphicAttribute)) || type.IsDefined(typeof(JsonDerivedTypeAttribute)))
            throw new ArgumentException("Decision contracts must be concrete, flat DTOs or scalar answers.");

        foreach(var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if(field.IsDefined(typeof(JsonIncludeAttribute)) || field.IsDefined(typeof(JsonExtensionDataAttribute)))
                throw new ArgumentException("Decision contracts do not include fields.");

        var nullability = new NullabilityInfoContext();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var wireNames = new HashSet<string>(StringComparer.Ordinal);
        var info = serializer.GetTypeInfo(type);
        foreach(var member in info.Properties)
        {
            if(member.AttributeProvider is not PropertyInfo property)
                throw new ArgumentException("Decision contracts require properties.");

            if(property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always)
                continue;

            if(property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0 ||
                property.SetMethod is { IsPublic: false } || property.IsDefined(typeof(JsonConverterAttribute)) ||
                property.IsDefined(typeof(JsonExtensionDataAttribute)) || !(DecisionQuestion.IsAnswer(property.PropertyType) || DecisionQuestion.IsSet(property.PropertyType)) ||
                nullability.Create(property).ReadState == NullabilityState.Nullable ||
                HasNullableArgument(nullability.Create(property)))
                throw new ArgumentException("Use nonnullable public decision answer properties with public setters/init or constructor binding.");

            if(property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: not JsonIgnoreCondition.Never })
                throw new ArgumentException("Conditional property ignoring is unsupported.");

            if(String.IsNullOrWhiteSpace(member.Name) || !names.Add(member.Name) || !wireNames.Add(property.Name))
                throw new ArgumentException("Property names must be nonblank and unique.");

            properties.Add(property, member.Name);
        }

        if(properties.Count == 0)
            throw new ArgumentException("Decision contracts must have at least one answer property.");

        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var attributed = constructors.Where(x => x.IsDefined(typeof(JsonConstructorAttribute))).ToArray();
        var available = constructors.Where(x => x.IsPublic).ToArray();
        if(attributed.Length > 1)
            throw new ArgumentException("Use only one JSON constructor.");

        var constructor = attributed.SingleOrDefault() ?? available.FirstOrDefault(x => x.GetParameters().Length == 0)
            ?? (available.Length == 1 ? available[0] : null);
        if(constructor is null)
            throw new ArgumentException("Use a public parameterless constructor, one public constructor, or JsonConstructor.");

        var bound = new HashSet<PropertyInfo>();
        foreach(var parameter in constructor.GetParameters())
        {
            var candidates = properties.Keys.Where(x => String.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                x.PropertyType == parameter.ParameterType).ToArray();
            if(candidates.Length != 1)
                throw new ArgumentException("Constructor parameters must bind to answer properties by CLR name and type.");

            bound.Add(candidates[0]);
        }

        if(properties.Keys.Any(x => x.SetMethod is null && !bound.Contains(x)))
            throw new ArgumentException("Get-only answers must bind to the selected JSON constructor.");

        return new() { Serializer = serializer, Properties = properties };
    }
}
