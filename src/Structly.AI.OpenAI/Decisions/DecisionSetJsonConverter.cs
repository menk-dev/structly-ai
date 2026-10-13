using System.Text.Json;
using System.Text.Json.Serialization;

namespace Structly.AI.OpenAI;

sealed class DecisionSetJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => DecisionQuestion.IsSet(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    sealed class Converter<TAnswer> : JsonConverter<DecisionSet<TAnswer>>
    {
        public override DecisionSet<TAnswer> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<DecisionEntry<TAnswer>[]>(ref reader, options) ?? throw new JsonException());

        public override void Write(Utf8JsonWriter writer, DecisionSet<TAnswer> value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
