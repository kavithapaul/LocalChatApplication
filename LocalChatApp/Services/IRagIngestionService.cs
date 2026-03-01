namespace LocalChatApp.Services;

public interface IRagIngestionService
{
    Task<RagIngestionResult> IngestPdfAsync(string pdfPath, CancellationToken cancellationToken);
}

public sealed record RagIngestionResult(string FileName, string CollectionName, int ChunksStored);
