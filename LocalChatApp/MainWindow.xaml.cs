using System.Windows;
using Microsoft.Win32;
using LocalChatApp.Services;
using LocalChatApp.ViewModels;

namespace LocalChatApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Use the model name that the Ollama server exposes (not a filesystem path)
        var llmService = new OllamaChatService("http://localhost:11434", "mistral");
        var imageService = new StableDiffusionImageService("http://127.0.0.1:7860", Path.Combine(AppContext.BaseDirectory, "generated-images"));
        var speechService = new WhisperSpeechToTextService(Path.Combine(AppContext.BaseDirectory, "models", "ggml-base.bin"));
        var chromaUrl = Environment.GetEnvironmentVariable("LOCALCHAT_CHROMA_URL") ?? "http://localhost:8000";
        var ollamaUrl = Environment.GetEnvironmentVariable("LOCALCHAT_OLLAMA_URL") ?? "http://127.0.0.1:11434";
        var ragIngestionService = new RagIngestionService(chromaUrl, "localchat_rag", ollamaUrl);

        DataContext = new MainViewModel(llmService, imageService, speechService, ragIngestionService);
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.ContextMenu is { } menu)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }

    private async void UploadPdfMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select a PDF to ingest into Chroma"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await vm.UploadPdfForRagAsync(dialog.FileName);
    }
}
