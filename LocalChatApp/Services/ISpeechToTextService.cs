namespace LocalChatApp.Services;

public interface ISpeechToTextService
{
    Task<string> CaptureAndTranscribeAsync(CancellationToken cancellationToken = default);
}
