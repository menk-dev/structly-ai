using System.Linq.Expressions;
using System.Reflection;

namespace Structly.AI.OpenAI;

/// <summary>Maps each answer property to a question, with instructions and allowed values supplied by application code.</summary>
public sealed class DecisionQuestionBuilder<T>
{
    readonly DecisionContract _contract;
    readonly Dictionary<PropertyInfo, DecisionQuestion[]> _questions = [];

    internal DecisionQuestionBuilder(DecisionContract contract) => _contract = contract;

    /// <summary>Asks a predicate question.</summary>
    public DecisionQuestionBuilder<T> Ask(Expression<Func<T, DecisionPredicate>> property, string instructions)
        => Add(property, instructions);

    /// <summary>Asks a choice question with inferred bool or enum choices. Enum descriptions explain the options.</summary>
    public DecisionQuestionBuilder<T> Ask<TChoice>(Expression<Func<T, DecisionChoice<TChoice>>> property, string instructions)
        => Add(property, instructions);

    /// <summary>Asks a choice question with an explicit set of allowed values.</summary>
    public DecisionQuestionBuilder<T> Ask<TChoice>(Expression<Func<T, DecisionChoice<TChoice>>> property, string instructions,
        IReadOnlyList<TChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return Add(property, instructions, choices.Select(x => new DecisionOption<object>(x!)).ToArray());
    }

    /// <summary>Asks a choice question with explicit values and descriptions.</summary>
    public DecisionQuestionBuilder<T> Ask<TChoice>(Expression<Func<T, DecisionChoice<TChoice>>> property, string instructions,
        IReadOnlyList<DecisionOption<TChoice>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return Add(property, instructions, choices.Select(x => x is null ? null! : new DecisionOption<object>(x.Value!, x.Description)).ToArray());
    }

    /// <summary>Asks a score question with rubric levels ordered from lowest to highest.</summary>
    public DecisionQuestionBuilder<T> Ask(Expression<Func<T, DecisionScore>> property, string instructions, IReadOnlyList<DecisionLevel> levels)
        => Add(property, instructions, levels: levels ?? throw new ArgumentNullException(nameof(levels)));

    /// <summary>Asks one predicate question per identifier and instruction.</summary>
    public DecisionQuestionBuilder<T> AskEach(Expression<Func<T, DecisionSet<DecisionPredicate>>> property,
        IReadOnlyDictionary<string, string> questions) => AddEach(property, questions);

    /// <summary>Asks one choice question per identifier, with inferred bool or enum choices.</summary>
    public DecisionQuestionBuilder<T> AskEach<TChoice>(Expression<Func<T, DecisionSet<DecisionChoice<TChoice>>>> property,
        IReadOnlyDictionary<string, string> questions) => AddEach(property, questions);

    /// <summary>Asks choice questions sharing explicit allowed values.</summary>
    public DecisionQuestionBuilder<T> AskEach<TChoice>(Expression<Func<T, DecisionSet<DecisionChoice<TChoice>>>> property,
        IReadOnlyDictionary<string, string> questions, IReadOnlyList<TChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return AddEach(property, questions, choices.Select(x => new DecisionOption<object>(x!)).ToArray());
    }

    /// <summary>Asks choice questions sharing explicit values and descriptions.</summary>
    public DecisionQuestionBuilder<T> AskEach<TChoice>(Expression<Func<T, DecisionSet<DecisionChoice<TChoice>>>> property,
        IReadOnlyDictionary<string, string> questions, IReadOnlyList<DecisionOption<TChoice>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return AddEach(property, questions, choices.Select(x => x is null ? null! : new DecisionOption<object>(x.Value!, x.Description)).ToArray());
    }

    /// <summary>Asks score questions sharing ordered rubric levels.</summary>
    public DecisionQuestionBuilder<T> AskEach(Expression<Func<T, DecisionSet<DecisionScore>>> property,
        IReadOnlyDictionary<string, string> questions, IReadOnlyList<DecisionLevel> levels)
        => AddEach(property, questions, levels: levels ?? throw new ArgumentNullException(nameof(levels)));

    DecisionQuestionBuilder<T> AddEach<TAnswer>(Expression<Func<T, DecisionSet<TAnswer>>> selector,
        IReadOnlyDictionary<string, string> questions, IReadOnlyList<DecisionOption<object>>? choices = null,
        IReadOnlyList<DecisionLevel>? levels = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(questions);
        if(selector.Body is not MemberExpression { Member: PropertyInfo property } member || member.Expression != selector.Parameters[0] ||
            !_contract.Properties.TryGetValue(property, out var jsonName))
            throw new ArgumentException("Select one direct, included answer property.", nameof(selector));

        // Validate the answer configuration even when no questions are supplied.
        _ = DecisionQuestion.Create(typeof(TAnswer), property.Name, jsonName, "Validate answer configuration", choices, levels);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var snapshot = questions.Select((entry, index) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            if(!keys.Add(entry.Key))
                throw new ArgumentException("Question identifiers must be unique.", nameof(questions));

            // A separator invalid in CLR property names keeps collection wire names distinct from fixed questions.
            return DecisionQuestion.Create(typeof(TAnswer), $"{property.Name}:{index}", jsonName, entry.Value, choices, levels, entry.Key);
        }).ToArray();
        if(!_questions.TryAdd(property, snapshot))
            throw new ArgumentException("Each answer property must have exactly one question definition.", nameof(selector));

        return this;
    }

    DecisionQuestionBuilder<T> Add<TAnswer>(Expression<Func<T, TAnswer>> selector, string instructions,
        IReadOnlyList<DecisionOption<object>>? choices = null, IReadOnlyList<DecisionLevel>? levels = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if(selector.Body is not MemberExpression { Member: PropertyInfo property } member || member.Expression != selector.Parameters[0] ||
            !_contract.Properties.TryGetValue(property, out var jsonName))
            throw new ArgumentException("Select one direct, included answer property.", nameof(selector));

        var question = DecisionQuestion.Create(property.PropertyType, property.Name, jsonName, instructions, choices, levels);
        if(!_questions.TryAdd(property, [question]))
            throw new ArgumentException("Each answer property must have exactly one question.", nameof(selector));

        return this;
    }

    internal DecisionQuestion[] Complete()
    {
        if(_questions.Count != _contract.Properties.Count)
            throw new ArgumentException("Define exactly one question for every answer property.");

        return _questions.Values.SelectMany(x => x).ToArray();
    }
}
