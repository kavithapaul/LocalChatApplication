using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalChatApp.Services;

public sealed class OllamaChatService : IChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatService(string baseUrl, string model)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _model = model;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var payload = CreatePayload(prompt, stream: false);
        using var response = await _httpClient.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateChunk>(cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(result?.Response)
            ? "No response from local model. Verify Ollama and the Mistral model are running."
            : result.Response;
    }

    public async IAsyncEnumerable<string> StreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payloadJson = JsonSerializer.Serialize(CreatePayload(prompt, stream: true), JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaGenerateChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaGenerateChunk>(line, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                yield return chunk.Response;
            }

            if (chunk?.Done == true)
            {
                yield break;
            }
        }
    }

    private object CreatePayload(string prompt, bool stream)
    {
        return new
        {
            model = _model,
            prompt,
            stream,
            keep_alive = "30m",
            options = new
            {
                num_predict = 256,
                temperature = 0.4,
                top_p = 0.9
            }
        };
    }

    private sealed record OllamaGenerateChunk(
        [property: JsonPropertyName("response")] string Response,
        [property: JsonPropertyName("done")] bool Done);
}
