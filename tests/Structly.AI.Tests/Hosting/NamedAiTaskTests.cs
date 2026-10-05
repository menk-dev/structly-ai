using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Structly.AI.Hosting;
using Structly.AI.OpenAI;
using Structly.AI.Testing;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Structly.AI.Tests;

public sealed class NamedAiTaskTests
{
    [Fact]
    public async Task Typed_references_share_named_registration_and_infer_output_types()
    {
        var reference = new AiTaskReference<Answer>("typed");
        var existing = new AiTaskReference<Answer>("existing");
        var bound = new AiTaskReference<Answer>("bound");
        var simple = new AiTaskReference<Answer>("simple");
        var bodies = new List<JsonElement>();
        var services = new ServiceCollection();

        var task = StructuredTask.Create<Answer>("Extract");
        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider(options =>
            {
                options.DefaultModel = new() { ModelId = "offline" };
                options.CredentialResolver = Credentials.FromStatic("offline");
            });
            ai
            .AddTask(reference, new()
            {
                Instructions = "Extract",
                ExecutionDefaults = new() { MaxOutputTokens = 800, TotalTimeout = TimeSpan.FromSeconds(30) },
            })
            .AddTask(existing, task)
            .AddTask(bound, task.BindOutput())
            .AddTask(simple, "Extract");
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler(async (request, token) =>
        {
            bodies.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token)));
            return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{\"value\":\"answer\"}"));
        }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var ai = provider.GetRequiredService<StructlyAi>();
        StructuredResult<Answer> result = await ai.ExecuteAsync(new AiTaskReference<Answer>("typed"), "input", TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        await ai.ExecuteAsync(reference, new() { Input = "input", MaxOutputTokens = 1600 }, TestContext.Current.CancellationToken);
        await ai.ExecuteTaskAsync<Answer>(reference.Name, "input", TestContext.Current.CancellationToken);
        foreach(var other in new[] { existing, bound, simple })
            Assert.True((await ai.ExecuteAsync(other, "input", TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(800, bodies[0].GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(1600, bodies[1].GetProperty("max_output_tokens").GetInt32());
        Assert.Equal(800, bodies[2].GetProperty("max_output_tokens").GetInt32());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ai.ExecuteAsync(new AiTaskReference<Answer>("missing"), "input", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.ExecuteAsync(new AiTaskReference<OtherAnswer>("typed"), "input", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => new AiTaskReference<Answer>(" "));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddStructlyAi(builder => builder
            .AddTask(reference, "Extract").AddTask<OtherAnswer>(reference.Name, "Other")));
    }

    [Fact]
    public async Task Registration_forms_execute_independent_definitions_and_preserve_request_settings()
    {
        var bodies = new List<JsonElement>();
        var observed = 0;
        var services = new ServiceCollection();

        var existing = StructuredTask.Create<Answer>(new() { Instructions = "Existing instructions" });
        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider(options =>
            {
                options.DefaultModel = new() { ModelId = "default-model" };
                options.Profiles.Add("alternate", new() { ModelId = "alternate-model" });
                options.CredentialResolver = Credentials.FromStatic("offline");
            });
            ai
            .AddTask<Answer>("simple", "Simple instructions")
            .AddTask<Answer>("options", new() { Instructions = "Options instructions", ModelSelection = new() { ProfileName = "alternate" } })
            .AddTask("existing", existing)
            .AddTask("bound", existing.BindOutput());
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler(async (request, token) =>
        {
            bodies.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token)));
            return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{\"value\":\"answer\"}", usage: new() { TotalTokens = 9 }));
        }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var ai = provider.GetRequiredService<StructlyAi>();
        Assert.Same(ai, provider.GetRequiredService<StructlyAi>());
        foreach(var name in new[] { "simple", "options", "existing", "bound" })
            Assert.Equal("answer", (await ai.ExecuteTaskAsync<Answer>(name, "input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);

        var result = await ai.ExecuteTaskAsync<Answer>("options", new()
        {
            Input = "override input",
            Instructions = "Override instructions",
            ModelSelection = new() { ModelId = "override-model" },
            CorrelationId = "job-7",
            UsageObserver = (usage, token) =>
            {
                observed++;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal("answer", result.EnsureSuccess().Value);
        Assert.Equal("job-7", result.Metadata.CorrelationId);
        Assert.Equal(9, result.Metadata.Usage!.TotalTokens);
        Assert.Equal(1, observed);
        Assert.Equal("Simple instructions", bodies[0].GetProperty("instructions").GetString());
        Assert.Equal("alternate-model", bodies[1].GetProperty("model").GetString());
        Assert.Equal("Existing instructions", bodies[2].GetProperty("instructions").GetString());
        Assert.Equal("Override instructions", bodies[4].GetProperty("instructions").GetString());
        Assert.Equal("override-model", bodies[4].GetProperty("model").GetString());
    }

    [Fact]
    public async Task Lookup_errors_precede_client_resolution_and_names_are_case_sensitive()
    {
        var services = new ServiceCollection();
        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider();
            ai.AddTask<Answer>("extract", "Extract").AddTask<OtherAnswer>("other", "Other");
        });
        using var provider = services.BuildServiceProvider();
        var ai = provider.GetRequiredService<StructlyAi>();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ai.ExecuteTaskAsync<Answer>("Extract", "input", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ai.ExecuteTaskAsync<Answer>("missing", "input", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.ExecuteTaskAsync<OtherAnswer>("extract", "input", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.ExecuteTaskAsync<Answer>("other", "input", TestContext.Current.CancellationToken));
        // Invalid provider options fail only after a valid task lookup.
        await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => ai.ExecuteTaskAsync<Answer>("extract", "input", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Registration_validates_definitions_and_freezes_retained_builders()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddStructlyAi(ai => ai.AddTask<Answer>(" ", "Extract")));
        Assert.Throws<ArgumentException>(() => services.AddStructlyAi(ai => ai.AddTask<Answer>("extract", " ")));
        Assert.Throws<StructuredSchemaException>(() => services.AddStructlyAi(ai => ai.AddTask<Dictionary<string, string>>("invalid", "Extract")));
        Assert.Throws<ArgumentException>(() => services.AddStructlyAi(ai => ai.AddTask<Answer>("extract", "Extract").AddTask<OtherAnswer>("extract", "Other")));
        StructlyAiBuilder? retained = null;
        services.AddStructlyAi(ai =>
        {
            retained = ai;
            ai.ConfigureOpenAiProvider(options => options.DefaultModel = new() { ModelId = "offline" });
            ai.AddTask<Answer>("extract", "Extract");
            ai.AddTask<Answer>("Extract", "Separate task");
        });
        Assert.Throws<InvalidOperationException>(() => retained!.AddTask<Answer>("later", "Later"));
        Assert.Throws<InvalidOperationException>(() => services.AddStructlyAi(ai => ai.AddTask<Answer>("another", "Another")));
    }

    [Fact]
    public void Failed_configuration_freezes_builder_without_registering_service()
    {
        var services = new ServiceCollection();
        StructlyAiBuilder? retained = null;
        Assert.Throws<InvalidOperationException>(() => services.AddStructlyAi(ai =>
        {
            retained = ai;
            throw new InvalidOperationException("Configuration failed.");
        }));
        Assert.Throws<InvalidOperationException>(() => retained!.AddTask<Answer>("later", "Later"));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(StructlyAi));
    }

    [Fact]
    public async Task Bound_vocabulary_is_snapshotted_and_unbound_tasks_accept_request_vocabularies()
    {
        var values = new List<string> { "first" };
        var vocabularies = new Dictionary<string, IReadOnlyList<string>> { ["choices"] = values };
        var task = StructuredTask.Create<Choice>(new() { Instructions = "Choose" });
        var services = new ServiceCollection();

        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider(options =>
            {
                options.DefaultModel = new() { ModelId = "offline" };
                options.CredentialResolver = Credentials.FromStatic("offline");
            });
            ai.AddTask("bound", task.BindOutput(new() { Vocabularies = vocabularies })).AddTask("unbound", task);
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler((request, token) =>
            Task.FromResult(ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{\"value\":\"first\"}")))));
        values[0] = "changed";
        using var provider = services.BuildServiceProvider();
        var ai = provider.GetRequiredService<StructlyAi>();
        Assert.Equal("first", (await ai.ExecuteTaskAsync<Choice>("bound", "input", TestContext.Current.CancellationToken)).EnsureSuccess().Value);
        var unbound = await ai.ExecuteTaskAsync<Choice>("unbound", new() { Input = "input", Vocabularies = vocabularies }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, unbound.Error!.Kind);
        var overridden = await ai.ExecuteTaskAsync<Choice>("bound", new() { Input = "input", Vocabularies = vocabularies }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, overridden.Error!.Kind);
    }

    [Fact]
    public async Task Singleton_worker_executes_concurrently_with_independent_async_scopes()
    {
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var failOutput = false;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            if(Interlocked.Increment(ref started) == 8)
                allStarted.SetResult();

            await allStarted.Task.WaitAsync(token);
            return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText(failOutput ? "{broken" : "{\"value\":\"answer\"}"));
        }))
        { Timeout = Timeout.InfiniteTimeSpan };
        var scopes = new ConcurrentBag<ScopeProbe>();
        var services = new ServiceCollection();
        services.AddScoped(provider => new ScopeProbe());
        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider(options => options.DefaultModel = new() { ModelId = "offline" });
            ai.AddTask<Answer>("extract", "Extract");
        });
        services.AddTransient<IStructuredClient>(provider => provider.GetRequiredService<OpenAiClient>());
        services.AddTransient(provider =>
        {
            scopes.Add(provider.GetRequiredService<ScopeProbe>());
            return new OpenAiClient(http, new() { DefaultModel = new() { ModelId = "offline" }, CredentialResolver = Credentials.FromStatic("offline") });
        });
        services.AddSingleton<Worker>();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var worker = provider.GetRequiredService<Worker>();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => worker.Execute(TestContext.Current.CancellationToken)));
        Assert.All(results, result => Assert.Equal("answer", result.EnsureSuccess().Value));
        Assert.Equal(8, scopes.Count);
        Assert.Equal(8, scopes.Distinct().Count());
        Assert.All(scopes, probe => Assert.True(probe.Disposed));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<StructuredOperationCanceledException>(() => worker.Execute(cancellation.Token));
        Assert.Equal(9, scopes.Count);
        Assert.All(scopes, probe => Assert.True(probe.Disposed));
        failOutput = true;
        var failure = await worker.Execute(TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, failure.Error!.Kind);
        Assert.Equal(10, scopes.Count);
        Assert.All(scopes, probe => Assert.True(probe.Disposed));
    }

    [Fact]
    public async Task Configuration_reload_updates_clients_used_by_existing_service()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Structly:OpenAI:DefaultModel:ModelId"] = "first-model",
            ["Structly:OpenAI:ApiKey"] = "first-key",
        });
        var models = new List<string?>();
        var keys = new List<string?>();

        builder.Services.AddStructlyAi(builder.Configuration, ai =>
        {
            ai.ConfigureOpenAiProvider();
            ai.AddTask<Answer>("extract", "Extract");
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler(async (request, token) =>
        {
            models.Add(JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(token)).GetProperty("model").GetString());
            keys.Add(request.Headers.Authorization?.Parameter);
            return ResponseEnvelopes.ToHttpResponse(ResponseEnvelopes.CompletedText("{\"value\":\"answer\"}"));
        }));
        using var host = builder.Build();
        var ai = host.Services.GetRequiredService<StructlyAi>();
        (await ai.ExecuteTaskAsync<Answer>("extract", "input", TestContext.Current.CancellationToken)).EnsureSuccess();
        builder.Configuration["Structly:OpenAI:DefaultModel:ModelId"] = "second-model";
        builder.Configuration["Structly:OpenAI:ApiKey"] = "second-key";
        ((IConfigurationRoot)builder.Configuration).Reload();
        (await ai.ExecuteTaskAsync<Answer>("extract", "input", TestContext.Current.CancellationToken)).EnsureSuccess();
        Assert.Equal(new[] { "first-model", "second-model" }, models);
        Assert.Equal(new[] { "first-key", "second-key" }, keys);
    }

    [Fact]
    public async Task Streaming_failure_and_invalid_input_preserve_core_behavior()
    {
        var sent = 0;
        var credentials = 0;
        var progress = 0;
        var services = new ServiceCollection();

        services.AddStructlyAi(ai =>
        {
            ai.ConfigureOpenAiProvider(options =>
            {
                options.DefaultModel = new() { ModelId = "offline" };
                options.CredentialResolver = token =>
                {
                    credentials++;
                    return ValueTask.FromResult<string?>("offline");
                };
            });
            ai.AddTask<Answer>("extract", "Extract");
        }).ConfigurePrimaryHttpMessageHandler(() => new Handler((request, token) =>
        {
            sent++;
            var terminal = JsonSerializer.Serialize(new { type = "response.completed", response = JsonSerializer.Deserialize<JsonElement>(ResponseTestSupport.Envelope("{broken")) });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"{broken\",\"output_index\":0}\n\nevent: response.completed\ndata: " + terminal + "\n\n", System.Text.Encoding.UTF8, "text/event-stream"),
            });
        }));
        using var provider = services.BuildServiceProvider();
        var ai = provider.GetRequiredService<StructlyAi>();
        var invalid = await ai.ExecuteTaskAsync<Answer>("extract", " ", TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidRequest, invalid.Error!.Kind);
        Assert.Equal(0, credentials);
        Assert.Equal(0, sent);
        var result = await ai.ExecuteTaskAsync<Answer>("extract", new()
        {
            Input = "input",
            Stream = true,
            Progress = (item, token) =>
            {
                progress++;
                return ValueTask.CompletedTask;
            },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(StructuredErrorKind.InvalidOutput, result.Error!.Kind);
        Assert.Equal(100, result.Metadata.Usage!.InputTokens);
        Assert.Equal("current", result.Metadata.ResponseId);
        Assert.True(progress > 0);
        Assert.Equal(1, sent);
    }

    public sealed record Answer(string Value);
    public sealed record OtherAnswer(int Count);
    public sealed record Choice
    {
        [DynamicVocabulary("choices")]
        public required string Value { get; init; }
    }
    sealed class Worker(StructlyAi ai)
    {
        public Task<StructuredResult<Answer>> Execute(CancellationToken token) => ai.ExecuteTaskAsync<Answer>("extract", "input", token);
    }
    sealed class ScopeProbe : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
