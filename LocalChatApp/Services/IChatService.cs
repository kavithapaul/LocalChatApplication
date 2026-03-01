namespace LocalChatApp.Services;

public interface IChatService
{
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken = default);
}
