namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient : IStructuredClient
{
    static readonly OpenAiResponseOptions _defaultResponseOptions = new();

    static OpenAiResponseOptions ResponseOptions(StructuredRequest request) => (request as OpenAiRequest)?.OpenAi ?? _defaultResponseOptions;

    readonly HttpClient _http;
    readonly OpenAiClientOptions _options;
    readonly Dictionary<string, ModelSelection> _profiles;

    /// <summary>Snapshots and validates configuration without resolving credentials.</summary>
    public OpenAiClient(HttpClient httpClient, OpenAiClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.DefaultModel);
        ArgumentNullException.ThrowIfNull(options.Profiles);
        ArgumentNullException.ThrowIfNull(options.CacheCompatibility);
        options.DefaultModel.Validate();
        if(options.TimeProvider is null || !OpenAiExecution.ValidTimeout(options.TotalTimeout) ||
            options.InactivityTimeout is { } idle && idle <= TimeSpan.Zero)
            throw new ArgumentException("Invalid execution clock or deadlines.", nameof(options));

        if(options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri ||
            options.BaseAddress.Scheme is not ("https" or "http") || !options.BaseAddress.AbsolutePath.EndsWith('/') ||
            options.BaseAddress.Query.Length != 0 || options.BaseAddress.Fragment.Length != 0 || options.BaseAddress.UserInfo.Length != 0 ||
            options.MaxResponseBytes <= 0 || options.MaxImageResponseBytes <= 0)
            throw new ArgumentException("Invalid API directory URI or response size ceiling.", nameof(options));

        _profiles = new(options.Profiles, StringComparer.Ordinal);
        foreach(var (name, profile) in _profiles)
        {
            if(String.IsNullOrWhiteSpace(name) || profile is null)
                throw new ArgumentException("Invalid model profile.", nameof(options));

            profile.Validate();
            if(profile.ModelId is null)
                throw new ArgumentException("Profiles must select explicit model IDs.", nameof(options));
        }

        SelectModel(options.DefaultModel);
        var embeddingProfiles = SnapshotProfiles(options.EmbeddingProfiles);
        var imageProfiles = SnapshotProfiles(options.ImageProfiles);
        var compatibility = new Dictionary<string, OpenAiCacheCompatibility>(options.CacheCompatibility, StringComparer.Ordinal);

        if(compatibility.Any(x => String.IsNullOrWhiteSpace(x.Key) || !Enum.IsDefined(x.Value)))
            throw new ArgumentException("Invalid cache compatibility configuration.", nameof(options));

        _http = httpClient;
        _options = options with
        {
            Profiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(_profiles),
            EmbeddingProfiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(embeddingProfiles),
            ImageProfiles = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ModelSelection>(imageProfiles),
            CacheCompatibility = new System.Collections.ObjectModel.ReadOnlyDictionary<string, OpenAiCacheCompatibility>(compatibility),
        };
    }

    static Dictionary<string, ModelSelection> SnapshotProfiles(IReadOnlyDictionary<string, ModelSelection> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var snapshot = new Dictionary<string, ModelSelection>(profiles, StringComparer.Ordinal);
        foreach(var (name, profile) in snapshot)
        {
            if(String.IsNullOrWhiteSpace(name) || profile is null)
                throw new ArgumentException("Invalid model profile.");

            profile.Validate();
            if(profile.ModelId is null || profile.ReasoningEffort is not null)
                throw new ArgumentException("Auxiliary profiles require explicit model IDs without reasoning.");
        }

        return snapshot;
    }

    ModelSelection SelectModel(ModelSelection selection, IReadOnlyDictionary<string, ModelSelection>? profiles = null)
    {
        selection.Validate();
        if(selection.ProfileName is not { } name)
            return selection;

        if(!(profiles ?? _profiles).TryGetValue(name, out var profile))
            throw new ArgumentException("Unknown model profile.");

        return profile with { ReasoningEffort = selection.ReasoningEffort ?? profile.ReasoningEffort };
    }

}
