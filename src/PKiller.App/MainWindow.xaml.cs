using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PKiller.Core.Abstractions;
using PKiller.Core.Model;
using PKiller.Core.Native;
using PKiller.Core.Services;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PKiller.App;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly NativeProcessSnapshotSource _source = new();
    private readonly CpuSampler _cpuSampler = new(Environment.ProcessorCount);
    private readonly ProcessListPresenter _presenter;
    private readonly AppIconResolver _iconResolver = new();
    private readonly ProcessContextActionFactory _contextActionFactory = new();
    private readonly Dictionary<string, Task<BitmapImage?>> _iconLoads = new(StringComparer.OrdinalIgnoreCase);
    private readonly StopPlanner _stopPlanner = new();
    private readonly StopCoordinator _stopCoordinator;
    private readonly IElevationService _elevation = new NativeElevationService();
    private readonly ISettingsStore _settingsStore = new FileSettingsStore();
    private readonly DispatcherQueueTimer _timer;
    private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Grid> _renderedGrids = new();
    private readonly List<TableColumn> _columnOrder = [];
    private readonly Dictionary<TableColumn, double> _columnWidths = new()
    {
        [TableColumn.Name] = 230,
        [TableColumn.Cpu] = 60,
        [TableColumn.Memory] = 90,
        [TableColumn.Read] = 100,
        [TableColumn.Write] = 100,
        [TableColumn.Actions] = 100,
    };
    private IReadOnlyList<AppGroup> _allGroups = Array.Empty<AppGroup>();
    private IReadOnlyList<AppGroup> _currentGroups = Array.Empty<AppGroup>();
    private SortOrder _sort;
    private bool _autoRefresh;
    private bool _refreshing;
    private bool _refreshRequested;
    private bool _resetCpuSampler;
    private DateTimeOffset _lastSample;
    private string _search = string.Empty;
    private TableColumn? _draggedColumn;
    private double _headerDragOffset;
    private Border? _draggedHeaderSurface;
    private Border? _dragPreview;
    private Windows.Foundation.Point _dragPreviewOrigin;

    public MainWindow()
    {
        InitializeComponent();
        Title = "PKiller";
        SetDefaultWindowSize();

        var classifier = new ProcessClassifier();
        _presenter = new ProcessListPresenter(new AppGrouper(classifier, new MetricsAggregator()));
        var resolver = new ProcessResolver(_source);
        _stopCoordinator = new StopCoordinator(
            resolver,
            new NativeWindowCloser(),
            new NativeProcessTerminator(),
            new SystemClock());

        AppSettings settings = _settingsStore.Load();
        _sort = settings.Sort;
        _autoRefresh = settings.AutoRefresh;
        _columnOrder.AddRange(settings.ColumnOrder);
        RefreshButton.Content = _autoRefresh ? "Pause updates" : "Resume updates";

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => _ = RefreshProcessesAsync();
        _timer.Start();
        AdminButton.Visibility = _elevation.CanRelaunchAsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        if (!_elevation.IsElevated)
        {
            ShowNotice("Some processes belong to other users or require elevation. They are marked unavailable. PKiller never elevates automatically.");
        }

        _ = RefreshProcessesAsync(force: true);
    }

    private async Task RefreshProcessesAsync(bool force = false)
    {
        if (!_autoRefresh && !force)
        {
            return;
        }

        if (_refreshing)
        {
            _refreshRequested |= force;
            return;
        }

        _refreshing = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            double elapsed = _lastSample == default ? 0 : (now - _lastSample).TotalSeconds;
            string search = _search.Trim();
            SortOrder sort = _sort;
            bool resetCpuSampler = _resetCpuSampler;
            _resetCpuSampler = false;

            var (allGroups, currentGroups) = await Task.Run(() =>
            {
                if (resetCpuSampler)
                {
                    _cpuSampler.Reset();
                }

                var raw = _source.ReadForDisplay();
                var sampled = _cpuSampler.Sample(raw, elapsed);
                var all = _presenter.Present(sampled, _source.CurrentSessionId, null, sort);
                var current = string.IsNullOrWhiteSpace(search)
                    ? all
                    : all
                        .Where(g => g.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                            || g.Processes.Any(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                return (all, current);
            });

            _lastSample = now;
            _allGroups = allGroups;
            _currentGroups = currentGroups;
            var liveKeys = _allGroups.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _selectedKeys.RemoveWhere(key => !liveKeys.Contains(key));
            UpdateSelection();
            if (_draggedColumn is null)
            {
                RenderRows();
            }

            StatusText.Text = $"{_currentGroups.Count} apps · Updated {DateTime.Now.ToString("HH:mm:ss")}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not refresh process list: {ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }

        if (_refreshRequested)
        {
            _refreshRequested = false;
            _ = RefreshProcessesAsync(force: true);
        }
    }

    private void RenderRows()
    {
        RowsPanel.Children.Clear();
        _renderedGrids.Clear();
        RowsPanel.Children.Add(CreateHeader());

        if (_currentGroups.Count == 0)
        {
            RowsPanel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_search) ? "No apps to show." : "No apps match your search.",
                Margin = new Thickness(16, 24, 0, 0),
                Foreground = (Brush)Application.Current.Resources["AppTextMutedBrush"],
            });
        }

        foreach (var group in _currentGroups)
        {
            RowsPanel.Children.Add(CreateAppRow(group));

            if (_expandedKeys.Contains(group.Key))
            {
                foreach (var process in group.Processes.Where(p => !group.Roots.Any(r => r.Pid == p.Pid)))
                {
                    RowsPanel.Children.Add(CreateProcessRow(process));
                }
            }
        }
    }

    private readonly HashSet<string> _expandedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TableColumn, Border> _headerSurfaces = new();

    private Grid CreateHeader()
    {
        var header = CreateColumns();
        header.Height = 38;
        header.Padding = new Thickness(8, 0, 8, 0);
        header.Background = (Brush)Application.Current.Resources["AppCardBrush"];
        foreach (TableColumn column in _columnOrder)
        {
            SortMetric? metric = GetSortMetric(column);
            var label = new TextBlock
            {
                Text = GetColumnTitle(column),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var grip = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 12,
                Opacity = 0.25,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                Tag = metric,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            ToolTipService.SetToolTip(button, metric is null
                ? "Use the grip to reorder this column."
                : "Click to sort. Use the grip to reorder or drag the right divider to resize.");
            if (metric is not null)
            {
                button.Click += OnSort;
            }

            var headerContent = new Grid();
            headerContent.Children.Add(button);
            var dragThumb = new Thumb
            {
                Width = 24,
                Margin = new Thickness(0, 0, _columnOrder.IndexOf(column) < _columnOrder.Count - 1 ? 10 : 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = column,
            };
            ToolTipService.SetToolTip(dragThumb, "Drag the grip to reorder this column.");
            AutomationProperties.SetName(dragThumb, $"Reorder {GetColumnTitle(column)} column");
            AutomationProperties.SetHelpText(dragThumb, "Drag to choose and save this column's position.");
            dragThumb.DragStarted += OnHeaderDragStarted;
            dragThumb.DragDelta += OnHeaderDragDelta;
            dragThumb.DragCompleted += OnHeaderDragCompleted;
            headerContent.Children.Add(dragThumb);
            grip.IsHitTestVisible = false;
            grip.HorizontalAlignment = HorizontalAlignment.Right;
            grip.Margin = new Thickness(0, 0, _columnOrder.IndexOf(column) < _columnOrder.Count - 1 ? 15 : 5, 0);
            headerContent.Children.Add(grip);
            var headerSurface = new Border
            {
                Child = headerContent,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
            };
            AddCell(header, headerSurface, column);
            _headerSurfaces[column] = headerSurface;
            if (_columnOrder.IndexOf(column) < _columnOrder.Count - 1)
            {
                AddResizeHandle(header, column);
            }
        }

        return header;
    }

    private Grid CreateAppRow(AppGroup group)
    {
        var row = CreateColumns();
        row.MinHeight = 48;
        row.Padding = new Thickness(8, 4, 8, 4);
        row.Background = (Brush)Application.Current.Resources["AppCardBrush"];
        AttachProcessContextMenu(row, _contextActionFactory.ForApp(group));

        var select = new CheckBox
        {
            IsChecked = _selectedKeys.Contains(group.Key),
            Width = 20,
            MinWidth = 20,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = group,
        };
        select.Checked += OnAppChecked;
        select.Unchecked += OnAppUnchecked;

        var expand = new Button
        {
            Content = _expandedKeys.Contains(group.Key) ? "⌄" : "›",
            Width = 20,
            Height = 24,
            Padding = new Thickness(0),
            Tag = group,
            Background = (Brush)Application.Current.Resources["AppCardBrush"],
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        expand.Click += OnExpandGroup;

        var namePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };
        namePanel.Children.Add(select);
        namePanel.Children.Add(expand);
        var iconPanel = new Grid
        {
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var fallbackIcon = new FontIcon
        {
            Glyph = "\uE71D",
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["AppTextMutedBrush"],
        };
        var appIcon = new Image
        {
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
        };
        iconPanel.Children.Add(fallbackIcon);
        iconPanel.Children.Add(appIcon);
        namePanel.Children.Add(iconPanel);
        _ = SetAppIconAsync(group, appIcon, fallbackIcon);
        namePanel.Children.Add(new TextBlock
        {
            Text = group.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        namePanel.Children.Add(new TextBlock
        {
            Text = $"({group.ProcessCount})",
            Foreground = (Brush)Application.Current.Resources["AppTextMutedBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        AddCell(row, namePanel, TableColumn.Name);
        AddTextCell(row, group.IsAccessible ? $"{group.CpuPercent:F1}%" : "Unavailable", TableColumn.Cpu);
        AddTextCell(row, group.IsAccessible ? $"{MetricsAggregator.WorkingSetMb(group.WorkingSetBytes):N1} MB" : "Unavailable", TableColumn.Memory);
        AddTextCell(row, group.IsAccessible ? FormatBytes(group.ReadBytes) : "Unavailable", TableColumn.Read);
        AddTextCell(row, group.IsAccessible ? FormatBytes(group.WriteBytes) : "Unavailable", TableColumn.Write);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var end = new Button
        {
            Content = group.HasCloseableWindow ? "End task" : "Force stop",
            Tag = group,
            IsEnabled = group.CanTerminate,
        };
        end.Click += OnEndApp;
        actions.Children.Add(end);
        if (!group.CanTerminate)
        {
            actions.Children.Add(new TextBlock
            {
                Text = "Unavailable",
                Foreground = (Brush)Application.Current.Resources["AppTextMutedBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        AddCell(row, actions, TableColumn.Actions);
        return row;
    }

    private async Task SetAppIconAsync(AppGroup group, Image image, FontIcon fallback)
    {
        string? path = _iconResolver.GetExecutablePath(group);
        if (path is null)
        {
            return;
        }

        if (!_iconLoads.TryGetValue(path, out Task<BitmapImage?>? iconTask))
        {
            iconTask = LoadAppIconAsync(path);
            _iconLoads[path] = iconTask;
        }

        BitmapImage? icon = await iconTask;
        if (icon is null)
        {
            return;
        }

        image.Source = icon;
        image.Visibility = Visibility.Visible;
        fallback.Visibility = Visibility.Collapsed;
    }

    private static async Task<BitmapImage?> LoadAppIconAsync(string path)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or UnauthorizedAccessException
                or System.IO.IOException
                or NotSupportedException
                or InvalidOperationException
                or COMException)
        {
            return null;
        }
    }

    private Grid CreateProcessRow(ProcessInfo process)
    {
        var row = CreateColumns();
        row.MinHeight = 38;
        row.Padding = new Thickness(8, 2, 8, 2);
        row.Background = (Brush)Application.Current.Resources["AppCardBrush"];
        AttachProcessContextMenu(row, _contextActionFactory.ForProcess(process));
        var namePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4,
        };
        namePanel.Children.Add(new Grid { Width = 72 });
        namePanel.Children.Add(new TextBlock
        {
            Text = $"{process.Name} · PID {process.Pid}",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        AddCell(row, namePanel, TableColumn.Name);
        AddTextCell(row, process.IsAccessible ? $"{process.CpuPercent:F1}%" : "Unavailable", TableColumn.Cpu);
        AddTextCell(row, process.IsAccessible ? $"{MetricsAggregator.WorkingSetMb(process.WorkingSetBytes):N1} MB" : "Unavailable", TableColumn.Memory);
        AddTextCell(row, process.IsAccessible ? FormatBytes(process.ReadBytes) : "Unavailable", TableColumn.Read);
        AddTextCell(row, process.IsAccessible ? FormatBytes(process.WriteBytes) : "Unavailable", TableColumn.Write);
        AddTextCell(row, string.Empty, TableColumn.Actions);
        return row;
    }

    private Grid CreateColumns()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (TableColumn column in _columnOrder)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(_columnWidths[column]),
            });
        }

        _renderedGrids.Add(grid);
        return grid;
    }

    private void AddResizeHandle(Grid header, TableColumn column)
    {
        var line = new Border
        {
            Width = 1,
            Margin = new Thickness(0, 8, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = (Brush)Application.Current.Resources["AppTextMutedBrush"],
            Opacity = 0.25,
        };
        var handle = new Thumb
        {
            Width = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        handle.Tag = column;
        ToolTipService.SetToolTip(handle, "Drag to resize this column.");
        AutomationProperties.SetName(handle, $"Resize {GetColumnTitle(column)} column");
        AutomationProperties.SetHelpText(handle, "Drag the divider to resize this column.");
        handle.PointerEntered += (_, _) => line.Opacity = 0.85;
        handle.PointerExited += (_, _) => line.Opacity = 0.25;
        handle.DragDelta += (_, e) =>
        {
            double minWidth = column switch
            {
                TableColumn.Name => 150,
                TableColumn.Actions => 80,
                _ => 55,
            };
            _columnWidths[column] = Math.Max(minWidth, _columnWidths[column] + e.HorizontalChange);
            ApplyColumnWidths();
        };
        var resizeArea = new Grid
        {
            Width = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        resizeArea.Children.Add(line);
        resizeArea.Children.Add(handle);
        Grid.SetColumn(resizeArea, _columnOrder.IndexOf(column));
        header.Children.Add(resizeArea);
    }

    private void AddTextCell(Grid grid, string text, TableColumn column)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AddCell(grid, block, column);
    }

    private void AddCell(Grid grid, FrameworkElement element, TableColumn column)
    {
        int index = _columnOrder.IndexOf(column);
        Grid.SetColumn(element, index);
        grid.Children.Add(element);
        if (index < _columnOrder.Count - 1)
        {
            var separator = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 8, -4, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = (Brush)Application.Current.Resources["AppTextMutedBrush"],
                Opacity = 0.2,
                IsHitTestVisible = false,
            };
            Grid.SetColumn(separator, index);
            grid.Children.Add(separator);
        }
    }

    private void ApplyColumnWidths()
    {
        foreach (Grid grid in _renderedGrids)
        {
            for (int index = 0; index < _columnOrder.Count; index++)
            {
                grid.ColumnDefinitions[index].Width = new GridLength(_columnWidths[_columnOrder[index]]);
            }
        }
    }

    private void OnHeaderDragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb { Tag: TableColumn column })
        {
            return;
        }

        _draggedColumn = column;
        _headerDragOffset = 0;
        _draggedHeaderSurface = _headerSurfaces[column];
        var headerOrigin = _draggedHeaderSurface
            .TransformToVisual(DragPreviewCanvas)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        double previewWidth = _draggedHeaderSurface.ActualWidth * 1.04;
        double previewHeight = _draggedHeaderSurface.ActualHeight * 1.08;
        _dragPreviewOrigin = new Windows.Foundation.Point(
            headerOrigin.X - (previewWidth - _draggedHeaderSurface.ActualWidth) / 2,
            headerOrigin.Y - (previewHeight - _draggedHeaderSurface.ActualHeight) / 2);
        var previewContent = new Grid();
        previewContent.Children.Add(new TextBlock
        {
            Text = GetColumnTitle(column),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        previewContent.Children.Add(new TextBlock
        {
            Text = "⋮⋮",
            FontSize = 12,
            Opacity = 0.4,
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _dragPreview = new Border
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = (Brush)Application.Current.Resources["AppCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AppAccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Opacity = 0.96,
            IsHitTestVisible = false,
            Child = previewContent,
        };
        Canvas.SetLeft(_dragPreview, _dragPreviewOrigin.X);
        Canvas.SetTop(_dragPreview, _dragPreviewOrigin.Y);
        DragPreviewCanvas.Children.Add(_dragPreview);
        _draggedHeaderSurface.Background = (Brush)Application.Current.Resources["AppCardBrush"];
        _draggedHeaderSurface.BorderBrush = (Brush)Application.Current.Resources["AppAccentBrush"];
        _draggedHeaderSurface.BorderThickness = new Thickness(1);
        _draggedHeaderSurface.Opacity = 0.25;
        Canvas.SetZIndex(_draggedHeaderSurface, 100);
    }

    private void OnHeaderDragDelta(object sender, DragDeltaEventArgs e)
    {
        _headerDragOffset += e.HorizontalChange;
        if (_dragPreview is not null)
        {
            Canvas.SetLeft(_dragPreview, _dragPreviewOrigin.X + _headerDragOffset);
        }
    }

    private void OnHeaderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_draggedColumn is not TableColumn column)
        {
            return;
        }

        bool canceled = e.Canceled;
        if (_dragPreview is not null)
        {
            DragPreviewCanvas.Children.Remove(_dragPreview);
            _dragPreview = null;
        }

        if (_draggedHeaderSurface is not null)
        {
            _draggedHeaderSurface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _draggedHeaderSurface.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _draggedHeaderSurface.BorderThickness = new Thickness(0);
            _draggedHeaderSurface.Opacity = 1;
            Canvas.SetZIndex(_draggedHeaderSurface, 0);
        }

        if (!canceled)
        {
            ReorderColumn(column, GetColumnCenter(column) + _headerDragOffset);
        }

        _draggedColumn = null;
        _headerDragOffset = 0;
        _draggedHeaderSurface = null;
        RenderRows();
    }

    private double GetColumnCenter(TableColumn column)
    {
        double position = 8;
        foreach (TableColumn current in _columnOrder)
        {
            double width = _columnWidths[current];
            if (current == column)
            {
                return position + width / 2;
            }

            position += width + 8;
        }

        throw new ArgumentOutOfRangeException(nameof(column));
    }

    private void ReorderColumn(TableColumn column, double targetCenter)
    {
        var reordered = _columnOrder.Where(current => current != column).ToList();
        int insertionIndex = 0;
        double left = 8;
        foreach (TableColumn current in reordered)
        {
            double width = _columnWidths[current];
            if (targetCenter < left + width / 2)
            {
                break;
            }

            insertionIndex++;
            left += width + 8;
        }

        reordered.Insert(insertionIndex, column);
        if (_columnOrder.SequenceEqual(reordered))
        {
            return;
        }

        _columnOrder.Clear();
        _columnOrder.AddRange(reordered);
        SaveSettings();
    }

    private static SortMetric? GetSortMetric(TableColumn column) =>
        column switch
        {
            TableColumn.Name => SortMetric.Name,
            TableColumn.Cpu => SortMetric.Cpu,
            TableColumn.Memory => SortMetric.Memory,
            TableColumn.Read => SortMetric.Read,
            TableColumn.Write => SortMetric.Write,
            _ => null,
        };

    private static string GetColumnTitle(TableColumn column) =>
        column switch
        {
            TableColumn.Name => "App / process",
            TableColumn.Cpu => "CPU",
            TableColumn.Memory => "Memory",
            TableColumn.Read => "Process I/O · Read",
            TableColumn.Write => "Process I/O · Write",
            TableColumn.Actions => "Actions",
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

    private void AttachProcessContextMenu(
        FrameworkElement target,
        IReadOnlyList<ProcessContextAction> actions)
    {
        var menu = new MenuFlyout();
        var openLocation = new MenuFlyoutItem
        {
            Text = actions[0].Title,
            Tag = actions[0].ExecutablePath,
        };
        openLocation.Click += OnOpenFileLocation;
        menu.Items.Add(openLocation);

        var searchOnline = new MenuFlyoutItem
        {
            Text = actions[1].Title,
            Tag = actions[1].SearchQuery,
        };
        searchOnline.Click += OnSearchOnline;
        menu.Items.Add(searchOnline);
        target.ContextFlyout = menu;
    }

    private void OnOpenFileLocation(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string executablePath } ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            ShowNotice("The executable path is unavailable, so its file location cannot be opened.");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (directory is null || !Directory.Exists(directory))
            {
                ShowNotice("The executable folder is unavailable.");
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { directory },
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            ShowNotice($"Could not open file location: {ex.Message}");
        }
    }

    private void OnSearchOnline(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string searchName } || string.IsNullOrWhiteSpace(searchName))
        {
            ShowNotice("The app or process name is unavailable for online search.");
            return;
        }

        try
        {
            var searchUri = new Uri($"https://www.bing.com/search?q={Uri.EscapeDataString(searchName)}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = searchUri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        {
            ShowNotice($"Could not start the default browser: {ex.Message}");
        }
    }

    private void SetDefaultWindowSize()
    {
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "PKillerIcon.ico"));
        appWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Round(800 * scale),
            (int)Math.Round(820 * scale)));
    }

    private async void OnEndApp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppGroup group })
        {
            return;
        }

        await StopGroupsAsync(new[] { group });
    }

    private async void OnEndSelected(object sender, RoutedEventArgs e)
    {
        var groups = _allGroups.Where(g => _selectedKeys.Contains(g.Key)).ToList();
        if (groups.Count > 0)
        {
            await StopGroupsAsync(groups);
        }
    }

    private async Task StopGroupsAsync(IReadOnlyList<AppGroup> groups)
    {
        var forceGroups = groups.Where(g => _stopPlanner.ActionFor(g) != StopAction.GracefulClose).ToList();
        var closeGroups = groups.Where(g => _stopPlanner.ActionFor(g) == StopAction.GracefulClose).ToList();

        bool forceConfirmed = false;
        if (forceGroups.Count > 0)
        {
            var targets = forceGroups.Select(AppStopTarget.From).ToList();
            var forceMessage = _stopPlanner.ConfirmationMessage(targets, StopAction.ForceStop);
            forceConfirmed = await ConfirmAsync(forceMessage, "Force stop");
        }

        bool closeConfirmed = false;
        if (closeGroups.Count > 0)
        {
            var targets = closeGroups.Select(AppStopTarget.From).ToList();
            closeConfirmed = !_stopPlanner.RequiresConfirmation(targets, StopAction.GracefulClose);
            if (!closeConfirmed)
            {
                var message = _stopPlanner.ConfirmationMessage(targets, StopAction.GracefulClose);
                closeConfirmed = await ConfirmAsync(message, "Close apps");
            }
        }

        if (forceConfirmed)
        {
            var targets = forceGroups.Select(AppStopTarget.From).ToList();
            var outcomes = targets.SelectMany(_stopCoordinator.ForceStop).ToList();
            ShowOutcomes(outcomes);
        }

        if (closeConfirmed)
        {
            var targets = closeGroups.Select(AppStopTarget.From).ToList();
            StatusText.Text = closeGroups.Count == 1
                ? $"Waiting up to 10 seconds for {closeGroups[0].DisplayName} to close…"
                : $"Asking {closeGroups.Count} apps to close; waiting up to 10 seconds…";
            var sessions = await _stopCoordinator.CloseBatchAsync(targets, CancellationToken.None);
            var outcomes = sessions.SelectMany(s => s.Outcomes).ToList();
            ShowOutcomes(outcomes);

            if (sessions.Any(s => s.OffersForceStop))
            {
                var stillOpen = sessions
                    .Where(s => s.OffersForceStop)
                    .Select(s => new AppStopTarget(
                        s.Target.Key,
                        s.Target.DisplayName,
                        s.StillRunning))
                    .ToList();
                var forceMessage = _stopPlanner.ConfirmationMessage(stillOpen, StopAction.ForceStop);
                if (await ConfirmAsync(forceMessage, "Force stop"))
                {
                    ShowOutcomes(stillOpen.SelectMany(_stopCoordinator.ForceStop).ToList());
                }
            }
        }

        await RefreshProcessesAsync(force: true);
    }

    private async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm action",
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowOutcomes(IReadOnlyList<StopOutcome> outcomes)
    {
        var failures = outcomes.Where(o => o.DidNotStop).ToList();
        if (failures.Count > 0)
        {
            ShowNotice(string.Join(Environment.NewLine, failures.Select(f => $"{f.Identity.Name}: {f.Message}")));
            StatusText.Text = $"{outcomes.Count - failures.Count} stopped or closed · {failures.Count} could not be stopped";
        }
        else
        {
            HideNotice();
            StatusText.Text = $"{outcomes.Count} process(es) stopped or closed.";
        }
    }

    private void OnExpandGroup(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppGroup group })
        {
            if (!_expandedKeys.Add(group.Key))
            {
                _expandedKeys.Remove(group.Key);
            }

            RenderRows();
        }
    }

    private void OnAppChecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AppGroup group })
        {
            _selectedKeys.Add(group.Key);
            UpdateSelection();
        }
    }

    private void OnAppUnchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: AppGroup group })
        {
            _selectedKeys.Remove(group.Key);
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        SelectionText.Text = $"{_selectedKeys.Count} selected";
        EndSelectedButton.IsEnabled = _selectedKeys.Count > 0;
    }

    private void OnSort(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SortMetric metric })
        {
            var direction = _sort.Metric == metric && _sort.Direction == SortDirection.Descending
                ? SortDirection.Ascending
                : SortDirection.Descending;
            _sort = new SortOrder(metric, direction);
            SaveSettings();
            _ = RefreshProcessesAsync(force: true);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text;
        _ = RefreshProcessesAsync(force: true);
    }

    private void OnRefreshToggle(object sender, RoutedEventArgs e)
    {
        _autoRefresh = !_autoRefresh;
        RefreshButton.Content = _autoRefresh ? "Pause updates" : "Resume updates";
        if (!_autoRefresh)
        {
            _resetCpuSampler = true;
            _lastSample = default;
        }
        else
        {
            _ = RefreshProcessesAsync(force: true);
        }

        SaveSettings();
    }

    private void OnRelaunchAdmin(object sender, RoutedEventArgs e)
    {
        if (!_elevation.CanRelaunchAsAdministrator)
        {
            return;
        }

        if (_elevation.RelaunchAsAdministrator())
        {
            Application.Current.Exit();
        }
        else
        {
            ShowNotice("Administrator relaunch did not start. You may have cancelled the Windows prompt.");
        }
    }

    private void SaveSettings() => _settingsStore.Save(new AppSettings(_sort, _autoRefresh, _columnOrder.ToArray()));

    private void ShowNotice(string message)
    {
        NoticeText.Text = message;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    private void HideNotice() => NoticeBorder.Visibility = Visibility.Collapsed;

    private static string FormatBytes(long bytes)
    {
        double mb = MetricsAggregator.WorkingSetMb(bytes);
        return mb >= 1024 ? $"{mb / 1024:N1} GB" : $"{mb:N1} MB";
    }

}
