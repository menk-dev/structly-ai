using System.Linq.Expressions;
using System.Reflection;

namespace Structly.AI.OpenAI;

/// <summary>Maps each answer property to a question, with instructions and allowed values supplied by application code.</summary>
public sealed class DecisionQuestionBuilder<T>
{
    readonly DecisionContract _contract;
    readonly Dictionary<PropertyInfo, DecisionQuestion> _questions = [];

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

    DecisionQuestionBuilder<T> Add<TAnswer>(Expression<Func<T, TAnswer>> selector, string instructions,
        IReadOnlyList<DecisionOption<object>>? choices = null, IReadOnlyList<DecisionLevel>? levels = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if(selector.Body is not MemberExpression { Member: PropertyInfo property } member || member.Expression != selector.Parameters[0] ||
            !_contract.Properties.TryGetValue(property, out var jsonName))
            throw new ArgumentException("Select one direct, included answer property.", nameof(selector));

        var question = DecisionQuestion.Create(property.PropertyType, property.Name, jsonName, instructions, choices, levels);
        if(!_questions.TryAdd(property, question))
            throw new ArgumentException("Each answer property must have exactly one question.", nameof(selector));

        return this;
    }

    internal DecisionQuestion[] Complete()
    {
        if(_questions.Count != _contract.Properties.Count)
            throw new ArgumentException("Define exactly one question for every answer property.");

        return _questions.Values.ToArray();
    }
}
