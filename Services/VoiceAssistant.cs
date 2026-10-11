using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using SoundPanel.ViewModels;
using Whisper.net;

namespace SoundPanel.Services;

public enum VoicePhase { Idle, Downloading, Listening, Thinking }

public sealed record VoiceOutcome(bool Ok, string Message);

/// <summary>Microphone -> offline Whisper -> command -> player. One instance is shared by all windows.</summary>
public sealed class VoiceAssistant : IDisposable
{
    private static readonly Regex NoiseTagRx = new(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", RegexOptions.Compiled);

    private readonly MainViewModel _vm;
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private CancellationTokenSource? _cts;
    private WhisperFactory? _factory;
    private VoicePhase _phase;

    public VoiceAssistant(MainViewModel vm) => _vm = vm;

    public event Action<VoicePhase>? PhaseChanged;
    public event Action<double>? LevelChanged;
    public event Action<string>? Heard;
    public event Action<VoiceOutcome>? Finished;

    public VoicePhase Phase => _phase;
    public int DownloadPercent { get; private set; }
    public bool IsBusy => _phase != VoicePhase.Idle;

    public Task RunOrCancelAsync()
    {
        if (IsBusy)
        {
            Cancel();
            return Task.CompletedTask;
        }
        return RunAsync();
    }

    public void Cancel() => _cts?.Cancel();

    private void SetPhase(VoicePhase phase)
    {
        _phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    private void Post(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.BeginInvoke(action);
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            if (!VoiceModel.IsReady)
            {
                DownloadPercent = 0;
                SetPhase(VoicePhase.Downloading);
                await VoiceModel.EnsureAsync(new Progress<int>(p =>
                {
                    DownloadPercent = p;
                    PhaseChanged?.Invoke(VoicePhase.Downloading);
                }), ct);
            }

            if (_factory == null)
            {
                SetPhase(VoicePhase.Thinking);
                _factory = await Task.Run(() => WhisperFactory.FromPath(VoiceModel.ModelPath), ct);
            }

            SetPhase(VoicePhase.Listening);
            var (pcm, peak) = await RecordAsync(level => Post(() => LevelChanged?.Invoke(level)), ct);
            if (pcm == null)
            {
                Finished?.Invoke(new VoiceOutcome(false,
                    $"მიკროფონი ხმას ვერ იჭერს (დონე {peak:0.000}). შეამოწმე Windows-ში default მიკროფონი"));
                return;
            }
            Boost(pcm);

            SetPhase(VoicePhase.Thinking);
            var text = await TranscribeAsync(pcm, BuildPrompt(), ct);
            if (text.Length == 0)
            {
                Finished?.Invoke(new VoiceOutcome(false, "ხმა ჩაიწერა, მაგრამ სიტყვები ვერ ამოვიცანი. ილაპარაკე ცოტა ხმამაღლა და მკაფიოდ"));
                return;
            }

            Heard?.Invoke(text);
            Finished?.Invoke(Execute(VoiceCommands.Parse(text), text));
        }
        catch (OperationCanceledException)
        {
            // cancelled by the user
        }
        catch (Exception ex)
        {
            Finished?.Invoke(new VoiceOutcome(false, $"ხმოვანი ბრძანება ვერ შესრულდა: {ex.Message}"));
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetPhase(VoicePhase.Idle);
        }
    }

    // ------------------------------------------------------------ recording

        private static async Task<(byte[]? Data, double Peak)> RecordAsync(Action<double> onLevel, CancellationToken ct)
    {
        using var waveIn = new WaveInEvent
        {
            DeviceNumber = VoiceMic.DeviceNumber(),
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 80
        };
        var data = new MemoryStream();
        var gate = new object();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var started = DateTime.UtcNow;
        var lastVoice = DateTime.MinValue;
        var heardVoice = false;
        double noiseSum = 0, peak = 0;
        var noiseCount = 0;
        var threshold = 0.006;

        waveIn.DataAvailable += (_, e) =>
        {
            lock (gate) data.Write(e.Buffer, 0, e.BytesRecorded);

            var level = Rms(e.Buffer, e.BytesRecorded);
            peak = Math.Max(peak, level);
            onLevel(Math.Min(1.0, level * 12));

            var now = DateTime.UtcNow;
            var elapsed = (now - started).TotalSeconds;

            if (elapsed < 0.3)
            {
                noiseSum += level;
                noiseCount++;
                threshold = Math.Clamp(noiseSum / noiseCount * 2.5, 0.006, 0.03);
                return;
            }

            if (level > threshold)
            {
                heardVoice = true;
                lastVoice = now;
            }
            else if (heardVoice && (now - lastVoice).TotalSeconds > 1.2)
            {
                tcs.TrySetResult(true);
            }

            if (!heardVoice && elapsed > 7) tcs.TrySetResult(false);
            if (elapsed > 12) tcs.TrySetResult(heardVoice);
        };

        using var registration = ct.Register(() => tcs.TrySetCanceled());
        waveIn.StartRecording();

        bool ok;
        try
        {
            ok = await tcs.Task;
        }
        finally
        {
            waveIn.StopRecording();
            onLevel(0);
        }

        lock (gate) return (ok ? data.ToArray() : null, peak);
    }

    /// <summary>Quiet microphones: amplify so the loudest sample sits at ~80%.</summary>
    private static void Boost(byte[] pcm)
    {
        var count = pcm.Length / 2;
        var max = 0;
        for (var i = 0; i < count; i++)
            max = Math.Max(max, Math.Abs((int)BitConverter.ToInt16(pcm, i * 2)));
        if (max < 200) return;

        var gain = Math.Min(20.0, 26000.0 / max);
        if (gain < 1.2) return;

        for (var i = 0; i < count; i++)
        {
            var v = (int)Math.Clamp(BitConverter.ToInt16(pcm, i * 2) * gain, short.MinValue, short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
    }

    private static double Rms(byte[] buffer, int count)
    {
        var samples = count / 2;
        if (samples == 0) return 0;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            var s = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            sum += s * s;
        }
        return Math.Sqrt(sum / samples);
    }

    // ------------------------------------------------------------ recognition

    private async Task<string> TranscribeAsync(byte[] pcm, string prompt, CancellationToken ct)
    {
        byte[] wav;
        using (var ms = new MemoryStream())
        {
            using (var writer = new WaveFileWriter(ms, new WaveFormat(16000, 16, 1)))
                writer.Write(pcm, 0, pcm.Length);
            wav = ms.ToArray();
        }

        using var processor = _factory!.CreateBuilder()
            .WithLanguage("en")
            .WithPrompt(prompt)
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 2, 2, 8))
            .Build();

        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(new MemoryStream(wav), ct))
            sb.Append(segment.Text);

        return NoiseTagRx.Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Hint for the recognizer: the command words and the names of the songs in the library.</summary>
    private string BuildPrompt()
    {
        var sb = new StringBuilder("Music player commands: play, next, previous, stop, shuffle on, repeat off, volume 120. Songs: ");
        foreach (var file in _vm.GetAllTrackFiles())
        {
            if (sb.Length > 500) break;
            var title = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();
            if (title.Length > 0) sb.Append(title).Append(". ");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ commands

    private VoiceOutcome Execute(VoiceCommand c, string heard)
    {
        switch (c.Kind)
        {
            case VoiceKind.Play:
                return PlayTitle(c.Arg);

            case VoiceKind.Resume:
                if (!_vm.IsPlaying) _vm.TogglePlay();
                return new VoiceOutcome(true, "ვაგრძელებ");

            case VoiceKind.Pause:
                if (_vm.IsPlaying) _vm.TogglePlay();
                return new VoiceOutcome(true, "გავაჩერე");

            case VoiceKind.Next:
                _vm.Next();
                return new VoiceOutcome(true, "შემდეგი სიმღერა");

            case VoiceKind.Previous:
                _vm.SeekTo(0); // otherwise "previous" would only restart the current song
                _vm.Previous();
                return new VoiceOutcome(true, "წინა სიმღერა");

            case VoiceKind.Shuffle:
            {
                var on = c.Flag ?? !_vm.Shuffle;
                _vm.Shuffle = on;
                return new VoiceOutcome(true, on ? "არეულად დაკვრა ჩართულია" : "არეულად დაკვრა გამორთულია");
            }

            case VoiceKind.Repeat:
            {
                var on = c.Flag ?? !_vm.Repeat;
                _vm.Repeat = on;
                return new VoiceOutcome(true, on ? "ავტოფლეი ჩართულია" : "ავტოფლეი გამორთულია");
            }

            case VoiceKind.VolumeSet:
            {
                var percent = Math.Clamp(c.Number, 0, 200);
                _vm.Volume = percent / 100.0;
                return new VoiceOutcome(true, $"ხმა: {percent}%");
            }

            case VoiceKind.VolumeUp:
            case VoiceKind.VolumeDown:
            {
                var step = (c.Number <= 0 ? 10 : c.Number) / 100.0;
                var volume = Math.Clamp(_vm.Volume + (c.Kind == VoiceKind.VolumeUp ? step : -step), 0, 2);
                _vm.Volume = Math.Round(volume, 2);
                return new VoiceOutcome(true, $"ხმა: {(int)Math.Round(volume * 100)}%");
            }

            case VoiceKind.Mute:
                if (_vm.Volume > 0) _vm.ToggleMute();
                return new VoiceOutcome(true, "ხმა გამორთულია");

            case VoiceKind.Unmute:
                if (_vm.Volume <= 0) _vm.ToggleMute();
                return new VoiceOutcome(true, "ხმა ჩართულია");

            default:
                return new VoiceOutcome(false, $"ვერ გავიგე: „{heard}“");
        }
    }

    private VoiceOutcome PlayTitle(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(FuzzySearch.Normalize)
            .Where(w => w.Length > 0)
            .ToArray();
        if (words.Length == 0) return new VoiceOutcome(false, "რა დავუკრა?");

        var files = _vm.GetAllTrackFiles();
        string? best = null;
        var bestScore = -1;
        var bestLength = int.MaxValue;

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var score = FuzzySearch.Score(words, FuzzySearch.Normalize(name));
            if (score > bestScore || (score == bestScore && name.Length < bestLength))
            {
                best = file;
                bestScore = score;
                bestLength = name.Length;
            }
        }

        if (best == null || bestScore < 400)
            return new VoiceOutcome(false, $"„{title}“ ვერ ვიპოვე");

        _vm.PlayFromLibrary(best, files);
        return new VoiceOutcome(true, "ვუკრავ: " + Path.GetFileNameWithoutExtension(best));
    }

    // ------------------------------------------------------------ hands-free ("Fifu ...")

    private CancellationTokenSource? _handsFreeCts;

    public bool HandsFree => _handsFreeCts != null;
    public event Action<bool>? HandsFreeChanged;

    public void SetHandsFree(bool on)
    {
        if (on == HandsFree) return;

        if (on)
        {
            _handsFreeCts = new CancellationTokenSource();
            _ = HandsFreeLoopAsync(_handsFreeCts);
        }
        else
        {
            _handsFreeCts!.Cancel();
            _handsFreeCts = null;
        }

        VoiceMic.HandsFree = on;
        HandsFreeChanged?.Invoke(on);
    }

    private async Task HandsFreeLoopAsync(CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            if (!VoiceModel.IsReady)
            {
                DownloadPercent = 0;
                SetPhase(VoicePhase.Downloading);
                await VoiceModel.EnsureAsync(new Progress<int>(p =>
                {
                    DownloadPercent = p;
                    PhaseChanged?.Invoke(VoicePhase.Downloading);
                }), ct);
            }

            if (_factory == null)
            {
                SetPhase(VoicePhase.Thinking);
                _factory = await Task.Run(() => WhisperFactory.FromPath(VoiceModel.ModelPath), ct);
            }
            SetPhase(VoicePhase.Idle);

            var queue = Channel.CreateBounded<byte[]>(
                new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest });

            using var waveIn = new WaveInEvent
            {
                DeviceNumber = VoiceMic.DeviceNumber(),
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 80
            };

            var speech = new MemoryStream();
            var previous = Array.Empty<byte>();
            var inSpeech = false;
            var started = DateTime.MinValue;
            var lastVoice = DateTime.MinValue;
            var noise = 0.002;

            waveIn.DataAvailable += (_, e) =>
            {
                var chunk = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, chunk, e.BytesRecorded);

                var level = Rms(chunk, chunk.Length);
                var threshold = Math.Clamp(noise * 3, 0.007, 0.05);
                var now = DateTime.UtcNow;

                if (level > threshold)
                {
                    if (!inSpeech)
                    {
                        inSpeech = true;
                        started = now;
                        speech.Write(previous, 0, previous.Length); // keep the very start of the word
                    }
                    lastVoice = now;
                }
                else if (!inSpeech)
                {
                    noise = noise * 0.97 + level * 0.03;
                }

                if (inSpeech)
                {
                    speech.Write(chunk, 0, chunk.Length);
                    var length = (now - started).TotalSeconds;
                    if ((now - lastVoice).TotalSeconds > 0.7 || length > 8)
                    {
                        inSpeech = false;
                        if (length > 0.5) queue.Writer.TryWrite(speech.ToArray());
                        speech.SetLength(0);
                    }
                }

                previous = chunk;
            };

            waveIn.StartRecording();
            try
            {
                await foreach (var pcm in queue.Reader.ReadAllAsync(ct))
                {
                    if (_phase != VoicePhase.Idle) continue; // a manual command is running

                    Boost(pcm);

                    string text;
                    try
                    {
                        text = await TranscribeAsync(pcm, "Hey Fifu. " + BuildPrompt(), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        continue;
                    }

                    var rest = VoiceCommands.StripWake(text, out var woke);
                    if (!woke) continue;

                    if (rest.Length > 0)
                    {
                        Heard?.Invoke(text);
                        Finished?.Invoke(Execute(VoiceCommands.Parse(rest), text));
                    }
                    else
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                        await RunAsync();
                    }
                }
            }
            finally
            {
                waveIn.StopRecording();
            }
        }
        catch (OperationCanceledException)
        {
            // switched off
        }
        catch (Exception ex)
        {
            Finished?.Invoke(new VoiceOutcome(false, $"ხმით გააქტიურება ვერ ჩაირთო: {ex.Message}"));
        }
        finally
        {
            if (_handsFreeCts == cts)
            {
                _handsFreeCts = null;
                VoiceMic.HandsFree = false;
                HandsFreeChanged?.Invoke(false);
            }
            if (_phase is VoicePhase.Downloading or VoicePhase.Thinking) SetPhase(VoicePhase.Idle);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _handsFreeCts?.Cancel();
        _factory?.Dispose();
    }
}