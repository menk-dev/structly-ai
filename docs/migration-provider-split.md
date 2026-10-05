# Provider package migration

Add a reference to `Structly.AI.OpenAI` wherever you construct or use OpenAI types. Keep `using Structly.AI.OpenAI;`: namespaces are preserved, but the types have moved to a different assembly. Rebuild all consumers; this change is not binary compatible.

Replace separate `AddStructlyOpenAi` and `AddStructlyAi` calls with one `AddStructlyAi` callback selecting `ConfigureOpenAiProvider`. The return value is now `IHttpClientBuilder`, not `IServiceCollection`. Move HTTP customization onto this registration. Rename `OpenAiHostingOptions` to `OpenAiOptions` and import `Structly.AI.OpenAI`.

Replace requests containing `OpenAi` controls with `OpenAiRequest`. Shared request settings remain on `StructuredRequest`. Record cloning and task-default application preserve provider settings. `CacheDiagnostics` now belongs to `Structly.AI`.

Core supports schema generation and output validation alone. OpenAI can be used without hosting. Hosting can execute any selected `IStructuredClient` implementation and does not reference OpenAI. Testing utilities reference OpenAI because their fixture constructs its client.
