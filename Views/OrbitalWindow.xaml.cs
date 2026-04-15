using FlowTracker.Domain;
using FlowTracker.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace FlowTracker.Views;

public partial class OrbitalWindow : Window
{
    private const double WindowRadius = 160.0;
    private const double ActiveRadius = 128.0;
    private const double InnerRadius = 54.0;
    private const double OuterRadius = 136.0;
    private static readonly TimeSpan HideGrace = TimeSpan.FromMilliseconds(1600);
    private static readonly TimeSpan HideDebounce = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PassiveInactivityTimeout = TimeSpan.FromSeconds(2.2);
    private const double HideDistanceThreshold = 180.0;
    private const double PassiveOpacity = 0.58;
    private const double InteractiveOpacity = 1.0;

    private bool _isVisible;
    private bool _isAnimating;
    private int _anchorX;
    private int _anchorY;
    private DateTimeOffset _shownAtUtc;
    private DateTimeOffset? _outsideSinceUtc;
    private double _lastDistanceFromAnchor;
    private OrbitalHideReason _pendingHideReason = OrbitalHideReason.Unknown;
    private int _quickDismissStreak;
    private bool _hasAnyInteraction;
    private readonly DispatcherTimer _proximityTimer;
    private IReadOnlyList<RadialMenuItemDefinition> _rootItems = [];
    private IReadOnlyList<RadialMenuItemDefinition> _currentItems = [];
    private readonly Stack<(IReadOnlyList<RadialMenuItemDefinition> Items, string Title)> _menuStack = [];

    public bool IsReadyForInteraction => _isVisible && !_isAnimating;

    public OrbitalWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        _proximityTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _proximityTimer.Tick += OnProximityTick;
        Hide();
    }

    public event EventHandler<RadialMenuInvokedEventArgs>? MenuItemInvoked;
    public event EventHandler<OrbitalOverlayClosedEventArgs>? OverlayClosed;

    public void ShowAt(int x, int y, IReadOnlyList<RadialMenuItemDefinition> rootItems)
    {
        _anchorX = x;
        _anchorY = y;
        _rootItems = rootItems;
        _menuStack.Clear();
        RenderMenu(_rootItems, "Aktion");

        Left = x - WindowRadius;
        Top = y - WindowRadius;

        if (_isVisible || _isAnimating)
        {
            OverlayTelemetry?.Invoke(this, new(
                OrbitalTelemetryEventKind.ShowSkippedAlreadyVisible,
                _anchorX,
                _anchorY,
                ItemCount: rootItems.Count,
                VisibleDuration: TimeSpan.Zero,
                DistanceFromAnchor: _lastDistanceFromAnchor,
                QuickDismissStreak: _quickDismissStreak,
                Details: "ShowAt ignored because overlay is already visible or animating."));
            return;
        }

        _isAnimating = true;
        _outsideSinceUtc = null;
        _hasAnyInteraction = false;
        Show();
        SetClickThrough(false);

        var storyboard = (Storyboard)Resources["ShowStoryboard"];
        storyboard.Completed -= OnShowCompleted;
        storyboard.Completed += OnShowCompleted;
        storyboard.Begin();
    }

    public void HideOverlay(OrbitalHideReason reason = OrbitalHideReason.Unknown)
    {
        if (!_isVisible || _isAnimating)
        {
            return;
        }

        _pendingHideReason = reason;
        _isAnimating = true;
        var storyboard = (Storyboard)Resources["HideStoryboard"];
        storyboard.Completed -= OnHideCompleted;
        storyboard.Completed += OnHideCompleted;
        storyboard.Begin();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (!_isVisible)
        {
            SetClickThrough(true);
        }
    }

    private void OnShowCompleted(object? sender, EventArgs e)
    {
        var storyboard = (Storyboard)Resources["ShowStoryboard"];
        storyboard.Completed -= OnShowCompleted;
        _isAnimating = false;
        _isVisible = true;
        _shownAtUtc = DateTimeOffset.UtcNow;
        _outsideSinceUtc = null;
        Root.Opacity = PassiveOpacity;
        _proximityTimer.Start();
    }

    public event EventHandler<OrbitalTelemetryEventArgs>? OverlayTelemetry;

    private void OnHideCompleted(object? sender, EventArgs e)
    {
        var storyboard = (Storyboard)Resources["HideStoryboard"];
        storyboard.Completed -= OnHideCompleted;

        var hideReason = _pendingHideReason;
        _pendingHideReason = OrbitalHideReason.Unknown;
        var visibleDuration = DateTimeOffset.UtcNow - _shownAtUtc;
        var isQuickDismiss = visibleDuration < TimeSpan.FromSeconds(1.8) && hideReason == OrbitalHideReason.PointerLeftThreshold;
        _quickDismissStreak = isQuickDismiss
            ? Math.Min(_quickDismissStreak + 1, 5)
            : Math.Max(_quickDismissStreak - 1, 0);

        Hide();
        SetClickThrough(true);
        _proximityTimer.Stop();
        _isAnimating = false;
        _isVisible = false;
        _outsideSinceUtc = null;
        OverlayTelemetry?.Invoke(this, new(
            OrbitalTelemetryEventKind.Hidden,
            _anchorX,
            _anchorY,
            ItemCount: _currentItems.Count,
            VisibleDuration: visibleDuration,
            DistanceFromAnchor: _lastDistanceFromAnchor,
            QuickDismissStreak: _quickDismissStreak,
            Details: $"reason={hideReason}"));
        OverlayClosed?.Invoke(this, new(
            hideReason,
            visibleDuration,
            _lastDistanceFromAnchor,
            _quickDismissStreak));
    }

    private void OnProximityTick(object? sender, EventArgs e)
    {
        if (!_isVisible || _isAnimating)
        {
            return;
        }

        if (!NativeMethods.TryGetCursorPosition(out var point))
        {
            return;
        }

        var dx = point.X - _anchorX;
        var dy = point.Y - _anchorY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        _lastDistanceFromAnchor = distance;

        if (!_hasAnyInteraction && DateTimeOffset.UtcNow - _shownAtUtc >= PassiveInactivityTimeout)
        {
            HideOverlay(OrbitalHideReason.InactivityTimeout);
            return;
        }

        if (DateTimeOffset.UtcNow - _shownAtUtc < EffectiveHideGrace)
        {
            return;
        }

        if (distance <= ActiveRadius)
        {
            _outsideSinceUtc = null;
            return;
        }

        if (distance <= HideDistanceThreshold)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _outsideSinceUtc ??= now;

        if (now - _outsideSinceUtc >= EffectiveHideDebounce)
        {
            HideOverlay(OrbitalHideReason.PointerLeftThreshold);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterInteraction();

        if (_menuStack.Count == 0)
        {
            return;
        }

        var previous = _menuStack.Pop();
        PlayDialTransitionAnimation();
        RenderMenu(previous.Items, previous.Title);
    }

    private void RenderMenu(IReadOnlyList<RadialMenuItemDefinition> items, string title)
    {
        _currentItems = items;
        CenterTitleText.Text = title;
        BackButton.Visibility = _menuStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SegmentCanvas.Children.Clear();
        if (items.Count == 0)
        {
            return;
        }

        var centerX = SegmentCanvas.Width / 2;
        var centerY = SegmentCanvas.Height / 2;
        var angleStep = 360.0 / items.Count;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var start = -90 + i * angleStep;
            var end = start + angleStep;

            var path = new Path
            {
                Data = CreateWedgeGeometry(centerX, centerY, InnerRadius, OuterRadius, start, end),
                Fill = new SolidColorBrush(ParseColor(item.ColorHex)),
                Stroke = new SolidColorBrush(MediaColor.FromArgb(170, 255, 255, 255)),
                StrokeThickness = 1,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = item
            };
            path.MouseEnter += Segment_MouseEnter;
            path.MouseLeave += Segment_MouseLeave;
            path.MouseLeftButtonUp += Segment_MouseLeftButtonUp;
            SegmentCanvas.Children.Add(path);

            var mid = DegreesToRadians((start + end) / 2);
            var labelRadius = (InnerRadius + OuterRadius) / 2;
            var labelX = centerX + Math.Cos(mid) * labelRadius;
            var labelY = centerY + Math.Sin(mid) * labelRadius;

            var label = new TextBlock
            {
                Text = item.Children.Count > 0 ? $"{item.Label} >" : item.Label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Width = 86,
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, labelX - 43);
            Canvas.SetTop(label, labelY - 12);
            SegmentCanvas.Children.Add(label);
        }
    }

    private static MediaColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return MediaColor.FromRgb(51, 65, 85);
        }

        try
        {
            return (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return MediaColor.FromRgb(51, 65, 85);
        }
    }

    private void PlayDialTransitionAnimation()
    {
        var pulse = new DoubleAnimation
        {
            From = 0.96,
            To = 1.05,
            Duration = TimeSpan.FromMilliseconds(120),
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void Segment_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RegisterInteraction();

        if (sender is Path path)
        {
            path.StrokeThickness = 2.4;
            path.Stroke = System.Windows.Media.Brushes.White;
        }
    }

    private void Segment_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Path path)
        {
            path.StrokeThickness = 1;
            path.Stroke = new SolidColorBrush(MediaColor.FromArgb(170, 255, 255, 255));
        }
    }

    private void Segment_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RegisterInteraction();

        if (sender is not Path { Tag: RadialMenuItemDefinition item })
        {
            return;
        }

        if (item.Children.Count > 0)
        {
            _menuStack.Push((_currentItems, CenterTitleText.Text));
            PlayDialTransitionAnimation();
            RenderMenu(item.Children, item.Label);
            e.Handled = true;
            return;
        }

        MenuItemInvoked?.Invoke(this, new(item.ActionKey, item.Label, _anchorX, _anchorY, DateTimeOffset.UtcNow));
        HideOverlay(OrbitalHideReason.ItemInvoked);
        e.Handled = true;
    }

    private TimeSpan EffectiveHideGrace => HideGrace + TimeSpan.FromMilliseconds(300 * _quickDismissStreak);
    private TimeSpan EffectiveHideDebounce => HideDebounce + TimeSpan.FromMilliseconds(120 * _quickDismissStreak);

    private void RegisterInteraction()
    {
        if (_hasAnyInteraction)
        {
            return;
        }

        _hasAnyInteraction = true;
        var opacityAnimation = new DoubleAnimation
        {
            To = InteractiveOpacity,
            Duration = TimeSpan.FromMilliseconds(130),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Root.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
    }

    private static Geometry CreateWedgeGeometry(double cx, double cy, double innerRadius, double outerRadius, double startDeg, double endDeg)
    {
        var startOuter = Polar(cx, cy, outerRadius, startDeg);
        var endOuter = Polar(cx, cy, outerRadius, endDeg);
        var startInner = Polar(cx, cy, innerRadius, startDeg);
        var endInner = Polar(cx, cy, innerRadius, endDeg);
        var largeArc = Math.Abs(endDeg - startDeg) > 180;

        var figure = new PathFigure { StartPoint = startOuter, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(endOuter, new System.Windows.Size(outerRadius, outerRadius), 0, largeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(endInner, true));
        figure.Segments.Add(new ArcSegment(startInner, new System.Windows.Size(innerRadius, innerRadius), 0, largeArc, SweepDirection.Counterclockwise, true));

        return new PathGeometry([figure]);
    }

    private static System.Windows.Point Polar(double cx, double cy, double radius, double angleDeg)
    {
        var rad = DegreesToRadians(angleDeg);
        return new System.Windows.Point(cx + radius * Math.Cos(rad), cy + radius * Math.Sin(rad));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExstyle).ToInt64();
        var newStyle = enabled
            ? exStyle | NativeMethods.WsExTransparent
            : exStyle & ~NativeMethods.WsExTransparent;

        if (newStyle != exStyle)
        {
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExstyle, (nint)newStyle);
        }
    }
}

public readonly record struct RadialMenuInvokedEventArgs(
    string ActionKey,
    string Label,
    int AnchorX,
    int AnchorY,
    DateTimeOffset TimestampUtc);

public enum OrbitalHideReason
{
    Unknown,
    PointerLeftThreshold,
    ItemInvoked,
    InactivityTimeout,
    AppForced,
    GhostMode
}

public enum OrbitalTelemetryEventKind
{
    ShowSkippedAlreadyVisible,
    Hidden
}

public readonly record struct OrbitalTelemetryEventArgs(
    OrbitalTelemetryEventKind EventKind,
    int AnchorX,
    int AnchorY,
    int ItemCount,
    TimeSpan VisibleDuration,
    double DistanceFromAnchor,
    int QuickDismissStreak,
    string Details);

public readonly record struct OrbitalOverlayClosedEventArgs(
    OrbitalHideReason Reason,
    TimeSpan VisibleDuration,
    double LastDistanceFromAnchor,
    int QuickDismissStreak);
