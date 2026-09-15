using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private Grid? _responsiveBodyGrid;
    private ColumnDefinition? _leftSidebarColumn;
    private ColumnDefinition? _leftSplitterColumn;
    private ColumnDefinition? _rightSplitterColumn;
    private ColumnDefinition? _rightSidebarColumn;
    private GridSplitter? _leftResponsiveSplitter;
    private GridSplitter? _rightResponsiveSplitter;
    private Button? _leftPanelToggleButton;
    private Button? _rightPanelToggleButton;

    private double _leftSidebarStoredWidth = 250;
    private double _rightSidebarStoredWidth = 350;
    private bool _leftUserCollapsed;
    private bool _rightUserCollapsed;
    private bool _leftAutoCollapsed;
    private bool _rightAutoCollapsed;
    private bool _responsiveLayoutInitialized;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InitializeResponsiveLayout();
    }

    private void InitializeResponsiveLayout()
    {
        if (_responsiveLayoutInitialized)
        {
            return;
        }

        _responsiveBodyGrid = FindMainBodyGrid();
        if (_responsiveBodyGrid is null || _responsiveBodyGrid.ColumnDefinitions.Count < 5)
        {
            return;
        }

        _leftSidebarColumn = _responsiveBodyGrid.ColumnDefinitions[0];
        _leftSplitterColumn = _responsiveBodyGrid.ColumnDefinitions[1];
        _rightSplitterColumn = _responsiveBodyGrid.ColumnDefinitions[3];
        _rightSidebarColumn = _responsiveBodyGrid.ColumnDefinitions[4];

        _leftResponsiveSplitter = _responsiveBodyGrid.Children
            .OfType<GridSplitter>()
            .FirstOrDefault(splitter => Grid.GetColumn(splitter) == 1);
        _rightResponsiveSplitter = _responsiveBodyGrid.Children
            .OfType<GridSplitter>()
            .FirstOrDefault(splitter => Grid.GetColumn(splitter) == 3);

        BuildResponsiveHeaderButtons();

        MinWidth = 860;
        MinHeight = 620;
        SizeChanged += MainWindow_ResponsiveSizeChanged;
        _responsiveLayoutInitialized = true;

        ApplyResponsiveLayout(ActualWidth);
    }

    private Grid? FindMainBodyGrid()
    {
        DependencyObject? current = ConversationList;
        while (current is not null)
        {
            if (current is Grid grid && grid.ColumnDefinitions.Count >= 5)
            {
                return grid;
            }

            current = GetResponsiveParent(current);
        }

        return null;
    }

    private static DependencyObject? GetResponsiveParent(DependencyObject current)
    {
        if (current is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement)
                ?? (contentElement as FrameworkContentElement)?.Parent;
        }

        var visualParent = VisualTreeHelper.GetParent(current);
        if (visualParent is not null)
        {
            return visualParent;
        }

        return current is FrameworkElement frameworkElement
            ? frameworkElement.Parent
            : null;
    }

    private void BuildResponsiveHeaderButtons()
    {
        if (SettingsButton.Parent is not Grid headerGrid)
        {
            return;
        }

        headerGrid.Children.Remove(SettingsButton);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerActions, 1);

        var headerButtonStyle = TryFindResource("HeaderButtonStyle") as Style;

        _leftPanelToggleButton = new Button
        {
            Content = "☰  대화",
            Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "대화 목록 패널 표시/숨김"
        };
        if (headerButtonStyle is not null)
        {
            _leftPanelToggleButton.Style = headerButtonStyle;
        }
        _leftPanelToggleButton.Click += LeftPanelToggleButton_Click;

        _rightPanelToggleButton = new Button
        {
            Content = "▤  기록",
            Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "현재 대화 패널 표시/숨김"
        };
        if (headerButtonStyle is not null)
        {
            _rightPanelToggleButton.Style = headerButtonStyle;
        }
        _rightPanelToggleButton.Click += RightPanelToggleButton_Click;

        headerActions.Children.Add(_leftPanelToggleButton);
        headerActions.Children.Add(_rightPanelToggleButton);
        headerActions.Children.Add(SettingsButton);
        headerGrid.Children.Add(headerActions);
    }

    private void LeftPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_leftSidebarColumn is null)
        {
            return;
        }

        var currentlyVisible = _leftSidebarColumn.Width.Value > 0;
        _leftUserCollapsed = currentlyVisible;
        _leftAutoCollapsed = false;
        SetLeftSidebarVisible(!currentlyVisible);

        if (!currentlyVisible && ActualWidth < 1000)
        {
            _rightUserCollapsed = false;
            _rightAutoCollapsed = true;
            SetRightSidebarVisible(false);
        }
    }

    private void RightPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rightSidebarColumn is null)
        {
            return;
        }

        var currentlyVisible = _rightSidebarColumn.Width.Value > 0;
        _rightUserCollapsed = currentlyVisible;
        _rightAutoCollapsed = false;
        SetRightSidebarVisible(!currentlyVisible);

        if (!currentlyVisible && ActualWidth < 1000)
        {
            _leftUserCollapsed = false;
            _leftAutoCollapsed = true;
            SetLeftSidebarVisible(false);
        }
    }

    private void MainWindow_ResponsiveSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (!_responsiveLayoutInitialized)
        {
            return;
        }

        if (width < 1000)
        {
            if (!_leftUserCollapsed)
            {
                _leftAutoCollapsed = true;
                SetLeftSidebarVisible(false);
            }

            if (!_rightUserCollapsed)
            {
                _rightAutoCollapsed = true;
                SetRightSidebarVisible(false);
            }
            return;
        }

        if (width < 1240)
        {
            if (!_leftUserCollapsed)
            {
                _leftAutoCollapsed = true;
                SetLeftSidebarVisible(false);
            }

            if (_rightAutoCollapsed && !_rightUserCollapsed)
            {
                _rightAutoCollapsed = false;
                SetRightSidebarVisible(true);
            }
            return;
        }

        if (_leftAutoCollapsed && !_leftUserCollapsed)
        {
            _leftAutoCollapsed = false;
            SetLeftSidebarVisible(true);
        }

        if (_rightAutoCollapsed && !_rightUserCollapsed)
        {
            _rightAutoCollapsed = false;
            SetRightSidebarVisible(true);
        }
    }

    private void SetLeftSidebarVisible(bool visible)
    {
        if (_leftSidebarColumn is null || _leftSplitterColumn is null)
        {
            return;
        }

        if (visible)
        {
            _leftSidebarColumn.MinWidth = 190;
            _leftSidebarColumn.MaxWidth = 310;
            _leftSidebarColumn.Width = new GridLength(Math.Clamp(_leftSidebarStoredWidth, 190, 310));
            _leftSplitterColumn.Width = new GridLength(6);
            if (_leftResponsiveSplitter is not null)
            {
                _leftResponsiveSplitter.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (_leftSidebarColumn.ActualWidth > 40)
            {
                _leftSidebarStoredWidth = _leftSidebarColumn.ActualWidth;
            }

            _leftSidebarColumn.MinWidth = 0;
            _leftSidebarColumn.MaxWidth = 0;
            _leftSidebarColumn.Width = new GridLength(0);
            _leftSplitterColumn.Width = new GridLength(0);
            if (_leftResponsiveSplitter is not null)
            {
                _leftResponsiveSplitter.Visibility = Visibility.Collapsed;
            }
        }

        UpdateResponsiveToggleLabels();
    }

    private void SetRightSidebarVisible(bool visible)
    {
        if (_rightSidebarColumn is null || _rightSplitterColumn is null)
        {
            return;
        }

        if (visible)
        {
            _rightSidebarColumn.MinWidth = 300;
            _rightSidebarColumn.MaxWidth = 440;
            _rightSidebarColumn.Width = new GridLength(Math.Clamp(_rightSidebarStoredWidth, 300, 440));
            _rightSplitterColumn.Width = new GridLength(6);
            if (_rightResponsiveSplitter is not null)
            {
                _rightResponsiveSplitter.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (_rightSidebarColumn.ActualWidth > 40)
            {
                _rightSidebarStoredWidth = _rightSidebarColumn.ActualWidth;
            }

            _rightSidebarColumn.MinWidth = 0;
            _rightSidebarColumn.MaxWidth = 0;
            _rightSidebarColumn.Width = new GridLength(0);
            _rightSplitterColumn.Width = new GridLength(0);
            if (_rightResponsiveSplitter is not null)
            {
                _rightResponsiveSplitter.Visibility = Visibility.Collapsed;
            }
        }

        UpdateResponsiveToggleLabels();
    }

    private void UpdateResponsiveToggleLabels()
    {
        if (_leftPanelToggleButton is not null && _leftSidebarColumn is not null)
        {
            _leftPanelToggleButton.Content = _leftSidebarColumn.Width.Value > 0 ? "☰  대화" : "☰  대화 열기";
        }

        if (_rightPanelToggleButton is not null && _rightSidebarColumn is not null)
        {
            _rightPanelToggleButton.Content = _rightSidebarColumn.Width.Value > 0 ? "▤  기록" : "▤  기록 열기";
        }
    }
}
