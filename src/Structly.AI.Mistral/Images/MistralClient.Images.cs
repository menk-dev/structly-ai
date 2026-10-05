using Structly.AI.Imaging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    /// <summary>Generates one PNG through Mistral's beta image generation tool and downloads its file bytes.</summary>
    public Task<StructuredResult<IReadOnlyList<GeneratedImage>>> GenerateImagesAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var controls = new MistralRequest
        {
            CredentialResolver = request.CredentialResolver,
            TotalTimeout = request.TotalTimeout,
            UsageObserver = request.UsageObserver,
            CorrelationId = request.CorrelationId,
            CaptureRawResponse = request.CaptureRawResponse,
        };
        return RunOperation(controls, "Images", async execution =>
        {
            if(String.IsNullOrWhiteSpace(request.Prompt) || request.ModelSelection is null || request.Count != 1 || request.Size != ImageSize.Auto ||
                request.CustomDimensions is not null || request.Quality != ImageQuality.Auto || request.Background != ImageBackground.Auto ||
                request.Format != ImageFormat.Png || request.IdempotencyKey is not null)
                throw new ArgumentException("Mistral image generation supports one PNG with provider-selected dimensions, quality and background.");

            var model = SelectModel(request.ModelSelection, _imageProfiles);
            if(model.ReasoningEffort is not null)
                throw new ArgumentException("Image requests do not support reasoning effort.");

            execution.Metadata = execution.Metadata with { RequestedModel = model.ModelId };
            var payload = new
            {
                model = model.ModelId,
                inputs = request.Prompt,
                instructions = "Generate exactly one image using the image_generation tool.",
                tools = new[] { new { type = "image_generation" } },
                store = false,
                stream = false,
            };
            var generated = await Send(execution, controls, null, HttpMethod.Post, "conversations", JsonContent.Create(payload), async response =>
            {
                using var document = await ReadEnvelope(response, execution).ConfigureAwait(false);
                var root = document.RootElement;
                execution.Metadata = execution.Metadata with
                {
                    ConversationId = StringValue(root, "conversation_id"),
                    Usage = ReadUsage(root, execution.Warnings),
                    RawResponse = request.CaptureRawResponse ? root : null,
                };
                var files = new List<string>();
                foreach(var output in root.GetProperty("outputs").EnumerateArray())
                {
                    if(StringValue(output, "type") != "message.output")
                        continue;

                    execution.Metadata = execution.Metadata with
                    {
                        ResponseId = StringValue(output, "id") ?? execution.Metadata.ResponseId,
                        ResolvedModel = StringValue(output, "model") ?? execution.Metadata.ResolvedModel,
                    };
                    var content = Property(output, "content");
                    if(content.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach(var chunk in content.EnumerateArray())
                    {
                        if(StringValue(chunk, "type") != "tool_file" || StringValue(chunk, "tool") != "image_generation")
                            continue;

                        if(StringValue(chunk, "file_type") is { } format && format != "png")
                            throw new JsonException();

                        files.Add(RemoteId(StringValue(chunk, "file_id")!));
                    }
                }

                return files.Count == 1 ? StructuredResult<string>.Success(files[0], execution.Metadata)
                    : execution.Failure<string>(StructuredErrorKind.InvalidResponse);
            }).ConfigureAwait(false);
            if(!generated.IsSuccess)
                return StructuredResult<IReadOnlyList<GeneratedImage>>.Failure(generated.Error!, execution.Metadata, execution.Warnings);

            return await Send(execution, controls, null, HttpMethod.Get, "files/" + generated.Value + "/content", null, async response =>
            {
                await using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), source => source.Dispose()).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await CopyLimited(source, buffer, _maxImageResponseBytes, execution).ConfigureAwait(false);
                var bytes = buffer.ToArray();
                if(!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                    return execution.Failure<IReadOnlyList<GeneratedImage>>(StructuredErrorKind.InvalidResponse);

                IReadOnlyList<GeneratedImage> images = Array.AsReadOnly(new[] { new GeneratedImage(Convert.ToBase64String(bytes), "image/png") });
                return StructuredResult<IReadOnlyList<GeneratedImage>>.Success(images, execution.Metadata);
            }).ConfigureAwait(false);
        }, cancellationToken);
    }
}
