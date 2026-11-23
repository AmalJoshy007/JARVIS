using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;

namespace JarvisBot.Services;

public class AudioService : IDisposable
{
    private readonly SpeechRecognitionEngine? _recognizer;
    private readonly SpeechSynthesizer _synthesizer;
    private readonly float _confidenceThreshold;

    public event Action<string>? SpeechDetected;
    public event Action<int>? AudioLevelChanged;
    public event Action<AudioState>? AudioStateChanged;
    public event Action<string>? LiveSpeechHypothesized;
    public event Action<TimeSpan>? VoiceActivityStarted;

    private volatile bool _isMuted = false;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_isMuted)
            {
                CurrentAudioLevel = 0;
                AudioLevelChanged?.Invoke(0);
                StopSpeaking();
            }
        }
    }

    public int CurrentAudioLevel { get; private set; }
    public AudioState CurrentAudioState { get; private set; } = AudioState.Silence;

    public AudioService(float confidenceThreshold = 0.30f, int speechRate = 1)
    {
        _confidenceThreshold = confidenceThreshold;
        _synthesizer = new SpeechSynthesizer();
        _synthesizer.Rate = speechRate;

        try
        {
            // Select installed recognizer (prioritizes English - India if installed, then English)
            var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(r => r.Culture.Name.Equals("en-IN", StringComparison.OrdinalIgnoreCase))
                ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.Name.StartsWith("en-IN", StringComparison.OrdinalIgnoreCase))
                ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();

            if (recognizerInfo != null)
            {
                _recognizer = new SpeechRecognitionEngine(recognizerInfo);
                Console.WriteLine($"[AudioService]: Initialized recognizer: {recognizerInfo.Description} ({recognizerInfo.Culture.Name})");
            }
            else
            {
                _recognizer = new SpeechRecognitionEngine();
                Console.WriteLine($"[AudioService]: Initialized default recognizer: {_recognizer.RecognizerInfo.Description}");
            }

            // 1. Build and load Command Grammar for high-confidence voice actions
            LoadCommandGrammars(_recognizer);

            // 2. Load Dictation Grammar with lower weight so SAPI strongly prefers assistant commands
            try
            {
                var dictation = new DictationGrammar { Name = "FreeformDictation", Weight = 0.2f };
                _recognizer.LoadGrammar(dictation);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioService Notice]: Dictation grammar could not be loaded: {ex.Message}");
            }

            // Live Audio Level & Signal Tracking
            _recognizer.AudioLevelUpdated += (s, e) =>
            {
                if (IsMuted) return;
                CurrentAudioLevel = e.AudioLevel;
                AudioLevelChanged?.Invoke(e.AudioLevel);
            };

            _recognizer.SpeechDetected += (s, e) =>
            {
                if (IsMuted) return;
                VoiceActivityStarted?.Invoke(e.AudioPosition);
            };

            _recognizer.AudioStateChanged += (s, e) =>
            {
                CurrentAudioState = e.AudioState;
                AudioStateChanged?.Invoke(e.AudioState);
            };

            _recognizer.SpeechHypothesized += (s, e) =>
            {
                if (IsMuted) return;
                string text = e.Result?.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    LiveSpeechHypothesized?.Invoke(text);
                }
            };

            _recognizer.SpeechRecognized += (s, e) =>
            {
                if (IsMuted) return;
                float confidence = e.Result.Confidence;
                string text = e.Result.Text?.Trim() ?? string.Empty;
                string grammar = e.Result.Grammar?.Name ?? "Unknown";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[Voice Recognized]: \"{text}\" (Confidence: {confidence:P0}, Grammar: {grammar})");
                Console.ResetColor();

                // Lower threshold for explicit command grammars (improves accented speech tolerance)
                float effectiveThreshold = grammar.Contains("Command") || grammar.Contains("Phrases")
                    ? Math.Min(_confidenceThreshold, 0.18f)
                    : _confidenceThreshold;

                if (confidence >= effectiveThreshold && !string.IsNullOrWhiteSpace(text))
                {
                    SpeechDetected?.Invoke(text);
                }
                else
                {
                    Console.WriteLine($"[AudioService]: Dropped due to confidence threshold ({confidence:P0} < {effectiveThreshold:P0}).");
                }
            };

            _recognizer.SpeechRecognitionRejected += (s, e) =>
            {
                if (IsMuted) return;
                string text = e.Result?.Text?.Trim() ?? string.Empty;
                float conf = e.Result?.Confidence ?? 0f;
                string grammar = e.Result?.Grammar?.Name ?? "Unknown";

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"\n[Mic Low Confidence]: \"{text}\" (Confidence: {conf:P0}, Grammar: {grammar})");
                    Console.ResetColor();

                    // If it matched a known command even with lower confidence (0.15+), execute on best-effort
                    if (conf >= 0.15f && text.Length > 2)
                    {
                        Console.WriteLine($"[AudioService]: Accepting accent-tolerant match: \"{text}\"");
                        SpeechDetected?.Invoke(text);
                    }
                }
            };

            _recognizer.AudioSignalProblemOccurred += (s, e) =>
            {
                if (e.AudioSignalProblem != AudioSignalProblem.None)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[Mic Warning]: Audio signal issue detected: {e.AudioSignalProblem}. (Ensure microphone is unmuted and speak clearly).");
                    Console.ResetColor();
                }
            };

            _recognizer.SetInputToDefaultAudioDevice();
            Console.WriteLine($"[AudioService]: Default recording device connected. Sensitivity threshold: {_confidenceThreshold:P0}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService Error]: Failed to bind microphone: {ex.Message}");
            Console.WriteLine("[AudioService]: Voice input via microphone is unavailable. You can type commands in the console.");
            _recognizer = null;
        }
    }

    private void LoadCommandGrammars(SpeechRecognitionEngine engine)
    {
        try
        {
            // Commands: Open / Launch apps
            var verbs = new Choices("open", "launch", "start", "run", "please open", "please launch", "open the");
            var apps = new Choices(
                "notepad", "note pad", "calculator", "calc", "calculation", "chrome", "google chrome",
                "spotify", "files", "file explorer", "explorer", "terminal", "command prompt", "cmd",
                "youtube", "you tube", "browser", "settings");

            var openGb = new GrammarBuilder();
            openGb.Append(verbs);
            openGb.Append(apps);
            var appGrammar = new Grammar(openGb) { Name = "AppCommands", Weight = 1.0f };
            engine.LoadGrammar(appGrammar);

            // Direct phrases with accent-tolerant common patterns
            var phrases = new Choices(
                "open notepad", "open the notepad", "open note pad", "launch notepad", "start notepad",
                "open calculator", "open the calculator", "launch calculator", "open calc", "open calculation",
                "open youtube", "open the youtube", "open you tube", "play youtube",
                "open chrome", "open google chrome", "open browser",
                "lock workstation", "lock screen", "lock the screen", "lock computer", "lock pc", "lock my screen", "lock my pc",
                "what time is it", "what is the time", "tell me the time", "current time", "time please", "time now",
                "what date is it", "what is today's date", "today's date",
                "search the web", "search google",
                "mute", "unmute", "mute microphone", "unmute microphone",
                "hey jarvis", "hello jarvis", "how are you", "exit", "quit"
            );
            var directGrammar = new Grammar(new GrammarBuilder(phrases)) { Name = "DirectPhrases", Weight = 1.0f };
            engine.LoadGrammar(directGrammar);

            // Web search grammar
            var searchVerbs = new Choices("search the web for", "search for", "google", "search");
            var searchGb = new GrammarBuilder();
            searchGb.Append(searchVerbs);
            searchGb.AppendDictation();
            var searchGrammar = new Grammar(searchGb) { Name = "WebSearchCommands", Weight = 0.8f };
            engine.LoadGrammar(searchGrammar);

            // Open and type grammar
            var openAndTypeChoices = new Choices(
                "open notepad and type",
                "open notepad then type",
                "open notepad type",
                "open note pad and type",
                "open notepad and write",
                "in notepad type",
                "in notepad write",
                "type into notepad",
                "write into notepad",
                "type",
                "write"
            );
            var openAndTypeGb = new GrammarBuilder();
            openAndTypeGb.Append(openAndTypeChoices);
            openAndTypeGb.AppendDictation();
            var typeGrammar = new Grammar(openAndTypeGb) { Name = "OpenAndTypeCommands", Weight = 1.0f };
            engine.LoadGrammar(typeGrammar);

            Console.WriteLine("[AudioService]: High-confidence command grammars loaded (Apps, Typing, System Controls, Queries).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService Notice]: Command grammar loading notice: {ex.Message}");
        }
    }

    public void RunInteractiveMicTest()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("          LIVE MICROPHONE AUDIO DETECTION TEST         ");
        Console.WriteLine("=======================================================");
        Console.WriteLine("Speak, blow, or tap your microphone to see live audio levels.");
        Console.WriteLine("Press any key to exit live test mode.\n");

        bool stop = false;
        var cancellationSource = new CancellationTokenSource();

        var keyTask = Task.Run(() =>
        {
            try
            {
                Console.ReadKey(true);
            }
            catch { }
            stop = true;
        });

        int maxLevel = 0;
        while (!stop && !keyTask.IsCompleted)
        {
            int level = CurrentAudioLevel;
            if (level > maxLevel) maxLevel = level;

            int barWidth = 30;
            int filled = Math.Clamp((level * barWidth) / 100, 0, barWidth);
            string bar = new string('█', filled) + new string('░', barWidth - filled);

            string stateText;
            if (CurrentAudioState == AudioState.Speech)
            {
                stateText = "🎙️ [VOICE ACTIVE]";
            }
            else if (level > 8)
            {
                stateText = "🔊 [SOUND DETECTED]";
            }
            else
            {
                stateText = "💤 [SILENT]";
            }

            Console.Write($"\rLive Level: [{bar}] {level,3}%  (Peak: {maxLevel,3}%)  {stateText}   ");
            Thread.Sleep(50);
        }

        Console.WriteLine("\n\nLive test ended. Returning to J.A.R.V.I.S. Core...\n");
    }

    public void StartListening()
    {
        if (_recognizer != null)
        {
            try
            {
                _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                Console.WriteLine("[AudioService]: Listening actively on default microphone. Speak into your mic...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioService Error]: Could not start continuous recognition: {ex.Message}");
            }
        }
    }

    public void Speak(string text)
    {
        if (IsMuted)
        {
            Console.WriteLine($"[J.A.R.V.I.S. (Muted)]: {text}");
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[J.A.R.V.I.S.]: {text}");
        Console.ResetColor();
        try
        {
            _synthesizer.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AudioService Warning]: Voice output failed: {ex.Message}");
        }
    }

    public void StopSpeaking()
    {
        try
        {
            _synthesizer.SpeakAsyncCancelAll();
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            _recognizer?.RecognizeAsyncCancel();
            _recognizer?.Dispose();
        }
        catch { }

        try
        {
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.Dispose();
        }
        catch { }
    }
}
