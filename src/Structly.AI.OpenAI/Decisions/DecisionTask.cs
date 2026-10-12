namespace Structly.AI.OpenAI;

/// <summary>Creates immutable decision contracts without transport or credentials.</summary>
public static class DecisionTask
{
    /// <summary>Creates a flat DTO task, requiring one question per included answer property.</summary>
    public static DecisionTask<T> Create<T>(Action<DecisionQuestionBuilder<T>> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var contract = DecisionContract.Create(typeof(T));
        if(DecisionQuestion.IsAnswer(typeof(T)))
            throw new ArgumentException("Use a scalar task factory for a scalar answer.", nameof(questions));

        var builder = new DecisionQuestionBuilder<T>(contract);
        questions(builder);
        return new(contract, builder.Complete());
    }

    /// <summary>Creates a scalar predicate or choice task. Bool and enum choices are inferred.</summary>
    public static DecisionTask<T> Create<T>(string instructions) => Scalar<T>(instructions);

    /// <summary>Creates a scalar choice task with inferred bool or enum choices.</summary>
    public static DecisionTask<DecisionChoice<T>> CreateChoice<T>(string instructions) => Scalar<DecisionChoice<T>>(instructions);

    /// <summary>Creates a scalar choice task with explicit allowed values.</summary>
    public static DecisionTask<DecisionChoice<T>> CreateChoice<T>(string instructions, IReadOnlyList<T> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return Scalar<DecisionChoice<T>>(instructions, choices.Select(x => new DecisionOption<object>(x!)).ToArray());
    }

    /// <summary>Creates a scalar choice task with explicit values and descriptions.</summary>
    public static DecisionTask<DecisionChoice<T>> CreateChoice<T>(string instructions, IReadOnlyList<DecisionOption<T>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return Scalar<DecisionChoice<T>>(instructions, choices.Select(x => x is null ? null! : new DecisionOption<object>(x.Value!, x.Description)).ToArray());
    }

    /// <summary>Creates a scalar score task with levels ordered from lowest to highest.</summary>
    public static DecisionTask<DecisionScore> CreateScore(string instructions, IReadOnlyList<DecisionLevel> levels)
        => Scalar<DecisionScore>(instructions, levels: levels ?? throw new ArgumentNullException(nameof(levels)));

    static DecisionTask<T> Scalar<T>(string instructions, IReadOnlyList<DecisionOption<object>>? choices = null,
        IReadOnlyList<DecisionLevel>? levels = null)
    {
        if(!DecisionQuestion.IsAnswer(typeof(T)))
            throw new ArgumentException("Use the question builder for a DTO contract.");

        var contract = DecisionContract.Create(typeof(T));
        return new(contract, [DecisionQuestion.Create(typeof(T), "answer", "answer", instructions, choices, levels)]);
    }
}
