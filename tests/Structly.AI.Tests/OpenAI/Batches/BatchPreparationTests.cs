using static Structly.AI.Tests.BatchTestSupport;

namespace Structly.AI.Tests;

public sealed class BatchPreparationTests
{
    [Fact]
    public void PreparationRejectsDuplicateModelsAndTransportControls()
    {
        using var http = new HttpClient();
        var client = Client(http);
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), Item("a")]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([Item("a"), new("b", Item("b").Request with { ModelSelection = new() { ModelId = "other" } })]));
        Assert.Throws<ArgumentException>(() => client.PrepareEmbeddingBatch([new("a", Item("a").Request with { TotalTimeout = TimeSpan.FromSeconds(1) })]));
    }

}
