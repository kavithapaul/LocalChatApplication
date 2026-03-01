using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        await using var audioStream = await RecordAudioToMemoryStreamAsync(TimeSpan.FromSeconds(6), cancellationToken);

        using var whisperFactory = WhisperFactory.FromPath(_modelPath);
        using var processor = whisperFactory.CreateBuilder().WithLanguage("en").Build();

        var segments = new List<string>();

        // Ensure stream is positioned to start
        audioStream.Position = 0;

        await foreach (var segment in processor.ProcessAsync(audioStream, cancellationToken))
        {
            segments.Add(segment.Text.Trim());
        }

        return string.Join(" ", segments.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
    }

    private static Task<MemoryStream> RecordAudioToMemoryStreamAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<MemoryStream>(TaskCreationOptions.RunContinuationsAsynchronously);

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16_000, 16, 1)
        };

        var timer = new System.Timers.Timer(duration.TotalMilliseconds) { AutoReset = false };

        var bufferStream = new MemoryStream();
        WaveFileWriter? writer = null;
        CancellationTokenRegistration reg = default;

        try
        {
            writer = new WaveFileWriter(bufferStream, waveIn.WaveFormat);
        }
        catch (Exception ex)
        {
            waveIn.Dispose();
            timer.Dispose();
            tcs.TrySetException(ex);
            return tcs.Task;
        }

        waveIn.DataAvailable += (_, args) =>
        {
            try
            {
                if (writer is null) return;
                writer.Write(args.Buffer, 0, args.BytesRecorded);
                writer.Flush();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                try { waveIn.StopRecording(); } catch { }
            }
        };

        waveIn.RecordingStopped += (_, _) =>
        {
            try
            {
                // finalize WAV header by disposing the writer
                writer?.Dispose();
                // Create a standalone MemoryStream with finalized WAV bytes
                var result = new MemoryStream(bufferStream.ToArray());
                result.Position = 0;
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                try { bufferStream.Dispose(); } catch { }
                try { timer.Dispose(); } catch { }
                try { waveIn.Dispose(); } catch { }
                try { reg.Dispose(); } catch { }
            }
        };

        timer.Elapsed += (_, _) => waveIn.StopRecording();

        reg = cancellationToken.Register(() =>
        {
            try { waveIn.StopRecording(); } catch { }
            tcs.TrySetCanceled(cancellationToken);
        });

        try
        {
            waveIn.StartRecording();
            timer.Start();
        }
        catch (Exception ex)
        {
            try { writer?.Dispose(); } catch { }
            try { bufferStream.Dispose(); } catch { }
            try { timer.Dispose(); } catch { }
            try { waveIn.Dispose(); } catch { }
            reg.Dispose();
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }
}
