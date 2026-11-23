using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace JarvisBot.UI;

public partial class FloatingOrbWindow : Window
{
    private Point _dragStartPoint;
    private readonly Action _onToggleHud;
    private readonly Action _onExitApp;
    private readonly Action _onToggleMute;
    private bool _isMuted = false;

    public FloatingOrbWindow(Action onToggleHud, Action onExitApp, Action onToggleMute)
    {
        InitializeComponent();
        _onToggleHud = onToggleHud;
        _onExitApp = onExitApp;
        _onToggleMute = onToggleMute;

        Loaded += FloatingOrbWindow_Loaded;
    }

    private void FloatingOrbWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position at bottom-right corner above taskbar
        var workingArea = SystemParameters.WorkArea;
        Left = workingArea.Right - Width - 30;
        Top = workingArea.Bottom - Height - 30;

        // Start animations
        if (Resources["RotateOuterRing"] is Storyboard rotateOuter)
            rotateOuter.Begin(this, true);

        if (Resources["RotateInnerRing"] is Storyboard rotateInner)
            rotateInner.Begin(this, true);

        if (Resources["PulseCoreGlow"] is Storyboard pulseCore)
            pulseCore.Begin(this, true);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);

        // Allow dragging the orb anywhere on the desktop
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Point current = e.GetPosition(this);

        // If moved less than 4 pixels, treat as click
        if (Math.Abs(current.X - _dragStartPoint.X) < 4 && Math.Abs(current.Y - _dragStartPoint.Y) < 4)
        {
            _onToggleHud();
        }
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var contextMenu = new ContextMenu();

        var openItem = new MenuItem { Header = "⚡ Open J.A.R.V.I.S. HUD" };
        openItem.Click += (s, args) => _onToggleHud();

        var muteItem = new MenuItem { Header = _isMuted ? "🔊 Unmute Microphone" : "🔇 Mute Microphone" };
        muteItem.Click += (s, args) => _onToggleMute();

        var resetPosItem = new MenuItem { Header = "📍 Reset Position" };
        resetPosItem.Click += (s, args) =>
        {
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 30;
            Top = workingArea.Bottom - Height - 30;
        };

        var exitItem = new MenuItem { Header = "❌ Shutdown J.A.R.V.I.S." };
        exitItem.Click += (s, args) => _onExitApp();

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(muteItem);
        contextMenu.Items.Add(resetPosItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        contextMenu.IsOpen = true;
    }

    public void UpdateAudioLevel(int level, bool isSpeech)
    {
        if (_isMuted) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted) return;
            float scale = 1.0f + (level / 100f) * 0.45f;
            PulseScale.ScaleX = scale;
            PulseScale.ScaleY = scale;
            AudioPulseRing.Opacity = isSpeech ? 0.9 : Math.Max(0.2, level / 100.0);
            CoreGlow.BlurRadius = isSpeech ? 28 : (14 + (level / 5));
        });
    }

    public void SetMutedVisualState(bool isMuted)
    {
        _isMuted = isMuted;
        Dispatcher.InvokeAsync(() =>
        {
            if (_isMuted)
            {
                PulseScale.ScaleX = 1.0;
                PulseScale.ScaleY = 1.0;
                AudioPulseRing.Opacity = 0.15;
                AudioPulseRing.Stroke = new SolidColorBrush(Color.FromRgb(255, 92, 92));
                ToolTip = "J.A.R.V.I.S. (MUTED - Click to open HUD | Right-click to Unmute)";
            }
            else
            {
                AudioPulseRing.Stroke = new SolidColorBrush(Color.FromRgb(0, 240, 255));
                ToolTip = "J.A.R.V.I.S. (Click to open HUD | Drag to reposition)";
            }
        });
    }
}
