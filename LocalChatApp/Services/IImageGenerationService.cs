namespace LocalChatApp.Services;

public interface IImageGenerationService
{
    Task<string> GenerateImageAsync(string prompt, CancellationToken cancellationToken = default);
}
