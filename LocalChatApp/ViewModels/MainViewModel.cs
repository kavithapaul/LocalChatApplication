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

    public MainViewModel(IChatService chatService, IImageGenerationService imageService, ISpeechToTextService speechToTextService)
    {
        _chatService = chatService;
        _imageService = imageService;
        _speechToTextService = speechToTextService;
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Prompt))
        {
            return;
        }

        IsBusy = true;

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
                Response = "Thinking with local Mistral model...";
                Response = await _chatService.AskAsync(Prompt);
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
