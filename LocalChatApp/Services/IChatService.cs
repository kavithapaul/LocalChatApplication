namespace LocalChatApp.Services;

public interface IChatService
{
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);
}
