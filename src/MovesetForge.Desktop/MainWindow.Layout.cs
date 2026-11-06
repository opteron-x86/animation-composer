using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using MovesetForge.Core;

namespace MovesetForge.Desktop;

public sealed partial class MainWindow
{
    private readonly ColumnDefinitions paneWidths = new("300*,12,740*,12,375*");
    private readonly RowDefinitions previewHeights = new("3*,12,*");
    private readonly RowDefinitions projectHeights = new("Auto,*,12,3*");
    private double? fittedNameWidth;

    private Grid CreateWorkspace()
    {
        var main = new Grid
        {
            ColumnDefinitions = paneWidths,
            Margin = new Thickness(22, 0, 22, 12)
        };
        main.ColumnDefinitions[0].MinWidth = 180;
        main.ColumnDefinitions[2].MinWidth = 422;
        main.ColumnDefinitions[4].MinWidth = 270;

        var libraryPane = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), RowSpacing = 9 };
        libraryPane.Children.Add(Label("ANIMATION LIBRARY"));
        Put(libraryPane, search, row: 1);
        Put(libraryPane, combatOnly, row: 2);
        Put(libraryPane, sourceList, row: 3);
        stats.TextWrapping = TextWrapping.Wrap;
        Put(libraryPane, stats, row: 4);

        Put(main, Card(libraryPane));
        Put(main, Divider(GridResizeDirection.Columns, "Resize library and preview"), 1);
        Put(main, Card(CreatePreviewPane()), 2);
        Put(main, Divider(GridResizeDirection.Columns, "Resize preview and project"), 3);
        Put(main, Card(CreateProjectPane()), 4);
        return main;
    }

    private Grid CreatePreviewPane()
    {
        var pane = new Grid { RowDefinitions = previewHeights };
        pane.RowDefinitions[0].MinHeight = 370;
        pane.RowDefinitions[2].MinHeight = 100;

        var preview = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 6 };
        preview.Children.Add(Label("PREVIEW"));
        var skeletonRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 };
        skeletonRow.Children.Add(skeleton);
        Put(skeletonRow, Button("Skeleton…", ChooseSkeleton), 1);
        Put(preview, skeletonRow, row: 1);
        AutomationProperties.SetName(previewFolders, "Preview folder");
        ToolTip.SetTip(previewFolders, "Choose the source folder to preview");
        previewFolders.ItemTemplate = new FuncDataTemplate<PreviewFolder>((folder, _) =>
            new TextBlock { Text = folder?.ToString(), TextTrimming = TextTrimming.CharacterEllipsis });
        Put(preview, previewFolders, row: 2);
        Put(preview, viewport, row: 3);
        Put(preview, timeline, row: 4);

        var inPlace = new CheckBox { Content = "In place", IsChecked = true };
        inPlace.IsCheckedChanged += (_, _) =>
        {
            viewport.InPlace = inPlace.IsChecked == true;
            viewport.InvalidateVisual();
        };
        play.Click += (_, _) => TogglePlay();
        var reset = Button("Reset view", () => { viewport.ResetCamera(); return Task.CompletedTask; });
        Put(preview, Toolbar(play, speed, loop, inPlace, reset), row: 5);
        normalChain.Click += async (_, _) => await LoadChain(AttackKind.Normal);
        powerChain.Click += async (_, _) => await LoadChain(AttackKind.Power);
        Put(preview, Toolbar(normalChain, powerChain, sequenceOrder), row: 6);
        pane.Children.Add(preview);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Clips", Content = clips });
        tabs.Items.Add(new TabItem { Header = "Sequence", Content = sequenceDetails });
        tabs.Items.Add(new TabItem { Header = "Events", Content = events });
        tabs.Items.Add(new TabItem { Header = "Source rules", Content = details });
        tabs.Items.Add(new TabItem { Header = "Conversion", Content = conversionDetails });
        Put(pane, Divider(GridResizeDirection.Rows, "Resize preview and details"), row: 1);
        Put(pane, tabs, row: 2);
        return pane;
    }

    private Grid CreateProjectPane()
    {
        var pane = new Grid { RowDefinitions = projectHeights };
        pane.RowDefinitions[1].MinHeight = 86;
        pane.RowDefinitions[3].MinHeight = 156;
        var headings = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        headings.Children.Add(Label("YOUR MOVESET MOD"));
        headings.Children.Add(projectName);
        headings.Children.Add(target);
        headings.Children.Add(Label("ADDED SETS"));
        pane.Children.Add(headings);
        Put(pane, rules, row: 1);
        Put(pane, Divider(GridResizeDirection.Rows, "Resize added sets and rule editor"), row: 2);

        var editor = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        var remove = Button("Remove", RemoveRule);
        remove.Bind(IsEnabledProperty, new Binding(nameof(DataGrid.SelectedItem))
        {
            Source = rules, Converter = ObjectConverters.IsNotNull
        });
        editor.Children.Add(Row(Button("Add selected set", AddSelected), remove));
        Put(editor, new ScrollViewer
        {
            Content = CreateRuleEditor(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }, row: 1);
        Put(pane, editor, row: 3);
        return pane;
    }

    private DataGrid CreateAddedSetsTable(TableColumnLayout? saved)
    {
        if (saved?.IsValid() != true) saved = null;
        var table = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            RowHeight = 27,
            ColumnHeaderHeight = 28,
            BorderBrush = Brush.Parse("#405060"),
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(table, "Added sets");
        table.Styles.Add(new Style(selector => selector.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(DataGridColumnHeader.SeparatorBrushProperty, Brush.Parse("#87939F")),
                new Setter(ToolTip.TipProperty, "Drag the right edge to resize this column")
            }
        });
        table.Styles.Add(new Style(selector => selector.OfType<DataGridCell>().Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis) }
        });
        var name = new DataGridTextColumn
        {
            Header = "Name", Binding = new Binding("Rule.Name"), MinWidth = 100, Width = new DataGridLength(Math.Max(100, saved?.Name ?? 170))
        };
        table.Columns.Add(name);
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "Actors", Binding = new Binding("Rule.Actors"), MinWidth = 46, Width = new DataGridLength(Math.Max(46, saved?.Actors ?? 66))
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = "Priority", Binding = new Binding("Rule.Priority"), MinWidth = 56, Width = new DataGridLength(Math.Max(56, saved?.Priority ?? 83))
        });
        // A star column prevents dragging the last header's outer edge. Use pixel
        // columns and fit the name to the pane until the user sets its width.
        fittedNameWidth = saved?.FitName == false ? null : name.Width.Value;
        table.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged || !NameFitsPane(name)) return;
            var actorsWidth = table.Columns[1].Width.Value;
            var priorityWidth = table.Columns[2].Width.Value;
            double width = Math.Max(name.MinWidth, e.NewSize.Width - actorsWidth - priorityWidth - 22);
            // The user may have resized a neighboring header since the last pane resize.
            // Keep the displayed name width when reopening at that same pane width.
            if (saved != null && Math.Abs(e.NewSize.Width - saved.TableWidth) < 1
                && Math.Abs(actorsWidth - saved.Actors) < 0.01 && Math.Abs(priorityWidth - saved.Priority) < 0.01)
            {
                width = Math.Max(name.MinWidth, saved.Name);
            }
            fittedNameWidth = width;
            name.Width = new DataGridLength(width);
        };
        return table;
    }

    private bool NameFitsPane(DataGridColumn name) =>
        fittedNameWidth is double fitted && Math.Abs(name.Width.Value - fitted) < 0.01;

    private StackPanel CreateRuleEditor()
    {
        AutomationProperties.SetName(keyword, "Weapon keyword EditorID");
        var form = new StackPanel { Spacing = 8 };
        form.Children.Add(editorInfo);
        form.Children.Add(detectionInfo);
        form.Children.Add(Field("Name", ruleName));
        form.Children.Add(Field("Weapon type", weapon));
        form.Children.Add(keyword);
        form.Children.Add(Two(Field("Actors", actors), Field("Weapon hand", hand)));
        form.Children.Add(Field("Priority · higher wins", priority));
        form.Children.Add(Field("Actor level · optional", Two(levelMin, levelMax)));
        form.Children.Add(Field("Base skill · optional", skill));
        form.Children.Add(Two(skillMin, skillMax));
        form.Children.Add(Field("Race · optional plugin + local FormID", Two(racePlugin, raceId)));
        form.Children.Add(Field("Specific weapon · optional", Two(weaponPlugin, weaponId)));
        form.Children.Add(new Expander { Header = "Advanced OAR conditions", Content = advanced });
        form.Children.Add(new TextBlock
        {
            Text = "Source equipment and gameplay conditions remain active. Choose Actors: Source to preserve player/NPC restrictions too.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#90A3B5"), FontSize = 11
        });
        form.Children.Add(Button("Apply rules", ApplyRule));
        return form;
    }

    private static WrapPanel Toolbar(params Control[] controls)
    {
        var panel = new WrapPanel();
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 2);
            panel.Children.Add(control);
        }
        return panel;
    }

    private static GridSplitter Divider(GridResizeDirection direction, string name)
    {
        bool vertical = direction == GridResizeDirection.Columns;
        var divider = new GridSplitter
        {
            ResizeDirection = direction,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            KeyboardIncrement = 12,
            Cursor = new Cursor(vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
            Template = new FuncControlTemplate<GridSplitter>((_, _) => new Border
            {
                Background = Brushes.Transparent,
                Child = new Border
                {
                    Width = vertical ? 3 : 34, Height = vertical ? 34 : 3,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    CornerRadius = new CornerRadius(2), Background = Brush.Parse("#74879A")
                }
            })
        };
        AutomationProperties.SetName(divider, name);
        ToolTip.SetTip(divider, name);
        return divider;
    }
}
