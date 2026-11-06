using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Threading;

namespace MovesetForge.Desktop;

public sealed partial class MainWindow
{
    private Control RememberLayout(Grid shell, LayoutPreferences? saved)
    {
        var columns = paneWidths.Where(c => c.Width.IsStar).ToArray();
        var preview = previewHeights.Where(r => r.Height.IsStar).ToArray();
        var projectRows = projectHeights.Where(r => r.Height.IsStar).ToArray();
        RestoreWeights(columns, saved?.PaneWidths, (column, weight) => column.Width = weight);
        RestoreWeights(preview, saved?.PreviewRowHeights, (row, weight) => row.Height = weight);
        RestoreWeights(projectRows, saved?.ProjectRowHeights, (row, weight) => row.Height = weight);

        var usualMinimum = new Size(MinWidth, MinHeight);
        var available = AvailableClientArea();
        MinWidth = Math.Min(MinWidth, available.Width);
        MinHeight = Math.Min(MinHeight, available.Height);
        double width = saved != null && LayoutPreferences.IsDimension(saved.WindowWidth) ? saved.WindowWidth : Width;
        double height = saved != null && LayoutPreferences.IsDimension(saved.WindowHeight) ? saved.WindowHeight : Height;
        Width = Math.Clamp(width, MinWidth, available.Width);
        Height = Math.Clamp(height, MinHeight, available.Height);

        var requestedSize = new Size(Width, Height);
        var normalSize = requestedSize;
        bool maximized = saved?.Maximized == true;
        bool resizePending = false;
        bool opened = false;
        void RememberNormalSize()
        {
            if (WindowState == WindowState.Normal && ClientSize.Width > 0 && ClientSize.Height > 0)
                normalSize = ClientSize;
        }
        Resized += (_, _) =>
        {
            // A native maximize can report its new size before its new window state.
            if (resizePending) return;
            resizePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                resizePending = false;
                if (IsVisible) RememberNormalSize();
            }, DispatcherPriority.Background);
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
                maximized = WindowState == WindowState.Maximized;
        };
        Opened += (_, _) =>
        {
            opened = true;
            // X11 may first size an unmapped window at 100% and round away a pixel.
            // Reapply at the mapped scale before the OS records the unmaximize bounds.
            if (saved != null && WindowState == WindowState.Normal)
            {
                Width = requestedSize.Width;
                Height = requestedSize.Height;
            }
            if (saved?.Maximized == true) WindowState = WindowState.Maximized;
        };
        Closing += (_, e) =>
        {
            if (e.Cancel || !opened) return;
            RememberNormalSize();
            new LayoutPreferences
            {
                WindowWidth = normalSize.Width,
                WindowHeight = normalSize.Height,
                Maximized = maximized,
                PaneWidths = columns.Select(c => c.Width.Value).ToArray(),
                PreviewRowHeights = preview.Select(r => r.Height.Value).ToArray(),
                ProjectRowHeights = projectRows.Select(r => r.Height.Value).ToArray(),
                AddedSetColumns = new TableColumnLayout(rules.Columns[0].Width.Value, rules.Columns[1].Width.Value,
                    rules.Columns[2].Width.Value, NameFitsPane(rules.Columns[0]), rules.Bounds.Width)
            }.Save();
        };

        if (MinWidth == usualMinimum.Width && MinHeight == usualMinimum.Height) return shell;
        // On a work area smaller than the usable editor, allow the whole layout to scroll.
        var scroll = new ScrollViewer
        {
            Content = shell,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        shell.MinWidth = usualMinimum.Width;
        shell.MinHeight = usualMinimum.Height;
        shell.Bind(WidthProperty, new Binding("Viewport.Width") { Source = scroll });
        shell.Bind(HeightProperty, new Binding("Viewport.Height") { Source = scroll });
        return scroll;
    }

    private Size AvailableClientArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null || screen.WorkingArea.Width <= 0 || screen.WorkingArea.Height <= 0)
            return Size.Infinity;
        var work = screen.WorkingArea.Size.ToSize(screen.Scaling);
        // Some platforms only know the frame dimensions after mapping the window.
        double frameWidth = FrameSize is { } f ? Math.Max(0, f.Width - ClientSize.Width) : 0;
        double frameHeight = FrameSize is { } h ? Math.Max(0, h.Height - ClientSize.Height) : 32;
        return new Size(Math.Max(1, work.Width - frameWidth), Math.Max(1, work.Height - frameHeight));
    }

    private static void RestoreWeights<T>(T[] definitions, double[]? weights, Action<T, GridLength> apply)
    {
        if (weights == null || weights.Length != definitions.Length || !weights.All(LayoutPreferences.IsDimension)) return;
        for (int i = 0; i < definitions.Length; i++)
            apply(definitions[i], new GridLength(weights[i], GridUnitType.Star));
    }
}
