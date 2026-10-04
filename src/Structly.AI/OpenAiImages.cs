using System.Text.Json;
using Structly.AI.Imaging;

namespace Structly.AI.OpenAI;

public sealed partial class OpenAiClient
{
    /// <summary>Generates validated base64 images in provider order through the Image API.</summary>
    public Task<StructuredResult<IReadOnlyList<GeneratedImage>>> GenerateImagesAsync(ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var controls = new StructuredRequest
        {
            TotalTimeout = request.TotalTimeout,
            CredentialResolver = request.CredentialResolver,
            UsageObserver = request.UsageObserver,
            CorrelationId = request.CorrelationId,
            OpenAi = new() { IdempotencyKey = request.IdempotencyKey, CaptureRawResponse = request.CaptureRawResponse }
        };
        return RunOperation(controls, "Images", execution => ImageCore(request, controls, execution), cancellationToken);
    }

    async Task<StructuredResult<IReadOnlyList<GeneratedImage>>> ImageCore(ImageGenerationRequest request,
        StructuredRequest controls, OpenAiExecution execution)
    {
        execution.CheckCancellation();
        Dictionary<string, object?> payload;
        try
        {
            ValidateCommonRequest(controls);
            if (String.IsNullOrWhiteSpace(request.Prompt) || request.Count is < 1 or > 10 || !Enum.IsDefined(request.Size) ||
                !Enum.IsDefined(request.Quality) || !Enum.IsDefined(request.Background) || !Enum.IsDefined(request.Format) ||
                request.Background == ImageBackground.Transparent && request.Format == ImageFormat.Jpeg ||
                request.CustomDimensions is { } custom && (custom.Width <= 0 || custom.Height <= 0) || request.ModelSelection is null)
                throw new ArgumentException("Invalid image options.");
            var model = SelectModel(request.ModelSelection, _options.ImageProfiles);
            if (model.ReasoningEffort is not null) throw new ArgumentException("Images do not support reasoning.");
            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var size = request.CustomDimensions is { } dimensions ? $"{dimensions.Width}x{dimensions.Height}" : request.Size switch
            { ImageSize.Square => "1024x1024", ImageSize.Portrait => "1024x1536", ImageSize.Landscape => "1536x1024", ImageSize.Wide => "1536x864", _ => "auto" };
            payload = new()
            {
                ["model"] = model.ModelId,
                ["prompt"] = request.Prompt,
                ["n"] = request.Count,
                ["size"] = size,
                ["quality"] = request.Quality.ToString().ToLowerInvariant(),
                ["background"] = request.Background.ToString().ToLowerInvariant(),
                ["output_format"] = request.Format.ToString().ToLowerInvariant()
            };
        }
        catch (ArgumentException) { return InvalidOptions<IReadOnlyList<GeneratedImage>>(execution); }
        return await Send(payload, "images/generations", controls, execution, null,
            root => ValueTask.FromResult(ReadImages(root, request, execution))).ConfigureAwait(false);
    }

    static StructuredResult<IReadOnlyList<GeneratedImage>> ReadImages(JsonElement root, ImageGenerationRequest request, OpenAiExecution execution)
    {
        StructuredResult<IReadOnlyList<GeneratedImage>> Invalid() => LocalFailure<IReadOnlyList<GeneratedImage>>(execution, StructuredErrorKind.InvalidResponse);
        var data = Property(root, "data");
        var format = request.Format.ToString().ToLowerInvariant();
        var reportedFormat = Property(root, "output_format");
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != request.Count ||
            reportedFormat.ValueKind != JsonValueKind.Undefined && Text(root, "output_format") != format) return Invalid();
        var images = new List<GeneratedImage>();
        foreach (var item in data.EnumerateArray())
        {
            var base64 = Text(item, "b64_json");
            if (String.IsNullOrWhiteSpace(base64)) return Invalid();
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { return Invalid(); }
            if (!MatchesFormat(bytes, request.Format)) return Invalid();
            images.Add(new(base64, "image/" + format));
        }
        return StructuredResult<IReadOnlyList<GeneratedImage>>.Success(images.AsReadOnly(), execution.Metadata, execution.Warnings);
    }

    static bool MatchesFormat(ReadOnlySpan<byte> bytes, ImageFormat format) => format switch
    {
        ImageFormat.Png => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        ImageFormat.Jpeg => bytes.StartsWith(new byte[] { 255, 216, 255 }),
        ImageFormat.WebP => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };
}
