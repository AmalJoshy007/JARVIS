using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JarvisBot.UI;

public partial class JarvisHudWindow : Window
{
    private readonly Func<string, Task> _onSendCommand;
    private readonly Action _onMinimizeToOrb;
    private readonly Action _onExitApp;
    private readonly Action _onTestMic;

    private readonly List<Rectangle> _equalizerBars = new();
    private readonly Random _random = new();
    private bool _isMuted = false;

    public JarvisHudWindow(
        Func<string, Task> onSendCommand,
        Action onMinimizeToOrb,
        Action onExitApp,
        Action onTestMic)
    {
        InitializeComponent();
        _onSendCommand = onSendCommand;
        _onMinimizeToOrb = onMinimizeToOrb;
        _onExitApp = onExitApp;
        _onTestMic = onTestMic;

        InitializeEqualizer();

        Loaded += JarvisHudWindow_Loaded;
    }

    private void JarvisHudWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position window on the right side of the screen
        var workingArea = SystemParameters.WorkArea;
        Left = workingArea.Right - Width - 30;
        Top = workingArea.Bottom - Height - 30;
    }

    private void InitializeEqualizer()
    {
        EqualizerPanel.Children.Clear();
        _equalizerBars.Clear();

        // 16 animated neon equalizer bars
        for (int i = 0; i < 16; i++)
        {
            var bar = new Rectangle
            {
                Width = 6,
                Height = 4,
                Fill = new SolidColorBrush(Color.FromRgb(0, 240, 255)),
                Margin = new Thickness(2, 0, 2, 0),
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = 0.8
            };
            EqualizerPanel.Children.Add(bar);
            _equalizerBars.Add(bar);
        }
    }

    public void UpdateAudioVisualizer(int level, bool isSpeech)
    {
        if (_isMuted) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted) return;
            AudioPercentText.Text = $"{level}%";

            // Modulate heights of equalizer bars
            for (int i = 0; i < _equalizerBars.Count; i++)
            {
                var bar = _equalizerBars[i];
                double factor = Math.Sin((i + 1) * 0.4) * 0.3 + 0.7;
                double targetHeight = isSpeech
                    ? Math.Clamp((level * 0.35 * factor) + _random.Next(4, 16), 4, 28)
                    : Math.Clamp((level * 0.2 * factor) + 3, 3, 20);

                bar.Height = targetHeight;
                bar.Fill = isSpeech 
                    ? new SolidColorBrush(Color.FromRgb(0, 240, 255)) 
                    : new SolidColorBrush(Color.FromArgb(180, 0, 119, 254));
            }
        });
    }

    public void SetStatus(string status, Color color)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted && status == "LISTENING...") return;
            StatusText.Text = status;
            StatusText.Foreground = new SolidColorBrush(color);
            StatusBadge.BorderBrush = new SolidColorBrush(color);
            StatusBadge.Background = new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B));
        });
    }

    public void SetMutedVisualState(bool isMuted)
    {
        _isMuted = isMuted;
        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted)
            {
                AudioPercentText.Text = "MUTED";
                AudioPercentText.Foreground = new SolidColorBrush(Color.FromRgb(255, 92, 92));
                for (int i = 0; i < _equalizerBars.Count; i++)
                {
                    _equalizerBars[i].Height = 3;
                    _equalizerBars[i].Fill = new SolidColorBrush(Color.FromArgb(80, 255, 92, 92));
                }
                MicIconText.Text = "🔇";
                MicButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 92, 92));
                MicButton.Background = new SolidColorBrush(Color.FromRgb(58, 20, 26));
                MicButton.ToolTip = "Microphone Muted (Click to Unmute)";
                HypothesisBanner.Visibility = Visibility.Collapsed;

                SetStatus("MUTED", Color.FromRgb(255, 92, 92));
            }
            else
            {
                AudioPercentText.Text = "0%";
                AudioPercentText.Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255));
                for (int i = 0; i < _equalizerBars.Count; i++)
                {
                    _equalizerBars[i].Height = 4;
                    _equalizerBars[i].Fill = new SolidColorBrush(Color.FromArgb(180, 0, 119, 254));
                }
                MicIconText.Text = "🎙️";
                MicButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 240, 255));
                MicButton.Background = new SolidColorBrush(Color.FromRgb(29, 42, 66));
                MicButton.ToolTip = "Microphone Active (Click to Mute)";

                SetStatus("ONLINE", Color.FromRgb(0, 240, 255));
            }
        });
    }

    public void ShowLiveHypothesis(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted || string.IsNullOrWhiteSpace(text))
            {
                HypothesisBanner.Visibility = Visibility.Collapsed;
            }
            else
            {
                HypothesisText.Text = text;
                HypothesisBanner.Visibility = Visibility.Visible;
            }
        });
    }

    public void AddUserMessage(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ShowLiveHypothesis(string.Empty);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(21, 35, 59)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 240, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(40, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "YOU",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            stack.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                FontSize = 13,
                LineHeight = 18
            });

            border.Child = stack;
            MessagesPanel.Children.Add(border);
            TranscriptScrollViewer.ScrollToEnd();
        });
    }

    public void AddAssistantMessage(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(19, 29, 46)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 240, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 40, 10),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var stack = new StackPanel();
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(new TextBlock
            {
                Text = "⚡ J.A.R.V.I.S.",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255))
            });

            stack.Children.Add(header);
            stack.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                FontSize = 13,
                LineHeight = 18
            });

            border.Child = stack;
            MessagesPanel.Children.Add(border);
            TranscriptScrollViewer.ScrollToEnd();
        });
    }

    public void AddActionNotification(string actionText)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 0, 240, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 240, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            badge.Child = new TextBlock
            {
                Text = $"🚀 {actionText}",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255))
            };

            MessagesPanel.Children.Add(badge);
            TranscriptScrollViewer.ScrollToEnd();
        });
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeToOrb_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _onMinimizeToOrb();
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _onExitApp();
    }

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        _onTestMic();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SubmitInput();
    }

    private async void CommandInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SubmitInput();
        }
    }

    private async Task SubmitInput()
    {
        string text = CommandInput.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
        {
            CommandInput.Text = string.Empty;
            await _onSendCommand(text);
        }
    }

    private async void QuickChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string command)
        {
            await _onSendCommand(command);
        }
    }
}
