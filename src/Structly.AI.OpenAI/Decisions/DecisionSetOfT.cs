using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Structly.AI.OpenAI;

/// <summary>An immutable collection of named answers, enumerated in task question order.</summary>
public sealed class DecisionSet<TAnswer> : IReadOnlyCollection<DecisionEntry<TAnswer>>
{
    readonly DecisionEntry<TAnswer>[] _entries;
    readonly Dictionary<string, TAnswer> _answers;

    internal DecisionSet(DecisionEntry<TAnswer>[] entries)
    {
        _entries = entries.ToArray();
        _answers = _entries.ToDictionary(x => x.Key, x => x.Answer, StringComparer.Ordinal);
    }

    /// <summary>Gets the number of answers.</summary>
    public int Count => _entries.Length;

    /// <summary>Gets an answer by its exact, case-sensitive identifier.</summary>
    public TAnswer this[string key] => _answers[key];

    /// <summary>Looks up an answer by its exact, case-sensitive identifier.</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TAnswer answer) => _answers.TryGetValue(key, out answer);

    /// <summary>Enumerates named answers in task question order.</summary>
    public IEnumerator<DecisionEntry<TAnswer>> GetEnumerator() => ((IEnumerable<DecisionEntry<TAnswer>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
