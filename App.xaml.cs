using System.Windows;
using System.Windows.Media;
using JarvisBot.Configuration;
using JarvisBot.Services;
using JarvisBot.UI;

namespace JarvisBot;

public partial class App : Application
{
    private AudioService? _audioService;
    private AgentService? _agentService;
    private FloatingOrbWindow? _orbWindow;
    private JarvisHudWindow? _hudWindow;
    private bool _isMuted = false;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = AppSettings.Load();
        var config = settings.Jarvis;

        _audioService = new AudioService(config.SpeechConfidenceThreshold, config.SpeechRate);
        _agentService = new AgentService(config.OllamaEndpoint, config.ModelId, config.SystemPrompt);

        // Instantiate HUD Window
        _hudWindow = new JarvisHudWindow(
            onSendCommand: ExecuteCommandAsync,
            onMinimizeToOrb: () => _hudWindow?.Hide(),
            onExitApp: ShutdownApplication,
            onTestMic: ToggleMute
        );

        // Instantiate Floating Orb Window
        _orbWindow = new FloatingOrbWindow(
            onToggleHud: ToggleHudVisibility,
            onExitApp: ShutdownApplication,
            onToggleMute: ToggleMute
        );

        // Wire up live audio level tracking to both Orb and HUD
        _audioService.AudioLevelChanged += (level) =>
        {
            if (_isMuted) return;
            bool isSpeech = _audioService.CurrentAudioState == System.Speech.Recognition.AudioState.Speech;
            _orbWindow?.UpdateAudioLevel(level, isSpeech);
            _hudWindow?.UpdateAudioVisualizer(level, isSpeech);
        };

        // Wire up live transcription hypothesis
        _audioService.LiveSpeechHypothesized += (hypo) =>
        {
            if (_isMuted) return;
            _hudWindow?.ShowLiveHypothesis(hypo);
        };

        // Wire up voice activity start
        _audioService.VoiceActivityStarted += (pos) =>
        {
            if (!_isMuted)
            {
                _hudWindow?.SetStatus("LISTENING...", Color.FromRgb(0, 240, 255));
            }
        };

        // Wire up speech recognition event
        _audioService.SpeechDetected += async (command) =>
        {
            if (!_isMuted)
            {
                await ExecuteCommandAsync(command);
            }
        };

        // Show floating orb on desktop
        _orbWindow.Show();

        // Start voice recognition
        _audioService.StartListening();
        _audioService.Speak("All systems functional. Ready for instructions.");

        // Pre-warm / ensure Ollama background service is running
        Task.Run(async () =>
        {
            try
            {
                bool isRunning = await _agentService.EnsureOllamaRunningAsync();
                if (!isRunning)
                {
                    _hudWindow?.Dispatcher.InvokeAsync(() =>
                    {
                        _hudWindow?.AddAssistantMessage("Notice: Local Ollama AI daemon is offline. Direct commands (opening apps, typing, web search, system controls) remain active.");
                    });
                }
            }
            catch { }
        });
    }

    private void ToggleHudVisibility()
    {
        if (_hudWindow == null) return;

        if (_hudWindow.IsVisible)
        {
            _hudWindow.Hide();
        }
        else
        {
            _hudWindow.Show();
            _hudWindow.Activate();
        }
    }

    private void ToggleMute()
    {
        _isMuted = !_isMuted;
        if (_audioService != null)
        {
            _audioService.IsMuted = _isMuted;
        }

        _hudWindow?.SetMutedVisualState(_isMuted);
        _orbWindow?.SetMutedVisualState(_isMuted);

        if (_isMuted)
        {
            _hudWindow?.AddAssistantMessage("Microphone input paused. Voice recognition muted.");
        }
        else
        {
            _hudWindow?.AddAssistantMessage("Microphone listening resumed.");
        }
    }

    private async Task ExecuteCommandAsync(string command)
    {
        if (_agentService == null || _audioService == null) return;

        // Show user message in HUD
        _hudWindow?.AddUserMessage(command);
        _hudWindow?.SetStatus("THINKING...", Color.FromRgb(180, 100, 255));

        try
        {
            string reply = await _agentService.ProcessCommandAsync(command);

            _hudWindow?.SetStatus("SPEAKING...", Color.FromRgb(255, 190, 0));
            _hudWindow?.AddAssistantMessage(reply);
            _audioService.Speak(reply);
        }
        catch (Exception ex)
        {
            string errorMsg = $"Error executing instruction: {ex.Message}";
            _hudWindow?.AddAssistantMessage(errorMsg);
            _audioService.Speak("An error occurred while executing the instruction.");
        }
        finally
        {
            if (_isMuted)
            {
                _hudWindow?.SetStatus("MUTED", Color.FromRgb(255, 92, 92));
            }
            else
            {
                _hudWindow?.SetStatus("ONLINE", Color.FromRgb(0, 240, 255));
            }
        }
    }

    private void ShutdownApplication()
    {
        try { _hudWindow?.Hide(); } catch { }
        try { _orbWindow?.Hide(); } catch { }
        try { _hudWindow?.Close(); } catch { }
        try { _orbWindow?.Close(); } catch { }

        Task.Run(() =>
        {
            try { _audioService?.Dispose(); } catch { }
        });

        try { Shutdown(0); } catch { }
        Environment.Exit(0);
    }
}
