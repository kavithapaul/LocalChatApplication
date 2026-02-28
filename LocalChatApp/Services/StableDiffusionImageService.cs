using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LocalChatApp.Services;

public sealed class StableDiffusionImageService : IImageGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDirectory;

    public StableDiffusionImageService(string baseUrl, string outputDirectory)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new Txt2ImgRequest(prompt, Steps: 28, Width: 768, Height: 768, CfgScale: 7);

        using var response = await _httpClient.PostAsJsonAsync("/sdapi/v1/txt2img", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<Txt2ImgResponse>(cancellationToken: cancellationToken);
        var firstImage = body?.Images?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstImage))
        {
            throw new InvalidOperationException("The local image model returned no image bytes.");
        }

        var filePath = Path.Combine(_outputDirectory, $"generated-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var bytes = Convert.FromBase64String(firstImage);
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
        return filePath;
    }

    private sealed record Txt2ImgRequest(
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("steps")] int Steps,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("cfg_scale")] int CfgScale);

    private sealed record Txt2ImgResponse([property: JsonPropertyName("images")] List<string> Images);
}
