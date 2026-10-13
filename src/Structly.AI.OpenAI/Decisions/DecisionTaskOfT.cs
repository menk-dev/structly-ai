using System.Text.Json;

namespace Structly.AI.OpenAI;

/// <summary>An immutable decision task. Instructions, choices and levels are validated and copied at creation.</summary>
public sealed class DecisionTask<T>
{
    readonly DecisionContract _contract;
    readonly DecisionQuestion[] _questions;

    internal DecisionTask(DecisionContract contract, DecisionQuestion[] questions)
    {
        _contract = contract;
        _questions = questions;
    }

    internal object[] Questions() => _questions.Select(x => (object)x.Payload()).ToArray();

    internal StructuredResult<T> Read(JsonElement root, StructuredMetadata metadata, IReadOnlyList<StructuredWarning> warnings)
    {
        StructuredResult<T> Fail(StructuredErrorKind kind) => StructuredResult<T>.Failure(new()
        {
            Kind = kind,
            Message = $"Decision operation failed: {kind}.",
        }, metadata, warnings);

        if(root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("model", out var model) ||
            model.ValueKind != JsonValueKind.String || String.IsNullOrWhiteSpace(model.GetString()) ||
            !root.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Array || answers.GetArrayLength() != _questions.Length)
            return Fail(StructuredErrorKind.InvalidResponse);

        var byName = _questions.ToDictionary(x => x.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new Dictionary<string, object?>();
        var collectionValues = _contract.Properties.Where(x => DecisionQuestion.IsSet(x.Key.PropertyType))
            .ToDictionary(x => x.Value, _ => new List<object>());
        var readAnswers = new Dictionary<string, object>();
        var refused = false;
        foreach(var answer in answers.EnumerateArray())
        {
            if(answer.ValueKind != JsonValueKind.Object || answer.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != answer.EnumerateObject().Count() ||
                !answer.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !byName.TryGetValue(name.GetString()!, out var question) || !seen.Add(question.Name) ||
                !answer.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return Fail(StructuredErrorKind.InvalidResponse);

            if(type.GetString() == "refusal")
            {
                refused = true;
                continue;
            }

            if(type.GetString() != question.Kind || !question.TryRead(answer, out var value))
                return Fail(StructuredErrorKind.InvalidResponse);

            readAnswers[question.Name] = value;
        }

        if(refused)
            return Fail(StructuredErrorKind.Refused);

        foreach(var question in _questions)
        {
            if(question.Key is { } key)
                collectionValues[question.JsonName].Add(new Dictionary<string, object?> { ["Key"] = key, ["Answer"] = readAnswers[question.Name] });
            else
                values[question.JsonName] = readAnswers[question.Name];
        }

        foreach(var collection in collectionValues)
            values[collection.Key] = collection.Value;

        try
        {
            object output = _contract.Properties.Count == 0 ? values["answer"]! : values;
            var value = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(output), _contract.Serializer);
            return value is null ? Fail(StructuredErrorKind.InvalidOutput) : StructuredResult<T>.Success(value, metadata, warnings);
        }
        catch(Exception exception) when(exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            return Fail(StructuredErrorKind.InvalidOutput);
        }
    }
}
