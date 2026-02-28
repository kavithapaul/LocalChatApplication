using NAudio.Wave;
using Whisper.net;

namespace LocalChatApp.Services;

public sealed class WhisperSpeechToTextService : ISpeechToTextService
{
    private readonly string _modelPath;

    public WhisperSpeechToTextService(string modelPath)
    {
        _modelPath = modelPath;
    }

    public async Task<string> CaptureAndTranscribeAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_modelPath))
        {
            throw new FileNotFoundException("Whisper model file was not found. Download ggml-base.bin and place it in ./models.", _modelPath);
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"localchat-{Guid.NewGuid():N}.wav");
        await RecordAudioAsync(wavPath, TimeSpan.FromSeconds(6), cancellationToken);

        using var whisperFactory = WhisperFactory.FromPath(_modelPath);
        using var processor = whisperFactory.CreateBuilder().WithLanguage("en").Build();

        await using var audioStream = File.OpenRead(wavPath);
        var segments = new List<string>();

        await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
        {
            segments.Add(segment.Text.Trim());
        }

        File.Delete(wavPath);
        return string.Join(" ", segments.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
    }

    private static Task RecordAudioAsync(string outputPath, TimeSpan duration, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16_000, 16, 1)
        };

        using var writer = new WaveFileWriter(outputPath, waveIn.WaveFormat);
        using var timer = new System.Timers.Timer(duration.TotalMilliseconds) { AutoReset = false };

        waveIn.DataAvailable += (_, args) =>
        {
            writer.Write(args.Buffer, 0, args.BytesRecorded);
            writer.Flush();
        };

        waveIn.RecordingStopped += (_, _) => tcs.TrySetResult();
        timer.Elapsed += (_, _) => waveIn.StopRecording();

        using var reg = cancellationToken.Register(() =>
        {
            waveIn.StopRecording();
            tcs.TrySetCanceled(cancellationToken);
        });

        waveIn.StartRecording();
        timer.Start();
        return tcs.Task;
    }
}
