using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;

namespace Structly.AI.Tests;

public sealed class ProviderRegistrationTests
{
    static readonly AiProviderDescriptor<FakeClient, FakeOptions> _descriptor = new("Fake", new FakeFactory());

    [Fact]
    public async Task Fake_provider_executes_tasks_and_bound_output_with_callback_precedence()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Structly:Fake:Value"] = "bound",
        }).Build();
        var services = new ServiceCollection();
        var task = StructuredTask.Create<Answer>("Extract");
        services.AddStructlyAi(configuration, builder =>
        {
            builder.ConfigureProvider(_descriptor, options => options.Value = "callback");
            builder.AddTask("task", task).AddTask("bound", task.BindOutput());
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<FakeClient>(scope.ServiceProvider.GetRequiredService<IStructuredClient>());
        Assert.NotSame(scope.ServiceProvider.GetRequiredService<FakeClient>(), scope.ServiceProvider.GetRequiredService<FakeClient>());
        var ai = provider.GetRequiredService<StructlyAi>();
        foreach(var name in new[] { "task", "bound" })
            Assert.Equal("callback", (await ai.ExecuteTaskAsync<Answer>(name, "input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);
    }

    [Fact]
    public void Registration_requires_one_provider_and_freezes_selection()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddStructlyAi(_ => { }));
        Assert.Throws<InvalidOperationException>(() => services.AddStructlyAi(builder =>
        {
            builder.ConfigureProvider(_descriptor);
            builder.ConfigureProvider(_descriptor);
        }));
        StructlyAiBuilder? retained = null;
        services.AddStructlyAi(builder =>
        {
            retained = builder;
            builder.ConfigureProvider(_descriptor);
        });
        Assert.Throws<InvalidOperationException>(() => retained!.ConfigureProvider(_descriptor));
        Assert.Throws<InvalidOperationException>(() => services.AddStructlyAi(builder => builder.ConfigureProvider(_descriptor)));
    }

    [Fact]
    public void Request_cloning_and_execution_defaults_preserve_provider_settings()
    {
        var request = new OpenAiRequest { Input = "input", OpenAi = new() { Store = true, CaptureOutputText = true } };
        var cloned = request with { CorrelationId = "clone" };
        var applied = new TaskExecutionDefaults { MaxOutputTokens = 30 }.Apply(cloned);
        var advanced = Assert.IsType<OpenAiRequest>(applied);
        Assert.True(advanced.OpenAi.Store);
        Assert.True(advanced.OpenAi.CaptureOutputText);
        Assert.Equal(30, advanced.MaxOutputTokens);
        Assert.Equal("clone", advanced.CorrelationId);
    }

    [Fact]
    public void Factory_validation_does_not_resolve_credentials()
    {
        var options = new OpenAiOptions
        {
            DefaultModel = new() { ModelId = "offline" },
            CredentialResolver = _ => throw new InvalidOperationException("Credentials must not be read during validation."),
        };
        new OpenAiClientFactory().Validate(options);
    }

    public sealed record Answer(string Value);
    public sealed class FakeOptions
    {
        public string Value { get; set; } = "default";
    }
    sealed class FakeFactory : IStructuredClientFactory<FakeClient, FakeOptions>
    {
        public void Validate(FakeOptions options) { }
        public FakeClient Create(HttpClient http, FakeOptions options) => new(options.Value);
    }
    sealed class FakeClient(string value) : IStructuredClient
    {
        public Task<StructuredResult<T>> ExecuteAsync<T>(StructuredTask<T> task, StructuredRequest request, CancellationToken cancellationToken = default)
            => ExecuteAsync(task.BindOutput(), request, cancellationToken);
        public Task<StructuredResult<T>> ExecuteAsync<T>(BoundOutput<T> output, StructuredRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(output.ReadOutput(System.Text.Json.JsonSerializer.Serialize(new { value })));
    }
}
