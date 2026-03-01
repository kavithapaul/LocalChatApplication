using System.Diagnostics;
using System.Text;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalChatApp.Services;

namespace LocalChatApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly IImageGenerationService _imageService;
    private readonly ISpeechToTextService _speechToTextService;
    private readonly IRagIngestionService _ragIngestionService;

    private CancellationTokenSource? _activeRequestCts;

    [ObservableProperty]
    private string prompt = string.Empty;

    [ObservableProperty]
    private string response = "Type a prompt and click Ask. For images, try: create an image of a black cat.";

    [ObservableProperty]
    private BitmapImage? generatedImage;

    [ObservableProperty]
    private string imageStatus = "No generated image yet.";

    [ObservableProperty]
    private bool isBusy;

    public MainViewModel(
        IChatService chatService,
        IImageGenerationService imageService,
        ISpeechToTextService speechToTextService,
        IRagIngestionService ragIngestionService)
    {
        _chatService = chatService;
        _imageService = imageService;
        _speechToTextService = speechToTextService;
        _ragIngestionService = ragIngestionService;
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        IsBusy = true;
        _activeRequestCts?.Cancel();
        _activeRequestCts?.Dispose();
        _activeRequestCts = new CancellationTokenSource();

        try
        {
            if (ShouldGenerateImage(Prompt))
            {
                ImageStatus = "Generating image locally...";
                var filePath = await _imageService.GenerateImageAsync(Prompt);
                GeneratedImage = LoadBitmap(filePath);
                ImageStatus = $"Image generated: {filePath}";
                Response = "I routed this prompt to the local image model.";
            }
            else
            {
                await StreamResponseAsync(Prompt, _activeRequestCts.Token);
            }
        }
        catch (Exception ex)
        {
            Response = $"Error: {ex.Message}";

            if (ShouldGenerateImage(Prompt))
            {
                GeneratedImage = null;
                ImageStatus = $"Image generation failed. {ex.Message}";
            }
        }
        finally
        {
            _activeRequestCts?.Dispose();
            _activeRequestCts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DictateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            Response = "Listening for ~6 seconds...";
            var transcription = await _speechToTextService.CaptureAndTranscribeAsync();

            if (string.IsNullOrWhiteSpace(transcription))
            {
                Response = "I could not detect speech. Please try again.";
                return;
            }

            Prompt = transcription;
            Response = "Transcription complete. Click Ask to run the local model.";
        }
        catch (Exception ex)
        {
            Response = $"Microphone/STT error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UploadPdfForRagAsync(string pdfPath)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Response = "Starting RAG ingestion: chunking PDF, generating embeddings, and storing in Chroma...";

        try
        {
            var result = await _ragIngestionService.IngestPdfAsync(pdfPath, CancellationToken.None);
            Response = $"RAG ingestion complete. Added {result.ChunksStored} chunks from '{result.FileName}' to Chroma collection '{result.CollectionName}'.";
        }
        catch (Exception ex)
        {
            Response = $"RAG ingestion failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StreamResponseAsync(string userPrompt, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var buffer = new StringBuilder(capacity: 512);

        Response = "Thinking with local Mistral model...";

        await foreach (var chunk in _chatService.StreamAsync(userPrompt, cancellationToken))
        {
            buffer.Append(chunk);
            Response = buffer.ToString();
        }

        if (buffer.Length == 0)
        {
            Response = "No response from local model. Verify Ollama and the Mistral model are running.";
            return;
        }

        stopwatch.Stop();
        var tokensPerSecond = buffer.Length / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        ImageStatus = $"Model response streamed in {stopwatch.Elapsed.TotalSeconds:F1}s (~{tokensPerSecond:F0} chars/sec).";
    }

    private static bool ShouldGenerateImage(string prompt)
    {
        var normalized = prompt.Trim().ToLowerInvariant();
        return normalized.StartsWith("create an image")
               || normalized.StartsWith("generate an image")
               || normalized.Contains("image of")
               || normalized.Contains("draw ");
    }

    private static BitmapImage LoadBitmap(string filePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(filePath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
