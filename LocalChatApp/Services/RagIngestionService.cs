using System.Net.Http.Json;
using System.Text;
using UglyToad.PdfPig;

namespace LocalChatApp.Services;

public sealed class RagIngestionService : IRagIngestionService
{
    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly string _chromaBaseUrl;
    private readonly string _ollamaBaseUrl;
    private readonly HttpClient _ollamaHttpClient;

    public RagIngestionService(string chromaBaseUrl, string collectionName = "localchat_rag", string ollamaBaseUrl = "http://127.0.0.1:11434")
    {
        _chromaBaseUrl = chromaBaseUrl.TrimEnd('/');
        _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
        _httpClient = new HttpClient { BaseAddress = new Uri(_chromaBaseUrl + "/") };
        _ollamaHttpClient = new HttpClient { BaseAddress = new Uri(_ollamaBaseUrl + "/") };
        _collectionName = collectionName;
    }

    public async Task<RagIngestionResult> IngestPdfAsync(string pdfPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException("PDF file not found.", pdfPath);
        }

        await EnsureChromaReachableAsync(cancellationToken);

        var fullText = ExtractTextFromPdf(pdfPath);
        var chunks = ChunkText(fullText, 900, 150).ToList();

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No readable text found in PDF.");
        }

        var collectionId = await EnsureCollectionAsync(cancellationToken);

        var ids = new List<string>(chunks.Count);
        var documents = new List<string>(chunks.Count);
        var metadatas = new List<Dictionary<string, object>>(chunks.Count);
        var embeddings = new List<List<float>>(chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedding = await GetEmbeddingAsync(chunk, cancellationToken);

            ids.Add($"{Path.GetFileNameWithoutExtension(pdfPath)}-{i}-{Guid.NewGuid():N}"[..24]);
            documents.Add(chunk);
            embeddings.Add(embedding);
            metadatas.Add(new Dictionary<string, object>
            {
                ["source"] = Path.GetFileName(pdfPath),
                ["chunk_index"] = i
            });
        }

        var addPayload = new
        {
            ids,
            documents,
            embeddings,
            metadatas
        };

        HttpResponseMessage addResponse;
        try
        {
            addResponse = await _httpClient.PostAsJsonAsync($"api/v1/collections/{collectionId}/add", addPayload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateChromaConnectionException(ex);
        }

        addResponse.EnsureSuccessStatusCode();

        return new RagIngestionResult(Path.GetFileName(pdfPath), _collectionName, chunks.Count);
    }

    private async Task<string> EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage getResponse;
        try
        {
            getResponse = await _httpClient.GetAsync($"api/v1/collections/{_collectionName}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateChromaConnectionException(ex);
        }

        if (getResponse.IsSuccessStatusCode)
        {
            var existing = await getResponse.Content.ReadFromJsonAsync<ChromaCollectionResponse>(cancellationToken: cancellationToken);
            return existing?.Id ?? throw new InvalidOperationException("Chroma returned collection without id.");
        }

        if (getResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var getError = await ReadErrorAsync(getResponse, cancellationToken);
            throw new InvalidOperationException($"Failed to query Chroma collection '{_collectionName}': {getError}");
        }

        var createPayload = new
        {
            name = _collectionName,
            metadata = new Dictionary<string, string>
            {
                ["description"] = "Chunks ingested from LocalChatApp for future RAG"
            }
        };

        HttpResponseMessage createResponse;
        try
        {
            createResponse = await _httpClient.PostAsJsonAsync("api/v1/collections", createPayload, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateChromaConnectionException(ex);
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            var createError = await ReadErrorAsync(createResponse, cancellationToken);
            throw new InvalidOperationException($"Failed to create Chroma collection '{_collectionName}': {createError}");
        }

        var created = await createResponse.Content.ReadFromJsonAsync<ChromaCollectionResponse>(cancellationToken: cancellationToken);
        return created?.Id ?? throw new InvalidOperationException("Failed to parse Chroma collection id.");
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        var builder = new StringBuilder(capacity: 8_192);

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private async Task<List<float>> GetEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = "nomic-embed-text",
            prompt = text
        };

        using var response = await _ollamaHttpClient.PostAsJsonAsync("api/embeddings", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new InvalidOperationException($"Failed to generate embedding via Ollama at '{_ollamaBaseUrl}': {error}");
        }

        var embeddingResponse = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken)
                                ?? throw new InvalidOperationException("Invalid embedding response from Ollama.");

        if (embeddingResponse.Embedding.Count == 0)
        {
            throw new InvalidOperationException("Ollama returned an empty embedding.");
        }

        return embeddingResponse.Embedding;
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var cleaned = text.Replace("\r", " ").Replace("\n", " ");
        var start = 0;

        while (start < cleaned.Length)
        {
            var length = Math.Min(chunkSize, cleaned.Length - start);
            var chunk = cleaned.Substring(start, length).Trim();

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                yield return chunk;
            }

            if (start + length >= cleaned.Length)
            {
                yield break;
            }

            start += chunkSize - overlap;
        }
    }

    private sealed class ChromaCollectionResponse
    {
        public string Id { get; set; } = string.Empty;
    }

    private async Task EnsureChromaReachableAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage heartbeat;

        try
        {
            heartbeat = await _httpClient.GetAsync("api/v1/heartbeat", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateChromaConnectionException(ex);
        }

        if (!heartbeat.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(heartbeat, cancellationToken);
            throw new InvalidOperationException($"Chroma endpoint '{_chromaBaseUrl}' responded to heartbeat with {(int)heartbeat.StatusCode} ({heartbeat.ReasonPhrase}). Details: {error}");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(payload) ? "No response body." : payload;
    }

    private InvalidOperationException CreateChromaConnectionException(HttpRequestException ex)
    {
        var message = $"Unable to connect to Chroma at '{_chromaBaseUrl}'. " +
                      "`chromadb.Client()` in Python only starts an embedded/in-process client and does not expose an HTTP server. " +
                      "Start the Chroma server (for example: `docker run -p 8000:8000 chromadb/chroma`) " +
                      "or set the `LOCALCHAT_CHROMA_URL` environment variable to the correct endpoint.";

        return new InvalidOperationException(message, ex);
    }

    private sealed class OllamaEmbeddingResponse
    {
        public List<float> Embedding { get; set; } = [];
    }
}
