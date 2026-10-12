using Structly.AI.OpenAI;
using System.Text.Json.Serialization;
using static Structly.AI.Tests.DecisionTestSupport;

namespace Structly.AI.Tests;

public sealed class DecisionTaskTests
{
    [Fact]
    public void EveryPropertyMustHaveExactlyOneDirectQuestion()
    {
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<TicketDecision>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateDto>(q => q
            .Ask(x => x.Answer, "first").Ask(x => x.Answer, "second")));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateDto>(q => q.Ask(x => new DecisionPredicate(1), "computed")));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateDto>(q => q.Ask(x => x.Answer, " ")));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionPredicate>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<PredicateDto>("question"));
    }

    [Fact]
    public void UnsupportedContractsFailAtCreation()
    {
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<NullableDto>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<NestedDto>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<ComputedDto>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<NullableChoiceDto>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<ConditionalDto>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<ConstructorMismatch>(_ => { }));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<EmptyDto>(_ => { }));
    }

    [Fact]
    public void ChoicesAreFiniteUniqueTypedValues()
    {
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionChoice<string>>("choose"));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionChoice<int>>("choose"));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionChoice<Alias>>("choose"));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionChoice<WireAlias>>("choose"));
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionChoice<Flags>>("choose"));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<string>("choose", ["one"]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<string>("choose", ["one", "one"]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<string>("choose", ["one", " "]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<string>("choose", Enumerable.Range(0, 256).Select(x => x.ToString()).ToArray()));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<Department>("choose", [Department.Billing, (Department)999]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateChoice<bool>("choose", [true, true]));
    }

    [Fact]
    public void ScoresRequireAnOrderedRubric()
    {
        Assert.Throws<ArgumentException>(() => DecisionTask.Create<DecisionScore>("score"));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateScore("score", []));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateScore("score", [new("one")]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateScore("score", [new("one"), new("one")]));
        Assert.Throws<ArgumentException>(() => DecisionTask.CreateScore("score", [new("one"), new(" ")]));
    }

    [Fact]
    public void DescribedChoicesAndInheritedAnswerPropertiesCanBeConfigured()
    {
        _ = DecisionTask.CreateChoice<string>("choose", [new DecisionOption<string>("first", "a"), new("second", "b")]);
        _ = DecisionTask.Create<InheritedDto>(q => q.Ask(x => x.Answer, "condition"));
        _ = DecisionTask.Create<ChoiceDto>(q => q.Ask(x => x.Answer, "choose",
            choices: [new DecisionOption<string>("first", "a"), new("second", "b")]));
    }

    public sealed record PredicateDto
    {
        public required DecisionPredicate Answer { get; init; }
    }
    public sealed record NullableDto
    {
        public DecisionPredicate? Answer { get; init; }
    }
    public sealed record NullableChoiceDto
    {
        public required DecisionChoice<string?> Answer { get; init; }
    }
    public sealed record NestedDto
    {
        public required PredicateDto Nested { get; init; }
    }
    public sealed record ComputedDto
    {
        public DecisionPredicate Answer => new(0);
    }
    public sealed record ConditionalDto
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public required DecisionPredicate Answer { get; init; }
    }
    public sealed class ConstructorMismatch(string other)
    {
        public DecisionPredicate Answer { get; } = new(other.Length);
    }
    public sealed record EmptyDto;
    public record BaseDto
    {
        public required DecisionPredicate Answer { get; init; }
    }
    public sealed record InheritedDto : BaseDto;
    public sealed record ChoiceDto
    {
        public required DecisionChoice<string> Answer { get; init; }
    }
    public enum Alias { First, Second = First }
    public enum WireAlias
    {
        [JsonStringEnumMemberName("same")]
        First,
        [JsonStringEnumMemberName("same")]
        Second,
    }
    [Flags]
    public enum Flags { First = 1, Second = 2, }
}
