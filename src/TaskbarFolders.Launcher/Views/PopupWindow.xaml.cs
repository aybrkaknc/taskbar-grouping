using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarFolders.Core.Interop;
using TaskbarFolders.Launcher.Services;
using TaskbarFolders.Launcher.ViewModels;
using TaskbarFolders.Shared.Configuration;
using TaskbarFolders.Shared.Models;

namespace TaskbarFolders.Launcher.Views;

/// <summary>
/// Popup window displayed when a taskbar group is clicked. Reads the user's popup-position
/// preference from the injected <see cref="AppSettings"/>, places itself anchored on the
/// cursor position at click time, plays a fade+scale open animation, and dismisses on focus
/// loss.
/// </summary>
/// <remarks>
/// v0.3+: chrome is fully transparent — no acrylic backdrop, no border, no shadow. The
/// previous TryEnableAcrylic path was removed; only the per-tile hover highlight is visible.
/// <see cref="AppSettings"/> is injected directly rather than re-loaded via
/// <see cref="IAppSettingsStore"/>; App.OnStartup loads once and registers the instance as a
/// singleton.
/// </remarks>
public sealed partial class PopupWindow : Window, IDisposable
{
    /// <summary>Outer padding on the popup Border in DIPs.</summary>
    private const int PaddingPx = 12;

    /// <summary>
    /// Interval of the never-activated fallback timer. Long enough that a user who is
    /// mousing towards the popup is not interrupted, short enough that a popup Windows
    /// refused to activate cannot linger as an orphaned Topmost window.
    /// </summary>
    private static readonly TimeSpan _activationFallbackInterval = TimeSpan.FromSeconds(3);

    private readonly PopupViewModel _viewModel;
    private readonly ITaskbarPositionHelper _positionHelper;
    private readonly AppSettings _settings;
    private readonly bool _useSystemBackdrop;
    private OutsideClickWatcher? _outsideClicks;
    private TileReorderDrag? _reorder;

    // True while a dialog owned by the popup (the add-apps picker) is open. Clicks in it land
    // outside the popup and it takes the focus, so every dismissal trigger has to stand down.
    private bool _dismissSuspended;
    private DispatcherTimer? _closeSafetyTimer;
    private bool _closeAnimating;
    private bool _closeConfirmed;
    private DispatcherTimer? _safetyTimer;
    private DispatcherTimer? _activationFallbackTimer;
    private bool _wasActivated;

    /// <summary>
    /// Raised on the UI thread for every press outside the popup while it may be dismissed,
    /// with the press position in device pixels. <see cref="App"/> sets
    /// <see cref="OutsidePressEventArgs.IsOnOpeningTile"/> for a press on the group's own tile
    /// (see <see cref="TileToggle"/>), which closes the popup even while it is pinned.
    /// </summary>
    public event EventHandler<OutsidePressEventArgs>? OutsidePress;

    /// <summary>
    /// Whether a click elsewhere, losing the focus or the never-activated fallback may close
    /// the popup: not while a dialog it owns is open, and not while it is pinned.
    /// </summary>
    public bool DismissesOnOutsideInteraction => !_dismissSuspended && !_viewModel.IsPinned;

    /// <summary>Initializes a new instance of the <see cref="PopupWindow"/> class.</summary>
    public PopupWindow(
        PopupViewModel viewModel,
        ITaskbarPositionHelper positionHelper,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(positionHelper);
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _viewModel = viewModel;
        _positionHelper = positionHelper;
        _settings = settings;

        // In edit mode a tile can be dragged to another place in the grid.
        _reorder = new TileReorderDrag(TileList, TileScroller, viewModel, settings.EnableAnimations);
        DataContext = viewModel;

        // A system backdrop is composed by DWM behind the window, and DWM never paints one
        // behind a layered window — AllowsTransparency=True is exactly that. So the window
        // mode has to be chosen here, before the HWND exists. Where the backdrop is
        // available the popup becomes an ordinary window whose frame covers the whole
        // client area (the WindowChrome below), and DWM fills it with acrylic. Elsewhere it
        // stays layered and ChromeRoot paints a translucent fallback panel instead.
        _useSystemBackdrop = WindowBackdrop.IsSupported;
        if (_useSystemBackdrop)
        {
            AllowsTransparency = false;
            ResizeMode = ResizeMode.NoResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                CornerRadius = default,
                UseAeroCaptionButtons = false,
            });
        }
        else
        {
            ChromeRoot.SetResourceReference(Border.BackgroundProperty, "PopupFallbackBackground");
            ChromeRoot.CornerRadius = new CornerRadius(8);
        }

        _viewModel.LaunchSucceeded += OnLaunchSucceeded;
        _viewModel.CloseRequested += OnCloseRequested;
        // LastError toggles the docked strip after open; height must grow/shrink with it
        // or the strip clips the fixed-size tiles (issue #24).
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Apps.CollectionChanged += OnAppsChanged;
        Activated += OnActivated;
        Closing += OnClosing;
        Closed += OnClosed;
        KeyDown += OnKeyDown;
        PreviewKeyDown += OnPreviewKeyDown;
        ContentRendered += OnContentRendered;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// Starts watching for clicks outside the popup once it is on screen. Not earlier: a
    /// low-level hook on a thread that does not pump messages stalls every mouse event on the
    /// desktop, and ContentRendered is the first point the dispatcher is known to be running
    /// (it never fires in the windowed tests, which have no message loop).
    /// </summary>
    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;

        // The first tile takes the focus, so Enter launches it without a click (upstream #26).
        // Focus inside the popup does not deactivate it, so dismiss-on-focus-loss is unaffected.
        if (_viewModel.Apps.Count > 0)
        {
            Dispatcher.BeginInvoke(() => FocusTile(0), DispatcherPriority.Loaded);
        }
        if (_closeAnimating || _closeConfirmed)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        _outsideClicks = new OutsideClickWatcher(hwnd, (x, y) =>
        {
            if (_dismissSuspended)
            {
                return;
            }

            // Synchronous: the marker has to exist before the shell starts the launcher that
            // the same click on the tile triggers.
            var press = new OutsidePressEventArgs(new Point(x, y));
            OutsidePress?.Invoke(this, press);
            if (DismissesOnOutsideInteraction || press.IsOnOpeningTile)
            {
                Dispatcher.BeginInvoke(new Action(Close));
            }
        });
    }

    /// <summary>
    /// Arrow keys, Home and End move the focus between tiles by <see cref="TileNavigator"/>'s
    /// rules — WPF's own directional navigation stops at row ends. Handled in the preview pass
    /// so that navigation never sees the keys. From anywhere else in the popup (the header's
    /// buttons) they go to the first tile.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        TileMove? move = e.Key switch
        {
            Key.Left => TileMove.Left,
            Key.Right => TileMove.Right,
            Key.Up => TileMove.Up,
            Key.Down => TileMove.Down,
            Key.Home => TileMove.Home,
            Key.End => TileMove.End,
            _ => null,
        };
        if (move is null || _viewModel.Apps.Count == 0)
        {
            return;
        }

        var current = IndexOfFocusedTile();
        FocusTile(current < 0 ? 0 : TileNavigator.Move(current, _viewModel.Apps.Count, _viewModel.EffectiveColumns, move.Value));
        e.Handled = true;
    }

    /// <summary>Index of the tile holding the focus, or -1 when the focus is elsewhere.</summary>
    internal int IndexOfFocusedTile()
    {
        // Logical focus, not keyboard focus: it is what Enter acts on once the window is
        // active, and it is set even while the window is not.
        if (FocusManager.GetFocusedElement(this) is not DependencyObject focused)
        {
            return -1;
        }

        for (var i = 0; i < _viewModel.Apps.Count; i++)
        {
            if (TileList.ItemContainerGenerator.ContainerFromIndex(i) is Visual container &&
                (ReferenceEquals(container, focused) || container.IsAncestorOf(focused)))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Moves the focus to the tile at <paramref name="index"/> and scrolls it into view.</summary>
    internal void FocusTile(int index)
    {
        if (TileList.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter container &&
            container.ContentTemplate?.FindName("TileButton", container) is Button tile)
        {
            // Keyboard focus would activate an inactive window. Logical focus does not, and the
            // window hands it the keyboard focus as soon as it is activated.
            if (IsActive)
            {
                tile.Focus();
            }
            else
            {
                FocusManager.SetFocusedElement(this, tile);
            }
            tile.BringIntoView();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;

            // Escape backs out one level: out of a drag first (the tiles go back), then out of
            // edit mode, then out of the popup.
            if (_reorder?.Cancel() == true)
            {
                return;
            }

            if (_viewModel.IsEditing)
            {
                _viewModel.IsEditing = false;
                return;
            }

            Close();
        }
    }

    /// <summary>
    /// Every way the popup closes — X, Escape, a click outside, focus loss, a launch, the
    /// fallback timer, even Alt+F4 — arrives here as a Close(), so this is the one place the
    /// fade-out is applied. The first request is cancelled and replaced by the animation,
    /// which calls Close() again once it has finished.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        if (!_settings.EnableAnimations || !IsVisible || TryFindResource("CloseAnimation") is not Storyboard storyboard)
        {
            _closeConfirmed = true;
            return;
        }

        e.Cancel = true;
        if (_closeAnimating)
        {
            return;
        }

        _closeAnimating = true;
        _outsideClicks?.Dispose();
        _outsideClicks = null;
        _safetyTimer?.Stop();
        PlayCloseAnimation(storyboard);
    }


    private void PlayCloseAnimation(Storyboard template)
    {
        var storyboard = template.Clone();
        foreach (var animation in storyboard.Children)
        {
            if (Storyboard.GetTargetName(animation) == "ChromeRoot")
            {
                Storyboard.SetTarget(animation, ChromeRoot);
            }
        }

        storyboard.Completed += (_, _) => FinishClose();
        storyboard.Begin(this);

        // Same reasoning as the open animation's safety net: if the composition pass is
        // skipped, Completed may never come, and a popup that cannot close is worse than one
        // that closes without its fade.
        _closeSafetyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _closeSafetyTimer.Tick += (_, _) => FinishClose();
        _closeSafetyTimer.Start();
    }

    private void FinishClose()
    {
        if (_closeConfirmed)
        {
            return;
        }

        _closeConfirmed = true;
        _closeSafetyTimer?.Stop();
        _closeSafetyTimer = null;
        Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_useSystemBackdrop)
        {
            ApplySystemBackdrop();
        }

        ApplySizeAndPlacement();

        if (_settings.EnableAnimations && TryFindResource("OpenAnimation") is Storyboard storyboard)
        {
            ScheduleAnimationOnFirstRender(storyboard);
        }
        else
        {
            // Either animations are disabled OR the storyboard resource was not found.
            // The XAML defaults Opacity=0.5 + ScaleX/Y=0.9 mean a missing storyboard would
            // leave the popup permanently faded and undersized — snap to the end state so
            // the user always sees the finished popup, regardless of resource lookup outcome.
            SnapToEndState();
        }

        // Never-activated fallback: dismiss-on-focus-loss relies on Deactivated, which can
        // only fire after the window has been activated once. When Windows denies the
        // activation (foreground-lock, background spawn) the popup would otherwise linger
        // as an orphaned Topmost window forever. Re-arms while the pointer is over the
        // popup so a user interacting with a never-activated popup is not interrupted;
        // OnActivated disarms it on the normal path.
        _activationFallbackTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _activationFallbackInterval,
        };
        _activationFallbackTimer.Tick += (_, _) =>
        {
            if (_wasActivated || IsMouseOver || !DismissesOnOutsideInteraction)
            {
                return;
            }

            _activationFallbackTimer?.Stop();
            Close();
        };
        _activationFallbackTimer.Start();
    }

    /// <summary>
    /// Puts DWM's acrylic behind the popup, tinted to match the theme, with rounded corners.
    /// With Windows' transparency effects switched off DWM substitutes a solid tint itself.
    /// </summary>
    private void ApplySystemBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // WPF clears the client area to the window background before drawing. Transparent
        // lets the DWM backdrop through; any other colour would cover it.
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        // The immersive dark-mode flag is what picks the dark acrylic tint, not just the
        // caption colour — the popup has no caption, so this is its only effect here.
        var dark = LauncherThemeApplier.Resolve(_settings.Theme) == ThemePreference.Dark;
        _ = WindowBackdrop.TrySetDarkTitleBar(hwnd, dark);
        _ = WindowBackdrop.TrySetRoundedCorners(hwnd);

        if (!WindowBackdrop.TryApply(hwnd, WindowBackdropKind.Acrylic))
        {
            // The window is no longer layered, so a transparent client area would render
            // black. Paint the fallback panel instead; it is near-opaque in this mode.
            ChromeRoot.SetResourceReference(Border.BackgroundProperty, "PopupFallbackBackground");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PopupViewModel.IsEditing))
        {
            ShowHeaderButtons(!_viewModel.IsEditing);
            Wiggle(_viewModel.IsEditing);
            return;
        }

        if (e.PropertyName is not (nameof(PopupViewModel.LastError) or nameof(PopupViewModel.IsUnavailable)))
        {
            return;
        }

        // Resize only — do not re-run the open animation after the window is already shown.
        ApplySizeAndPlacement();
    }

    /// <summary>
    /// In edit mode only the pencil stays in the header: add, pin and close fade and slide out,
    /// and come back when editing ends — at once when animations are off. They are hidden, not
    /// collapsed, so the pencil keeps its place; hidden also takes them out of the tab order and
    /// of UI Automation.
    /// </summary>
    private void ShowHeaderButtons(bool show)
    {
        SlidePencil(toTheEnd: !show);

        foreach (var button in new UIElement[] { AddAppsButton, PinButton, CloseButton })
        {
            button.IsHitTestVisible = show;
            var slide = button.RenderTransform as TranslateTransform ?? new TranslateTransform();
            button.RenderTransform = slide;

            if (!_settings.EnableAnimations)
            {
                button.BeginAnimation(OpacityProperty, null);
                slide.BeginAnimation(TranslateTransform.XProperty, null);
                button.Opacity = 1;
                slide.X = 0;
                button.Visibility = show ? Visibility.Visible : Visibility.Hidden;
                continue;
            }

            if (show)
            {
                button.Visibility = Visibility.Visible;
            }

            var duration = TimeSpan.FromMilliseconds(160);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(show ? 1 : 0, duration) { EasingFunction = ease };
            if (!show)
            {
                // Only if editing is still on when the fade ends; a quick second click on the
                // pencil has already started bringing it back.
                fade.Completed += (_, _) =>
                {
                    if (_viewModel.IsEditing)
                    {
                        button.Visibility = Visibility.Hidden;
                    }
                };
            }

            button.BeginAnimation(OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(show ? 0 : 10, duration) { EasingFunction = ease });
        }
    }

    /// <summary>
    /// In edit mode every tile wiggles, as icons do on a phone: a small rotation about its centre,
    /// neighbours starting in opposite directions and at slightly different speeds so the grid does
    /// not move in step. The tile's root is rotated (tile and badge together), never its container,
    /// whose transform belongs to a drag. Off when animations are off.
    /// </summary>
    private void Wiggle(bool on)
    {
        for (var i = 0; i < TileList.Items.Count; i++)
        {
            if (TileList.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter container
                || VisualTreeHelper.GetChildrenCount(container) == 0
                || VisualTreeHelper.GetChild(container, 0) is not FrameworkElement tile)
            {
                continue;
            }

            if (!on || !_settings.EnableAnimations)
            {
                if (tile.RenderTransform is RotateTransform still)
                {
                    still.BeginAnimation(RotateTransform.AngleProperty, null);
                    still.Angle = 0;
                }

                continue;
            }

            var lean = i % 2 == 0 ? WiggleDegrees : -WiggleDegrees;
            var rotate = new RotateTransform(-lean);
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = rotate;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-lean, lean, TimeSpan.FromMilliseconds(120 + (i % 3 * 14)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
    }

    private const double WiggleDegrees = 1.5;

    /// <summary>
    /// Moves the pencil to the header's right end, where the close button was, while the others
    /// are hidden, and back to its own place afterwards. The hidden buttons keep their layout
    /// slots, so the distance is read from the slots, not from where anything is drawn.
    /// </summary>
    private void SlidePencil(bool toTheEnd)
    {
        var offset = 0.0;
        if (toTheEnd)
        {
            var close = LayoutInformation.GetLayoutSlot(CloseButton);
            var pencil = LayoutInformation.GetLayoutSlot(EditButton);
            offset = (close.Right - CloseButton.Margin.Right) - (pencil.Right - EditButton.Margin.Right);
        }

        var slide = EditButton.RenderTransform as TranslateTransform ?? new TranslateTransform();
        EditButton.RenderTransform = slide;
        if (!_settings.EnableAnimations)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = offset;
            return;
        }

        slide.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(offset, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>
    /// Sets Width from the column count and Height from what the chrome actually wants,
    /// then recomputes taskbar-aware placement and the scale-transform pivot.
    /// </summary>
    private void ApplySizeAndPlacement()
    {
        // Explicit Width + Height skip the SizeToContent measure pass that pre-v0.4 added
        // ~5-10 ms before placement could be computed. Empty / unavailable groups fall
        // back to MinHeight, keeping the banner-only layout centred.
        var cols = Math.Max(_viewModel.EffectiveColumns, 1);
        Width = Math.Clamp(cols * _viewModel.Tile.CellWidth + 2 * PaddingPx, MinWidth, MaxWidth);

        // Height comes from measuring ChromeRoot rather than adding up tile rows, because
        // the error strip docks Bottom in a fill-last DockPanel: at a grid-only height it
        // takes its space out of the fixed-size tiles and clips the last row (issue #24).
        // ChromeRoot's DesiredSize already carries the grid, its padding and whatever the
        // LastError binding has currently made of the strip, so nothing here needs to know
        // the strip exists.
        //
        // Measuring the strip on its own instead would mean writing Visibility to force it
        // visible for the measure pass — and a local value on a OneWay-bound property
        // discards the binding for good. ClearValue then removes only the local value, not
        // the expression, and Visibility falls back to its default, which is Visible. The
        // strip would survive as an empty red bar and clip the tiles again on the next
        // click, when LaunchApp clears LastError.
        //
        // UpdateTarget rather than an assignment: this runs from LastError's PropertyChanged,
        // and the strip's own binding is not guaranteed to have been notified first, so the
        // measure below could otherwise see the previous visibility. Pushing the source value
        // through the existing expression is deterministic and leaves the binding in place.
        ErrorStrip.GetBindingExpression(VisibilityProperty)?.UpdateTarget();

        // Measure short-circuits when the element is measure-valid and the constraint is
        // unchanged, which is exactly the case on a repeat pass — invalidate so DesiredSize
        // reflects the strip's current state rather than the previous one.
        UpdateLayout();
        ChromeRoot.InvalidateMeasure();
        ChromeRoot.Measure(new Size(Width, double.PositiveInfinity));
        Height = Math.Clamp(ChromeRoot.DesiredSize.Height, MinHeight, MaxHeight);


        // Second pass so hit-test / DesiredSize match the new explicit size before placement.
        UpdateLayout();

        // Past MaxHeight the tile grid scrolls (issue #32), and the vertical bar takes its width
        // out of the viewport, which would squeeze the fixed-size tiles; widen the window by
        // exactly that much instead (the bar's width and margin, PopupScrollBar). Asked of the
        // laid-out scroller rather than worked out from DesiredSize, which does not yet carry the
        // tiles on the first pass. MaxWidth leaves room for the bar at six columns of the widest
        // cell (TileMetrics at 80 DIPs with names and the widest spacing: 6 × 124 + padding).
        if (TileScroller.ComputedVerticalScrollBarVisibility == Visibility.Visible)
        {
            var barWidth = TileScroller.ActualWidth - TileScroller.ViewportWidth;
            Width = Math.Clamp(Width + barWidth, MinWidth, MaxWidth);
            UpdateLayout();
        }

        var placement = _positionHelper.ComputePlacement(new Size(Width, Height), _settings.PopupPosition);
        Left = placement.Left;
        Top = placement.Top;

        UpdateScalePivot();
    }

    /// <summary>
    /// Sets the ScaleTransform pivot to bottom-centre so the open animation grows the
    /// popup up out of the clicked tile. Must also run after a strip-driven resize so
    /// CenterY tracks the new Height. Transform lives on ChromeRoot, not the Window —
    /// Window.CoerceRenderTransform rejects non-identity transforms outright.
    /// </summary>
    private void UpdateScalePivot()
    {
        if (ChromeRoot.RenderTransform is ScaleTransform scale)
        {
            scale.CenterX = Width / 2.0;
            scale.CenterY = Height;
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        _wasActivated = true;
        _activationFallbackTimer?.Stop();
        _activationFallbackTimer = null;
    }

    private void SnapToEndState()
    {
        if (FindName("ChromeRoot") is Border chrome)
        {
            chrome.Opacity = 1;
            if (chrome.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }
    }

    /// <summary>
    /// Schedules <see cref="Storyboard.Begin(System.Windows.FrameworkElement)"/> on the next
    /// dispatcher Render cycle and arms a 500 ms safety-net that force-snaps to the end state
    /// if the popup has not reached it. v0.4.1 used <see cref="CompositionTarget.Rendering"/>,
    /// but Win11 24H2 can skip the composition pass entirely for fully-transparent windows —
    /// <c>Rendering</c> never fires and the popup stays invisible forever. A
    /// <see cref="Dispatcher.BeginInvoke(DispatcherPriority, Delegate)"/> at Render priority
    /// always runs regardless of paint state. <c>Storyboard.SetTarget</c> on the
    /// <c>ChromeRoot</c> opacity child resolves the visual-tree element directly instead of
    /// going through the resource-scope <c>TargetName</c> lookup, which can silently no-op
    /// when the storyboard lives in <c>Window.Resources</c>. The 500 ms timer then guarantees
    /// the popup is visible no matter which corner of the WPF animation pipeline fails.
    /// </summary>
    private void ScheduleAnimationOnFirstRender(Storyboard storyboard)
    {
        if (FindName("ChromeRoot") is not Border chrome)
        {
            // No chrome to animate — collapsing to the end state still gives the user a
            // popup; missing chrome is a far worse bug than a missed animation.
            SnapToEndState();
            return;
        }

        // Clone before mutating: the resource instance can be frozen (BAML-loaded
        // Freezables), and SetTarget on a frozen timeline throws.
        storyboard = storyboard.Clone();
        foreach (var anim in storyboard.Children)
        {
            if (Storyboard.GetTargetName(anim) == "ChromeRoot")
            {
                Storyboard.SetTarget(anim, chrome);
            }
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() => storyboard.Begin(this)));

        // Stored in a field so OnClosed can stop the timer if the popup is dismissed before
        // 500 ms (Deactivated / LaunchSucceeded close the window). Without that, the captured
        // chrome reference keeps the window alive until the tick fires and writes opacity on
        // a detached visual tree.
        _safetyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _safetyTimer.Tick += (_, _) =>
        {
            _safetyTimer?.Stop();

            // The start state is half-opaque, so "still below 0.5" can no longer tell a
            // stuck animation from a finished one. Anything short of the end state after
            // 500 ms means the storyboard never ran to completion.
            if (chrome.Opacity < 1)
            {
                SnapToEndState();
            }
        };
        _safetyTimer.Start();
    }

    internal void OnDeactivated(object? sender, EventArgs e)
    {
        // Only dismiss after the window has genuinely held activation once. A spurious
        // deactivation before first activation would otherwise close the popup before
        // the user ever saw it.
        //
        // Not once the close is confirmed: an active window that closes (Escape, the close
        // button, a launch) is deactivated while it is being destroyed, and Close() on a
        // closing window throws from inside a WndProc — the launcher then exited with code 4
        // after every such close.
        if (_wasActivated && !_closeConfirmed && DismissesOnOutsideInteraction)
        {
            Close();
        }
    }

    private void OnLaunchSucceeded(object? sender, EventArgs e)
    {
        if (!_viewModel.IsPinned)
        {
            Close();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    // Tiles added or removed from inside the popup change the grid's height. Deferred to
    // Loaded priority: this handler can run before the ItemsControl has seen the change, and a
    // measure taken then sized the window for the old tile count — a tile that opened a new
    // row was cut off, label first, until the popup was reopened.
    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A tile moved by a drag changes no size; re-placing the window on every step of a drag
        // would only cost time.
        if (!IsLoaded || _closeAnimating || e.Action == NotifyCollectionChangedAction.Move)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_closeAnimating && !_closeConfirmed)
            {
                ApplySizeAndPlacement();

                // A tile rebuilt while editing (a reload, a removal) wiggles with the rest.
                if (_viewModel.IsEditing)
                {
                    Wiggle(true);
                }
            }
        }));
    }

    /// <summary>
    /// Opens the file picker for the + button. Input routing only: the picked files go to the
    /// view model, which applies the add rule and saves the group.
    /// </summary>
    private async void AddApps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Applications and shortcuts|*.exe;*.lnk|Executables (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk",
            Multiselect = true,
            Title = $"Add apps to \"{_viewModel.GroupName}\"",
        };

        bool? picked;
        _dismissSuspended = true;
        try
        {
            picked = dialog.ShowDialog(this);
        }
        finally
        {
            _dismissSuspended = false;
            // The dialog had the focus; give it back so dismiss-on-focus-loss works again.
            Activate();
        }

        if (picked == true && dialog.FileNames.Length > 0)
        {
            await _viewModel.AddAppsAsync(dialog.FileNames).ConfigureAwait(true);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Cancel any in-flight icon-load tasks so post-close task completions cannot
        // mutate the now-detached view model.
        _viewModel.CancelIconLoad();
        // Disarm the visibility safety-net if the popup closes before it fires (e.g.,
        // Deactivated dismissal within 500 ms). Letting it tick after Close would write
        // opacity on a detached visual tree.
        _safetyTimer?.Stop();
        _safetyTimer = null;
        _activationFallbackTimer?.Stop();
        _activationFallbackTimer = null;
        _viewModel.LaunchSucceeded -= OnLaunchSucceeded;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Apps.CollectionChanged -= OnAppsChanged;
        Activated -= OnActivated;
        Closing -= OnClosing;
        Closed -= OnClosed;
        KeyDown -= OnKeyDown;
        PreviewKeyDown -= OnPreviewKeyDown;
        ContentRendered -= OnContentRendered;
        SourceInitialized -= OnSourceInitialized;
        _closeSafetyTimer?.Stop();
        _closeSafetyTimer = null;
        Dispose();
    }

    /// <summary>
    /// Removes the outside-click hook. Called from <c>Closed</c>; safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        _outsideClicks?.Dispose();
        _outsideClicks = null;
        _reorder?.Dispose();
        _reorder = null;
    }
}
