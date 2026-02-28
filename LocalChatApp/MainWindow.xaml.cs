using LocalChatApp.Services;
using LocalChatApp.ViewModels;

namespace LocalChatApp;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Use the model name that the Ollama server exposes (not a filesystem path)
        var llmService = new OllamaChatService("http://localhost:11434", "mistral");
        var imageService = new StableDiffusionImageService("http://127.0.0.1:7860", Path.Combine(AppContext.BaseDirectory, "generated-images"));
        var speechService = new WhisperSpeechToTextService(Path.Combine(AppContext.BaseDirectory, "models", "ggml-base.bin"));

        DataContext = new MainViewModel(llmService, imageService, speechService);
    }
}
