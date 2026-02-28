using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LocalChatApp.Services;

public sealed class OllamaChatService : IChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;

    public OllamaChatService(string baseUrl, string model)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _model = model;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var payload = new OllamaGenerateRequest(_model, prompt, Stream: false);
        using var response = await _httpClient.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(result?.Response)
            ? "No response from local model. Verify Ollama and the Mistral model are running."
            : result.Response;
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaGenerateResponse([property: JsonPropertyName("response")] string Response);
}
