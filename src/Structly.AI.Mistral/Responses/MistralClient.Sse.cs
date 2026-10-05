using System.Text;
using System.Text.Json;

namespace Structly.AI.Mistral;

public sealed partial class MistralClient
{
    async Task<bool> ReadSse(HttpResponseMessage response, StructuredRequest request, MistralExecution execution,
        Func<string, ValueTask<bool>> frameReceived)
    {
        await using var source = await execution.Await(response.Content.ReadAsStreamAsync(execution.Token), stream => stream.Dispose()).ConfigureAwait(false);
        using var line = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        var inactivity = request.InactivityTimeout ?? _inactivityTimeout;
        execution.StartInactivity(inactivity);
        var skipLf = false;
        var firstLine = true;
        var utf8 = new UTF8Encoding(false, true);
        var done = false;
        var data = new StringBuilder();
        while(!done)
        {
            var count = await execution.Await(source.ReadAsync(buffer, execution.Token).AsTask()).ConfigureAwait(false);
            if(count == 0)
                break;

            total += count;
            if(total > _maxResponseBytes)
                throw new JsonException();

            for(var i = 0; i < count && !done; i++)
            {
                if(skipLf && buffer[i] == 10)
                {
                    skipLf = false;
                    continue;
                }

                skipLf = false;
                if(buffer[i] is not (10 or 13))
                {
                    line.WriteByte(buffer[i]);
                    continue;
                }

                skipLf = buffer[i] == 13;
                string value;
                try
                {
                    value = utf8.GetString(line.ToArray());
                }
                catch(DecoderFallbackException)
                {
                    throw new JsonException();
                }

                if(firstLine)
                {
                    value = value.TrimStart('\uFEFF');
                    firstLine = false;
                }

                execution.ResetInactivity(inactivity);
                line.SetLength(0);
                if(value.StartsWith("data:", StringComparison.Ordinal))
                {
                    if(data.Length > 0)
                        data.Append('\n');

                    data.Append(value.AsSpan(5).TrimStart(' '));
                }
                else if(value.Length == 0 && data.Length > 0)
                {
                    var frame = data.ToString();
                    data.Clear();
                    done = await frameReceived(frame).ConfigureAwait(false);
                }
            }
        }

        execution.StopInactivity();
        return done;
    }
}
