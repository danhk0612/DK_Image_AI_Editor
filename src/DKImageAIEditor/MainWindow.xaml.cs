using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DKImageAIEditor.Models;
using DKImageAIEditor.Services;
using DKImageAIEditor.Views;
using Microsoft.Win32;

namespace DKImageAIEditor;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    private readonly ConversationStore _conversationStore = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly OpenRouterImageService _openRouterImageService = new();
    private readonly ImageRegionService _imageRegionService = new();

    private ConversationRecord? _currentConversation;
    private string? _currentImagePath;
    private bool _isBusy;
    private bool _isSelecting;
    private bool _showingOriginal;
    private Point _selectionStart;
    private Int32Rect? _selectionPixelRect;
    private ImageEditMode _editMode = ImageEditMode.Full;

    public MainWindow()
    {
        InitializeComponent();
        ConversationList.SelectionChanged += ConversationList_SelectionChanged;
        SelectionCanvas.SizeChanged += (_, _) => RenderSelectionRectangle();
        SetEditMode(ImageEditMode.Full);
        RefreshConversationList();
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "편집할 이미지 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|Bitmap|*.bmp",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            StartConversation(dialog.FileName);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var settingsWindow = new SettingsWindow
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isBusy && TryGetSingleImagePath(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (TryGetSingleImagePath(e.Data, out var imagePath))
        {
            StartConversation(imagePath!);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isBusy)
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (ConversationList.SelectedItem is ListBoxItem { Tag: ConversationRecord conversation })
        {
            ShowConversation(conversation);
        }
    }

    private void DeleteConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (sender is not Button { Tag: string conversationId })
        {
            return;
        }

        var conversation = _conversationStore.GetConversation(conversationId);
        if (conversation is null)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"'{conversation.Title}' 대화와 저장된 이미지 기록을 삭제하시겠습니까?",
            "대화 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var deletedCurrentConversation = _currentConversation?.Id == conversationId;
        _conversationStore.DeleteConversation(conversationId);

        if (deletedCurrentConversation)
        {
            _currentConversation = null;
            _currentImagePath = null;
        }

        RefreshConversationList();
        e.Handled = true;
    }

    private void FullEditModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetEditMode(ImageEditMode.Full);
    }

    private void RegionEditModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetEditMode(ImageEditMode.Region);
    }

    private void CropEditModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetEditMode(ImageEditMode.Crop);
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void CompareOriginalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            return;
        }

        _showingOriginal = !_showingOriginal;
        var path = _showingOriginal
            ? _currentConversation.OriginalImagePath
            : _currentImagePath;

        EditorImage.Source = LoadBitmap(path);
        CompareOriginalButton.Content = _showingOriginal ? "현재 보기" : "원본 비교";
        RenderSelectionRectangle();
    }

    private void SaveCurrentImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || string.IsNullOrWhiteSpace(_currentImagePath) || !File.Exists(_currentImagePath))
        {
            return;
        }

        var extension = Path.GetExtension(_currentImagePath);
        var dialog = new SaveFileDialog
        {
            Title = "현재 이미지 저장",
            FileName = _currentConversation?.Title + extension,
            DefaultExt = extension,
            Filter = $"이미지 파일|*{extension}|모든 파일|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.Copy(_currentImagePath, dialog.FileName, true);
            OperationStatusTextBlock.Text = $"저장됨: {dialog.FileName}";
        }
    }

    private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isBusy || _editMode == ImageEditMode.Full || EditorImage.Source is not BitmapSource)
        {
            return;
        }

        var imageRect = GetDisplayedImageRect();
        if (imageRect.IsEmpty)
        {
            return;
        }

        var point = e.GetPosition(SelectionCanvas);
        if (!imageRect.Contains(point))
        {
            return;
        }

        _isSelecting = true;
        _selectionStart = point;
        _selectionPixelRect = null;
        SelectionCanvas.CaptureMouse();
        UpdateSelectionVisual(point, point);
        e.Handled = true;
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        var point = ClampToRect(e.GetPosition(SelectionCanvas), GetDisplayedImageRect());
        UpdateSelectionVisual(_selectionStart, point);
    }

    private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();

        var imageRect = GetDisplayedImageRect();
        var point = ClampToRect(e.GetPosition(SelectionCanvas), imageRect);
        var selectedVisualRect = NormalizeRect(_selectionStart, point);

        if (selectedVisualRect.Width < 4 || selectedVisualRect.Height < 4)
        {
            ClearSelection();
            return;
        }

        _selectionPixelRect = VisualRectToPixelRect(selectedVisualRect, imageRect);
        RenderSelectionRectangle();

        if (_selectionPixelRect is { } selection)
        {
            OperationStatusTextBlock.Text =
                $"선택 영역: X {selection.X}, Y {selection.Y}, {selection.Width} × {selection.Height}px";
        }

        e.Handled = true;
    }

    private async void EditImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        if (_currentConversation is null || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            MessageBox.Show(this, "먼저 편집할 이미지 대화를 선택하세요.", "이미지 편집", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_editMode != ImageEditMode.Full && _selectionPixelRect is null)
        {
            MessageBox.Show(
                this,
                "이미지에서 수정할 영역을 마우스로 드래그해 선택하세요.",
                "영역 선택 필요",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var prompt = PromptTextBox.Text.Trim();
        if (prompt.Length == 0)
        {
            MessageBox.Show(this, "수정 요청을 입력하세요.", "이미지 편집", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var apiKey = CredentialStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, "설정에서 OpenRouter API Key를 입력하세요.", "OpenRouter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = _settingsService.Load();
        var modelId = settings.EffectiveModelId;
        var conversationId = _currentConversation.Id;
        var sourceImagePath = _currentImagePath;
        var edits = _conversationStore.GetEdits(conversationId);
        var sequence = edits.Count + 1;
        var stopwatch = Stopwatch.StartNew();
        var editMode = _editMode;
        var selection = _selectionPixelRect;

        SetOperationState(true, "요청 준비 중...");
        await Task.Yield();

        try
        {
            byte[] requestBytes;
            string requestMediaType;
            PreparedRegionRequest? preparedRegion = null;
            string requestPrompt = prompt;

            if (editMode == ImageEditMode.Full)
            {
                await UpdateOperationStatusAsync("원본 이미지를 읽는 중...");
                requestBytes = await File.ReadAllBytesAsync(sourceImagePath);
                requestMediaType = GetImageMediaType(sourceImagePath);
            }
            else
            {
                await UpdateOperationStatusAsync(
                    editMode == ImageEditMode.Region
                        ? "선택 영역과 주변 문맥을 준비하는 중..."
                        : "선택 영역을 잘라내는 중...");

                preparedRegion = _imageRegionService.PrepareRequest(
                    sourceImagePath,
                    selection!.Value,
                    editMode == ImageEditMode.Region);
                requestBytes = preparedRegion.RequestImageBytes;
                requestMediaType = "image/png";
                requestPrompt = BuildRegionPrompt(prompt, preparedRegion, editMode);
            }

            await UpdateOperationStatusAsync($"OpenRouter 응답 대기 중... ({modelId})");
            var result = await _openRouterImageService.EditImageAsync(
                apiKey,
                modelId,
                requestBytes,
                requestMediaType,
                requestPrompt);

            await UpdateOperationStatusAsync("결과 이미지를 합성하고 저장하는 중...");
            byte[] outputBytes;
            string outputExtension;

            if (preparedRegion is null)
            {
                outputBytes = result.ImageBytes;
                outputExtension = GetImageExtension(result.MediaType);
            }
            else
            {
                outputBytes = _imageRegionService.ComposeResult(
                    sourceImagePath,
                    result.ImageBytes,
                    preparedRegion);
                outputExtension = ".png";
            }

            var outputPath = _conversationStore.GetVersionImagePath(
                conversationId,
                sequence,
                outputExtension);
            await File.WriteAllBytesAsync(outputPath, outputBytes);

            await UpdateOperationStatusAsync("편집 기록을 저장하는 중...");
            var createdAt = DateTimeOffset.UtcNow;
            _conversationStore.AddEdit(new EditRecord(
                Guid.NewGuid().ToString("N"),
                conversationId,
                sequence,
                prompt,
                modelId,
                GetEditModeDisplayName(editMode),
                selection?.X,
                selection?.Y,
                selection?.Width,
                selection?.Height,
                sourceImagePath,
                outputPath,
                createdAt));

            await UpdateOperationStatusAsync("화면을 갱신하는 중...");
            PromptTextBox.Clear();
            ClearSelection(false);
            _showingOriginal = false;
            CompareOriginalButton.Content = "원본 비교";

            _isBusy = false;
            RefreshConversationList(conversationId);

            stopwatch.Stop();
            SetOperationState(false, $"완료 ({stopwatch.Elapsed.TotalSeconds:F1}초)");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            SetOperationState(false, $"실패 ({stopwatch.Elapsed.TotalSeconds:F1}초)");

            MessageBox.Show(
                this,
                exception.Message,
                "이미지 편집 실패",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildRegionPrompt(
        string userPrompt,
        PreparedRegionRequest request,
        ImageEditMode mode)
    {
        if (mode == ImageEditMode.Crop)
        {
            return $"수정 대상은 첨부된 이미지 전체입니다. 요청한 수정만 수행하고 가능한 한 기존 구도와 스타일을 유지하세요.\n\n사용자 요청:\n{userPrompt}";
        }

        var relativeX = request.SelectionRect.X - request.RequestRect.X;
        var relativeY = request.SelectionRect.Y - request.RequestRect.Y;

        return $"""
            첨부 이미지는 원본의 일부이며 주변 문맥을 포함합니다.
            실제 수정 대상은 다음 사각형 영역입니다.
            X={relativeX}, Y={relativeY}, Width={request.SelectionRect.Width}, Height={request.SelectionRect.Height} 픽셀.
            이 대상 영역만 사용자 요청대로 수정하고 대상 밖의 주변 문맥은 최대한 유지하세요.

            사용자 요청:
            {userPrompt}
            """;
    }

    private void SetOperationState(bool isBusy, string statusMessage)
    {
        _isBusy = isBusy;
        OperationStatusTextBlock.Text = statusMessage;
        OperationProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;

        EditImageButton.IsEnabled = !isBusy;
        EditImageButton.Content = isBusy ? "수정 중..." : "이미지 수정";
        PromptTextBox.IsEnabled = !isBusy;
        ConversationList.IsEnabled = !isBusy;
        NewConversationButton.IsEnabled = !isBusy;
        SettingsButton.IsEnabled = !isBusy;
        FullEditModeButton.IsEnabled = !isBusy;
        RegionEditModeButton.IsEnabled = !isBusy;
        CropEditModeButton.IsEnabled = !isBusy;
        ClearSelectionButton.IsEnabled = !isBusy;
        CompareOriginalButton.IsEnabled = !isBusy;
        SaveCurrentImageButton.IsEnabled = !isBusy;
        SelectionCanvas.IsHitTestVisible = !isBusy && _editMode != ImageEditMode.Full;
    }

    private async Task UpdateOperationStatusAsync(string statusMessage)
    {
        OperationStatusTextBlock.Text = statusMessage;
        await Task.Yield();
    }

    private void SetEditMode(ImageEditMode mode)
    {
        if (_isBusy)
        {
            return;
        }

        _editMode = mode;
        SelectionCanvas.IsHitTestVisible = mode != ImageEditMode.Full;
        SelectionCanvas.Cursor = mode == ImageEditMode.Full ? Cursors.Arrow : Cursors.Cross;

        FullEditModeButton.FontWeight = mode == ImageEditMode.Full ? FontWeights.Bold : FontWeights.Normal;
        RegionEditModeButton.FontWeight = mode == ImageEditMode.Region ? FontWeights.Bold : FontWeights.Normal;
        CropEditModeButton.FontWeight = mode == ImageEditMode.Crop ? FontWeights.Bold : FontWeights.Normal;

        OperationStatusTextBlock.Text = mode switch
        {
            ImageEditMode.Full => "전체 편집 모드: 이미지 전체가 수정 대상입니다.",
            ImageEditMode.Region => "영역 편집 모드: 이미지에서 수정할 부분을 사각형으로 드래그하세요.",
            ImageEditMode.Crop => "잘라서 편집 모드: AI에 보낼 영역을 사각형으로 드래그하세요.",
            _ => "준비됨"
        };
    }

    private void ClearSelection(bool updateStatus = true)
    {
        _selectionPixelRect = null;
        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();
        SelectionRectangle.Visibility = Visibility.Collapsed;

        if (updateStatus)
        {
            SetEditMode(_editMode);
        }
    }

    private Rect GetDisplayedImageRect()
    {
        if (EditorImage.Source is not BitmapSource bitmap ||
            SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(
            SelectionCanvas.ActualWidth / bitmap.PixelWidth,
            SelectionCanvas.ActualHeight / bitmap.PixelHeight);
        var width = bitmap.PixelWidth * scale;
        var height = bitmap.PixelHeight * scale;
        var left = (SelectionCanvas.ActualWidth - width) / 2.0;
        var top = (SelectionCanvas.ActualHeight - height) / 2.0;
        return new Rect(left, top, width, height);
    }

    private Int32Rect VisualRectToPixelRect(Rect visualRect, Rect displayedImageRect)
    {
        if (EditorImage.Source is not BitmapSource bitmap)
        {
            return Int32Rect.Empty;
        }

        var scaleX = bitmap.PixelWidth / displayedImageRect.Width;
        var scaleY = bitmap.PixelHeight / displayedImageRect.Height;

        var x = Math.Clamp(
            (int)Math.Round((visualRect.Left - displayedImageRect.Left) * scaleX),
            0,
            bitmap.PixelWidth - 1);
        var y = Math.Clamp(
            (int)Math.Round((visualRect.Top - displayedImageRect.Top) * scaleY),
            0,
            bitmap.PixelHeight - 1);
        var width = Math.Max(1, (int)Math.Round(visualRect.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(visualRect.Height * scaleY));

        width = Math.Min(width, bitmap.PixelWidth - x);
        height = Math.Min(height, bitmap.PixelHeight - y);
        return new Int32Rect(x, y, width, height);
    }

    private void RenderSelectionRectangle()
    {
        if (_selectionPixelRect is not { } selection ||
            EditorImage.Source is not BitmapSource bitmap)
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        var imageRect = GetDisplayedImageRect();
        if (imageRect.IsEmpty)
        {
            return;
        }

        var scaleX = imageRect.Width / bitmap.PixelWidth;
        var scaleY = imageRect.Height / bitmap.PixelHeight;
        var visualRect = new Rect(
            imageRect.Left + selection.X * scaleX,
            imageRect.Top + selection.Y * scaleY,
            selection.Width * scaleX,
            selection.Height * scaleY);

        Canvas.SetLeft(SelectionRectangle, visualRect.Left);
        Canvas.SetTop(SelectionRectangle, visualRect.Top);
        SelectionRectangle.Width = visualRect.Width;
        SelectionRectangle.Height = visualRect.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void UpdateSelectionVisual(Point start, Point end)
    {
        var rect = NormalizeRect(start, end);
        Canvas.SetLeft(SelectionRectangle, rect.Left);
        Canvas.SetTop(SelectionRectangle, rect.Top);
        SelectionRectangle.Width = rect.Width;
        SelectionRectangle.Height = rect.Height;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private static Rect NormalizeRect(Point a, Point b)
    {
        return new Rect(
            new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    private static Point ClampToRect(Point point, Rect rect)
    {
        if (rect.IsEmpty)
        {
            return point;
        }

        return new Point(
            Math.Clamp(point.X, rect.Left, rect.Right),
            Math.Clamp(point.Y, rect.Top, rect.Bottom));
    }

    private static bool TryGetSingleImagePath(IDataObject data, out string? imagePath)
    {
        imagePath = null;

        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var files = data.GetData(DataFormats.FileDrop) as string[];
        if (files is not { Length: 1 })
        {
            return false;
        }

        if (!SupportedImageExtensions.Contains(Path.GetExtension(files[0])))
        {
            return false;
        }

        imagePath = files[0];
        return true;
    }

    private void StartConversation(string imagePath)
    {
        var conversation = _conversationStore.CreateConversation(imagePath);
        RefreshConversationList(conversation.Id);
    }

    private void RefreshConversationList(string? selectedConversationId = null)
    {
        var conversations = _conversationStore.GetConversations();
        ConversationList.Items.Clear();

        if (conversations.Count == 0)
        {
            ConversationList.Items.Add(CreateEmptyConversationListItem());
            ClearWorkspace();
            return;
        }

        ListBoxItem? selectedItem = null;
        foreach (var conversation in conversations)
        {
            var item = CreateConversationListItem(conversation);
            ConversationList.Items.Add(item);

            if (conversation.Id == selectedConversationId)
            {
                selectedItem = item;
            }
        }

        ConversationList.SelectedItem = selectedItem ?? ConversationList.Items[0];
    }

    private ListBoxItem CreateConversationListItem(ConversationRecord conversation)
    {
        var edits = _conversationStore.GetEdits(conversation.Id);
        var latestEdit = edits.LastOrDefault();
        var thumbnailPath = File.Exists(conversation.CurrentImagePath)
            ? conversation.CurrentImagePath
            : conversation.OriginalImagePath;

        var thumbnail = new Image
        {
            Width = 54,
            Height = 54,
            Stretch = Stretch.UniformToFill,
            Source = LoadBitmap(thumbnailPath, 96),
            Margin = new Thickness(0, 0, 10, 0)
        };

        var titlePanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = conversation.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = latestEdit is null
                ? "아직 편집 요청이 없습니다."
                : latestEdit.Prompt.ReplaceLineEndings(" "),
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 125
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = conversation.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.Gray,
            FontSize = 10
        });

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(thumbnail, 0);
        Grid.SetColumn(titlePanel, 1);
        contentGrid.Children.Add(thumbnail);
        contentGrid.Children.Add(titlePanel);

        var deleteButton = new Button
        {
            Content = "삭제",
            Tag = conversation.Id,
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        deleteButton.Click += DeleteConversationButton_Click;
        Grid.SetColumn(deleteButton, 2);
        contentGrid.Children.Add(deleteButton);

        return new ListBoxItem
        {
            Tag = conversation,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = contentGrid
        };
    }

    private static ListBoxItem CreateEmptyConversationListItem()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "대화가 없습니다.",
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "새 대화에서 이미지를 선택하세요.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });

        return new ListBoxItem
        {
            IsEnabled = false,
            Padding = new Thickness(10),
            Content = panel
        };
    }

    private void ShowConversation(ConversationRecord conversation)
    {
        _currentConversation = conversation;
        _currentImagePath = conversation.CurrentImagePath;
        _showingOriginal = false;
        CompareOriginalButton.Content = "원본 비교";
        ClearSelection(false);

        EditorImage.Source = LoadBitmap(conversation.CurrentImagePath);
        EditorImage.Visibility = Visibility.Visible;
        CanvasPlaceholder.Visibility = Visibility.Collapsed;

        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(CreateHistoryTextCard(
            $"원본 이미지: {Path.GetFileName(conversation.OriginalImagePath)}",
            Color.FromRgb(241, 243, 246)));

        foreach (var edit in _conversationStore.GetEdits(conversation.Id))
        {
            ChatHistoryPanel.Children.Add(CreateHistoryTextCard(
                $"{edit.Prompt}\n\n[{edit.EditMode}]  {edit.ModelId}",
                Color.FromRgb(231, 240, 255)));

            if (File.Exists(edit.OutputImagePath))
            {
                var resultImage = new Image
                {
                    Source = LoadBitmap(edit.OutputImagePath, 420),
                    Stretch = Stretch.Uniform,
                    MaxHeight = 260
                };
                ChatHistoryPanel.Children.Add(new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(225, 228, 234)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 8, 0, 4),
                    Child = resultImage
                });
            }
        }

        SetEditMode(_editMode);
    }

    private static Border CreateHistoryTextCard(string text, Color backgroundColor)
    {
        return new Border
        {
            Background = new SolidColorBrush(backgroundColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void ClearWorkspace()
    {
        _currentConversation = null;
        _currentImagePath = null;
        _showingOriginal = false;
        EditorImage.Source = null;
        EditorImage.Visibility = Visibility.Collapsed;
        CanvasPlaceholder.Visibility = Visibility.Visible;
        CompareOriginalButton.Content = "원본 비교";
        ClearSelection(false);

        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(CreateHistoryTextCard(
            "이미지를 선택하면 편집 대화가 시작됩니다.",
            Color.FromRgb(241, 243, 246)));
    }

    private static string GetEditModeDisplayName(ImageEditMode mode)
    {
        return mode switch
        {
            ImageEditMode.Region => "영역 편집",
            ImageEditMode.Crop => "잘라서 편집",
            _ => "전체 편집"
        };
    }

    private static string GetImageMediaType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    private static string GetImageExtension(string mediaType)
    {
        return mediaType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
    }

    private static BitmapImage LoadBitmap(string imagePath, int decodePixelWidth = 0)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
