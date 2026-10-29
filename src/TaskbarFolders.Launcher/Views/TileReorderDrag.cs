using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarFolders.Launcher.ViewModels;

namespace TaskbarFolders.Launcher.Views;

/// <summary>
/// Dragging a tile to another place in the popup's grid while the group is being edited. The
/// tile follows the pointer, drawn above the others and slightly enlarged; as it passes over
/// another cell the view model moves it there and the other tiles shift to make room. Released
/// over the grid, the order is kept; released outside it, cancelled with Escape or interrupted,
/// every tile goes back. A group taller than the popup scrolls while the tile is held near the top
/// or bottom.
/// </summary>
/// <remarks>
/// Pointer capture rather than <c>DragDrop.DoDragDrop</c>: the OLE drag loop shows no picture of
/// the tile and runs its own message loop, which this short-lived, self-dismissing window is better
/// off without. The arithmetic is <see cref="TileGridHit"/>'s; this class only moves visuals.
/// </remarks>
internal sealed class TileReorderDrag : IDisposable
{
    private const double LiftScale = 1.08;

    // How long a tile takes to slide out of the way, and the dropped tile to land in its cell.
    private static readonly TimeSpan _slide = TimeSpan.FromMilliseconds(180);

    private readonly ItemsControl _tiles;
    private readonly ScrollViewer _scroller;
    private readonly PopupViewModel _viewModel;
    private readonly bool _animate;
    private readonly DispatcherTimer _scrollTimer;

    private Point? _pressedAt;
    private PopupAppViewModel? _app;
    private ContentPresenter? _lifted;
    private Vector _grab;
    private bool _dragging;
    private bool _releasing;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="tiles">The popup's tile list.</param>
    /// <param name="scroller">The scroller round it.</param>
    /// <param name="viewModel">The popup's view model.</param>
    /// <param name="animate">
    /// Whether tiles slide to their new places (Settings → Enable animations); otherwise they
    /// jump there.
    /// </param>
    public TileReorderDrag(ItemsControl tiles, ScrollViewer scroller, PopupViewModel viewModel, bool animate)
    {
        _tiles = tiles;
        _scroller = scroller;
        _viewModel = viewModel;
        _animate = animate;
        _scrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _scrollTimer.Tick += OnScrollTick;

        _tiles.PreviewMouseLeftButtonDown += OnPressed;
        _tiles.PreviewMouseMove += OnMoved;
        _tiles.PreviewMouseLeftButtonUp += OnReleased;
        _tiles.LostMouseCapture += OnLostCapture;
    }

    /// <summary>Whether a tile is being dragged.</summary>
    public bool IsDragging => _dragging;

    /// <summary>Puts every tile back if a drag is in progress.</summary>
    /// <returns>Whether there was a drag to cancel.</returns>
    public bool Cancel()
    {
        if (!_dragging)
        {
            return false;
        }

        Finish(keep: false);
        return true;
    }

    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        Forget();
        if (!_viewModel.IsEditing || IsOnRemoveBadge(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(_tiles, (DependencyObject)e.OriginalSource) is ContentPresenter { Content: PopupAppViewModel app })
        {
            _app = app;
            _pressedAt = e.GetPosition(_tiles);
        }
    }

    private void OnMoved(object sender, MouseEventArgs e)
    {
        if (_pressedAt is not { } start || _app is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragging)
            {
                Finish(keep: false);
            }

            Forget();
            return;
        }

        if (!_dragging)
        {
            var moved = e.GetPosition(_tiles) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (!Start())
            {
                Forget();
                return;
            }
        }

        Follow();
        e.Handled = true;
    }

    private void OnReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            Forget();
            return;
        }

        // Released over the grid keeps the order; anywhere else (the header, outside the
        // window) puts the tiles back.
        var keep = TilePanel() is { } panel && IndexUnderPointer(panel) >= 0;
        Finish(keep);

        // The release ends the drag; the tile under it must not take it as a click.
        e.Handled = true;
    }

    // Only the list's own capture: a tile button losing the capture it took on the press bubbles
    // up here too, and that is the drag starting, not ending.
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (_dragging && !_releasing && ReferenceEquals(e.OriginalSource, _tiles))
        {
            Finish(keep: false);
        }
    }

    private bool Start()
    {
        if (_app is null || !_viewModel.BeginReorder())
        {
            return false;
        }

        if (_tiles.ItemContainerGenerator.ContainerFromItem(_app) is not ContentPresenter container
            || TilePanel() is not { } panel)
        {
            _viewModel.CancelReorder();
            return false;
        }

        _dragging = true;
        Lift(container);
        _grab = Mouse.GetPosition(panel) - (Point)VisualTreeHelper.GetOffset(container);
        Mouse.Capture(_tiles, CaptureMode.SubTree);
        _scrollTimer.Start();
        return true;
    }

    // Moves the tile in the grid when the pointer is over another cell, then puts its picture
    // under the pointer, wherever its cell now is.
    private void Follow()
    {
        if (_app is null || TilePanel() is not { } panel)
        {
            return;
        }

        var target = IndexUnderPointer(panel);
        var current = _viewModel.Apps.IndexOf(_app);
        if (target >= 0 && current >= 0 && target != current)
        {
            SlideOthers(() => _viewModel.MoveApp(current, target));
        }

        if (_tiles.ItemContainerGenerator.ContainerFromItem(_app) is ContentPresenter container)
        {
            if (!ReferenceEquals(container, _lifted))
            {
                Drop();
                Lift(container);
            }

            var cell = (Point)VisualTreeHelper.GetOffset(container);
            var at = Mouse.GetPosition(panel) - _grab;
            if (container.RenderTransform is TransformGroup { Children: [_, TranslateTransform move] })
            {
                move.X = at.X - cell.X;
                move.Y = at.Y - cell.Y;
            }
        }
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (!_dragging)
        {
            _scrollTimer.Stop();
            return;
        }

        var step = TileGridHit.EdgeScroll(Mouse.GetPosition(_scroller).Y, _scroller.ViewportHeight);
        if (step != 0 && _scroller.ScrollableHeight > 0)
        {
            _scroller.ScrollToVerticalOffset(Math.Clamp(_scroller.VerticalOffset + step, 0, _scroller.ScrollableHeight));
            _scroller.UpdateLayout();
            Follow();
        }
    }

    private void Finish(bool keep)
    {
        _scrollTimer.Stop();
        _dragging = false;

        // Where the dropped tile is drawn now, under the pointer; it lands from there once its
        // cell is known — the same one on a drop, its old one when the tiles go back.
        var landing = _lifted;
        var drawnAt = landing is null ? default : DrawnAt(landing);
        if (!keep)
        {
            SlideOthers(_viewModel.CancelReorder);
        }

        Land(landing, drawnAt);

        _releasing = true;
        try
        {
            if (ReferenceEquals(Mouse.Captured, _tiles))
            {
                Mouse.Capture(null);
            }
        }
        finally
        {
            _releasing = false;
        }

        if (keep)
        {
            // Saving cannot throw: a failure shows in the popup's error strip.
            _ = _viewModel.CommitReorderAsync();
        }

        Forget();
    }

    /// <summary>
    /// Runs a change to the order and lets every tile but the dragged one glide from where it was
    /// drawn to its new cell, instead of jumping there. A tile caught mid-slide starts from where
    /// it is at that moment.
    /// </summary>
    private void SlideOthers(Action reorder)
    {
        if (!_animate)
        {
            reorder();
            _tiles.UpdateLayout();
            return;
        }

        var before = new List<(ContentPresenter Tile, Point At)>();
        for (var i = 0; i < _tiles.Items.Count; i++)
        {
            if (_tiles.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter tile && !ReferenceEquals(tile, _lifted))
            {
                before.Add((tile, DrawnAt(tile)));
            }
        }

        reorder();
        _tiles.UpdateLayout();

        foreach (var (tile, at) in before)
        {
            var from = at - (Point)VisualTreeHelper.GetOffset(tile);
            if (from.Length > 0.5)
            {
                var move = new TranslateTransform(from.X, from.Y);
                tile.RenderTransform = move;
                move.BeginAnimation(TranslateTransform.XProperty, Glide(from.X, 0));
                move.BeginAnimation(TranslateTransform.YProperty, Glide(from.Y, 0));
            }
        }
    }

    // Puts the dropped tile back in the row: from where it was drawn to its cell, shrinking back
    // to its size, and only then under its neighbours again.
    private void Land(ContentPresenter? tile, Point drawnAt)
    {
        _lifted = null;
        if (tile is null)
        {
            return;
        }

        _tiles.UpdateLayout();
        var from = drawnAt - (Point)VisualTreeHelper.GetOffset(tile);
        if (!_animate || from.Length < 0.5)
        {
            tile.ClearValue(System.Windows.Controls.Panel.ZIndexProperty);
            tile.ClearValue(UIElement.RenderTransformProperty);
            return;
        }

        var scale = new ScaleTransform(LiftScale, LiftScale, tile.ActualWidth / 2, tile.ActualHeight / 2);
        var move = new TranslateTransform(from.X, from.Y);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(move);
        tile.RenderTransform = group;

        var settle = Glide(from.X, 0);
        settle.Completed += (_, _) =>
        {
            // Unless the same tile was picked up again meanwhile.
            if (!ReferenceEquals(tile, _lifted))
            {
                tile.ClearValue(System.Windows.Controls.Panel.ZIndexProperty);
            }
        };
        move.BeginAnimation(TranslateTransform.XProperty, settle);
        move.BeginAnimation(TranslateTransform.YProperty, Glide(from.Y, 0));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Glide(LiftScale, 1));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Glide(LiftScale, 1));
    }

    private static DoubleAnimation Glide(double from, double to) =>
        new(from, to, _slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

    // Where a tile is drawn: its cell plus whatever translation it carries now.
    private static Point DrawnAt(ContentPresenter tile)
    {
        var at = (Point)VisualTreeHelper.GetOffset(tile);
        var move = tile.RenderTransform switch
        {
            TranslateTransform t => t,
            TransformGroup { Children: [.., TranslateTransform t] } => t,
            _ => null,
        };
        return move is null ? at : at + new Vector(move.X, move.Y);
    }

    // Draws the dragged tile above the others, a little larger.
    private void Lift(ContentPresenter container)
    {
        _lifted = container;
        Panel.SetZIndex(container, 1);
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(LiftScale, LiftScale, container.ActualWidth / 2, container.ActualHeight / 2));
        group.Children.Add(new TranslateTransform());
        container.RenderTransform = group;
    }

    private void Drop()
    {
        if (_lifted is null)
        {
            return;
        }

        _lifted.ClearValue(System.Windows.Controls.Panel.ZIndexProperty);
        _lifted.ClearValue(UIElement.RenderTransformProperty);
        _lifted = null;
    }

    private void Forget()
    {
        _pressedAt = null;
        _app = null;
    }

    private int IndexUnderPointer(System.Windows.Controls.Panel panel) =>
        TileGridHit.IndexAt(
            Mouse.GetPosition(panel),
            new Size(panel.ActualWidth, panel.ActualHeight),
            _viewModel.EffectiveColumns,
            _viewModel.Apps.Count);

    // The UniformGrid the tiles are laid out in.
    private System.Windows.Controls.Panel? TilePanel() =>
        _tiles.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject first
            ? VisualTreeHelper.GetParent(first) as System.Windows.Controls.Panel
            : null;

    private static bool IsOnRemoveBadge(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase { Name: "RemoveBadge" })
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _scrollTimer.Stop();
        _scrollTimer.Tick -= OnScrollTick;
        _tiles.PreviewMouseLeftButtonDown -= OnPressed;
        _tiles.PreviewMouseMove -= OnMoved;
        _tiles.PreviewMouseLeftButtonUp -= OnReleased;
        _tiles.LostMouseCapture -= OnLostCapture;
    }
}
