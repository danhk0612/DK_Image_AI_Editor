using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    private const double MinZoom = 1.0;
    private const double MaxZoom = 8.0;
    private const double ZoomStep = 1.15;

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

    private double _zoom = 1.0;
    private double _panX;
    private double _panY;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;

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

        var settingsWindow = new SettingsWindow { Owner = this };
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
        if (!_isBusy && TryGetSingleImagePath(e.Data, out var imagePath))
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
        if (_isBusy || sender is not Button { Tag: string conversationId })
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
            "이 대화와 저장된 이미지 기록을 삭제하시겠습니까?",
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

    private void FullEditModeButton_Click(object sender, RoutedEventArgs e) => SetEditMode(ImageEditMode.Full);
    private void RegionEditModeButton_Click(object sender, RoutedEventArgs e) => SetEditMode(ImageEditMode.Region);
    private void CropEditModeButton_Click(object sender, RoutedEventArgs e) => SetEditMode(ImageEditMode.Crop);
    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e) => ClearSelection();

    private void CompareOriginalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            return;
        }

        _showingOriginal = !_showingOriginal;
        var path = _showingOriginal ? _currentConversation.OriginalImagePath : _currentImagePath;
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
            FileName = $"DKImageAIEditor{extension}",
            DefaultExt = extension,
            Filter = $"이미지 파일|*{extension}|모든 파일|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.Copy(_currentImagePath, dialog.FileName, true);
            OperationStatusTextBlock.Text = $"저장됨: {dialog.FileName}";
        }
    }

    private void EditorSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isBusy || EditorImage.Source is not BitmapSource)
        {
            return;
        }

        var factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        var nextZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - _zoom) < 0.001)
        {
            return;
        }

        _zoom = nextZoom;
        if (_zoom <= MinZoom + 0.001)
        {
            _panX = 0;
            _panY = 0;
        }

        ApplyViewportTransform();
        OperationStatusTextBlock.Text = $"확대: {_zoom * 100:F0}% · 가운데 버튼 드래그로 이동";
        e.Handled = true;
    }

    private void EditorSurface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isBusy || e.ChangedButton != MouseButton.Middle || EditorImage.Source is not BitmapSource)
        {
            return;
        }

        _isPanning = true;
        _panStart = e.GetPosition(EditorSurface);
        _panStartX = _panX;
        _panStartY = _panY;
        EditorSurface.CaptureMouse();
        EditorSurface.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void EditorSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        var point = e.GetPosition(EditorSurface);
        _panX = _panStartX + point.X - _panStart.X;
        _panY = _panStartY + point.Y - _panStart.Y;
        ApplyViewportTransform();
        e.Handled = true;
    }

    private void EditorSurface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPanning || e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _isPanning = false;
        EditorSurface.ReleaseMouseCapture();
        EditorSurface.Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void ApplyViewportTransform()
    {
        ViewportScaleTransform.ScaleX = _zoom;
        ViewportScaleTransform.ScaleY = _zoom;
        ViewportTranslateTransform.X = _panX;
        ViewportTranslateTransform.Y = _panY;
    }

    private void ResetViewportTransform()
    {
        _zoom = 1.0;
        _panX = 0;
        _panY = 0;
        _isPanning = false;
        EditorSurface.ReleaseMouseCapture();
        EditorSurface.Cursor = Cursors.Arrow;
        ApplyViewportTransform();
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
        HideSelectionHandles();
        SelectionCanvas.CaptureMouse();
        UpdateSelectionVisual(point, point);
        e.Handled = true;
    }

    private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isSelecting)
        {
            UpdateSelectionVisual(_selectionStart, ClampToRect(e.GetPosition(SelectionCanvas), GetDisplayedImageRect()));
        }
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
        UpdateSelectionStatus();
        e.Handled = true;
    }

    private void SelectionMoveThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isBusy || _selectionPixelRect is not { } selection || EditorImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        var imageRect = GetDisplayedImageRect();
        if (imageRect.IsEmpty)
        {
            return;
        }

        var deltaX = (int)Math.Round(e.HorizontalChange * bitmap.PixelWidth / imageRect.Width);
        var deltaY = (int)Math.Round(e.VerticalChange * bitmap.PixelHeight / imageRect.Height);
        var x = Math.Clamp(selection.X + deltaX, 0, bitmap.PixelWidth - selection.Width);
        var y = Math.Clamp(selection.Y + deltaY, 0, bitmap.PixelHeight - selection.Height);

        _selectionPixelRect = new Int32Rect(x, y, selection.Width, selection.Height);
        RenderSelectionRectangle();
        UpdateSelectionStatus();
    }

    private void SelectionResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_isBusy || sender is not Thumb { Tag: string handle } ||
            _selectionPixelRect is not { } selection || EditorImage.Source is not BitmapSource bitmap)
        {
            return;
        }

        var imageRect = GetDisplayedImageRect();
        if (imageRect.IsEmpty)
        {
            return;
        }

        var deltaX = (int)Math.Round(e.HorizontalChange * bitmap.PixelWidth / imageRect.Width);
        var deltaY = (int)Math.Round(e.VerticalChange * bitmap.PixelHeight / imageRect.Height);
        var left = selection.X;
        var top = selection.Y;
        var right = selection.X + selection.Width;
        var bottom = selection.Y + selection.Height;
        const int minSize = 2;

        if (handle.Contains('L'))
        {
            left = Math.Clamp(left + deltaX, 0, right - minSize);
        }
        else
        {
            right = Math.Clamp(right + deltaX, left + minSize, bitmap.PixelWidth);
        }

        if (handle.Contains('T'))
        {
            top = Math.Clamp(top + deltaY, 0, bottom - minSize);
        }
        else
        {
            bottom = Math.Clamp(bottom + deltaY, top + minSize, bitmap.PixelHeight);
        }

        _selectionPixelRect = new Int32Rect(left, top, right - left, bottom - top);
        RenderSelectionRectangle();
        UpdateSelectionStatus();
    }

    private async void EditImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            if (!_isBusy)
            {
                MessageBox.Show(this, "먼저 편집할 이미지 대화를 선택하세요.", "이미지 편집", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        if (_editMode != ImageEditMode.Full && _selectionPixelRect is null)
        {
            MessageBox.Show(this, "이미지에서 수정할 영역을 마우스로 드래그해 선택하세요.", "영역 선택 필요", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prompt = NormalizePrompt(PromptTextBox.Text);
        if (prompt.Length == 0)
        {
            MessageBox.Show(this, "수정 요청을 입력하세요.", "이미지 편집", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = _settingsService.Load();
        await ExecuteEditAsync(
            _currentConversation.Id,
            _currentImagePath,
            prompt,
            settings.EffectiveModelId,
            _editMode,
            _selectionPixelRect);
    }

    private async void RetryEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || sender is not Button { Tag: EditRecord edit })
        {
            return;
        }

        if (!File.Exists(edit.InputImagePath))
        {
            MessageBox.Show(this, "이 작업에 사용된 입력 이미지를 찾을 수 없습니다.", "다시 시도", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = ParseEditMode(edit.EditMode);
        Int32Rect? selection = null;
        if (edit.SelectionX.HasValue && edit.SelectionY.HasValue && edit.SelectionWidth.HasValue && edit.SelectionHeight.HasValue)
        {
            selection = new Int32Rect(
                (int)Math.Round(edit.SelectionX.Value),
                (int)Math.Round(edit.SelectionY.Value),
                (int)Math.Round(edit.SelectionWidth.Value),
                (int)Math.Round(edit.SelectionHeight.Value));
        }

        SelectHistoryImage(edit.InputImagePath, "재시도 입력 이미지");
        await ExecuteEditAsync(
            _currentConversation.Id,
            edit.InputImagePath,
            NormalizePrompt(edit.Prompt),
            edit.ModelId,
            mode,
            selection);
    }

    private async Task ExecuteEditAsync(
        string conversationId,
        string sourceImagePath,
        string prompt,
        string modelId,
        ImageEditMode editMode,
        Int32Rect? selection)
    {
        var apiKey = CredentialStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, "설정에서 OpenRouter API Key를 입력하세요.", "OpenRouter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (editMode != ImageEditMode.Full && selection is null)
        {
            MessageBox.Show(this, "이 작업의 선택 영역 정보가 없습니다.", "이미지 편집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sequence = _conversationStore.GetEdits(conversationId).Count + 1;
        var stopwatch = Stopwatch.StartNew();
        SetOperationState(true, "요청 준비 중...");
        await Task.Yield();

        try
        {
            byte[] requestBytes;
            string requestMediaType;
            PreparedRegionRequest? preparedRegion = null;
            var requestPrompt = prompt;

            if (editMode == ImageEditMode.Full)
            {
                await UpdateOperationStatusAsync("입력 이미지를 읽는 중...");
                requestBytes = await File.ReadAllBytesAsync(sourceImagePath);
                requestMediaType = GetImageMediaType(sourceImagePath);
            }
            else
            {
                await UpdateOperationStatusAsync(
                    editMode == ImageEditMode.Region
                        ? "선택 영역과 주변 문맥을 준비하는 중..."
                        : "선택 영역을 독립 이미지로 준비하는 중...");

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

            byte[] outputBytes;
            string outputExtension;

            if (editMode == ImageEditMode.Region && preparedRegion is not null)
            {
                await UpdateOperationStatusAsync("영역 결과를 원본과 자연스럽게 합성하는 중...");
                outputBytes = _imageRegionService.ComposeRegionResult(sourceImagePath, result.ImageBytes, preparedRegion);
                outputExtension = ".png";
            }
            else
            {
                await UpdateOperationStatusAsync(
                    editMode == ImageEditMode.Crop
                        ? "잘라서 편집한 독립 결과를 저장하는 중..."
                        : "결과 이미지를 저장하는 중...");
                outputBytes = result.ImageBytes;
                outputExtension = GetImageExtension(result.MediaType);
            }

            var outputPath = _conversationStore.GetVersionImagePath(conversationId, sequence, outputExtension);
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

            PromptTextBox.Clear();
            ClearSelection(false);
            _showingOriginal = false;
            CompareOriginalButton.Content = "원본 비교";

            _isBusy = false;
            RefreshConversationList(conversationId);
            SelectHistoryImage(outputPath, "최신 결과");

            stopwatch.Stop();
            SetOperationState(false, $"완료 ({stopwatch.Elapsed.TotalSeconds:F1}초)");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            SetOperationState(false, $"실패 ({stopwatch.Elapsed.TotalSeconds:F1}초)");
            MessageBox.Show(this, exception.Message, "이미지 편집 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BuildRegionPrompt(string userPrompt, PreparedRegionRequest request, ImageEditMode mode)
    {
        if (mode == ImageEditMode.Crop)
        {
            return $"수정 대상은 첨부된 이미지 전체입니다. 결과는 이 이미지 자체로 완성하세요. 요청한 수정만 수행하고 기존 구도와 스타일은 가능한 한 유지하세요.\n\n사용자 요청:\n{userPrompt}";
        }

        var relativeX = request.SelectionRect.X - request.RequestRect.X;
        var relativeY = request.SelectionRect.Y - request.RequestRect.Y;

        return $"""
            첨부 이미지는 원본의 일부이며 실제 수정 대상 주변 문맥까지 포함합니다.
            실제 수정 대상은 X={relativeX}, Y={relativeY}, Width={request.SelectionRect.Width}, Height={request.SelectionRect.Height} 픽셀입니다.
            대상 영역은 사용자 요청대로 수정하되, 대상 경계의 조명·색·선·텍스처가 주변과 자연스럽게 이어지도록 만드세요.
            대상 밖 주변 문맥은 가능한 한 유지하세요.

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
            ImageEditMode.Full => "전체 편집 모드: 현재 선택된 이미지 전체가 수정 대상입니다.",
            ImageEditMode.Region => "영역 편집 모드: 수정할 부분을 사각형으로 드래그하세요.",
            ImageEditMode.Crop => "잘라서 편집 모드: 독립 이미지로 편집할 부분을 사각형으로 드래그하세요.",
            _ => "준비됨"
        };
    }

    private void ClearSelection(bool updateStatus = true)
    {
        _selectionPixelRect = null;
        _isSelecting = false;
        SelectionCanvas.ReleaseMouseCapture();
        SelectionRectangle.Visibility = Visibility.Collapsed;
        HideSelectionHandles();
        if (updateStatus)
        {
            SetEditMode(_editMode);
        }
    }

    private Rect GetDisplayedImageRect()
    {
        if (EditorImage.Source is not BitmapSource bitmap || SelectionCanvas.ActualWidth <= 0 || SelectionCanvas.ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(SelectionCanvas.ActualWidth / bitmap.PixelWidth, SelectionCanvas.ActualHeight / bitmap.PixelHeight);
        var width = bitmap.PixelWidth * scale;
        var height = bitmap.PixelHeight * scale;
        return new Rect((SelectionCanvas.ActualWidth - width) / 2.0, (SelectionCanvas.ActualHeight - height) / 2.0, width, height);
    }

    private Int32Rect VisualRectToPixelRect(Rect visualRect, Rect displayedImageRect)
    {
        if (EditorImage.Source is not BitmapSource bitmap)
        {
            return Int32Rect.Empty;
        }

        var scaleX = bitmap.PixelWidth / displayedImageRect.Width;
        var scaleY = bitmap.PixelHeight / displayedImageRect.Height;
        var x = Math.Clamp((int)Math.Round((visualRect.Left - displayedImageRect.Left) * scaleX), 0, bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round((visualRect.Top - displayedImageRect.Top) * scaleY), 0, bitmap.PixelHeight - 1);
        var width = Math.Min(Math.Max(1, (int)Math.Round(visualRect.Width * scaleX)), bitmap.PixelWidth - x);
        var height = Math.Min(Math.Max(1, (int)Math.Round(visualRect.Height * scaleY)), bitmap.PixelHeight - y);
        return new Int32Rect(x, y, width, height);
    }

    private void RenderSelectionRectangle()
    {
        if (_selectionPixelRect is not { } selection || EditorImage.Source is not BitmapSource bitmap)
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
            HideSelectionHandles();
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
        PositionSelectionHandles(visualRect);
    }

    private void PositionSelectionHandles(Rect visualRect)
    {
        SelectionMoveThumb.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionMoveThumb, visualRect.Left);
        Canvas.SetTop(SelectionMoveThumb, visualRect.Top);
        SelectionMoveThumb.Width = visualRect.Width;
        SelectionMoveThumb.Height = visualRect.Height;

        PositionResizeThumb(SelectionTopLeftThumb, visualRect.Left, visualRect.Top);
        PositionResizeThumb(SelectionTopRightThumb, visualRect.Right, visualRect.Top);
        PositionResizeThumb(SelectionBottomLeftThumb, visualRect.Left, visualRect.Bottom);
        PositionResizeThumb(SelectionBottomRightThumb, visualRect.Right, visualRect.Bottom);
    }

    private static void PositionResizeThumb(Thumb thumb, double x, double y)
    {
        Canvas.SetLeft(thumb, x - thumb.Width / 2.0);
        Canvas.SetTop(thumb, y - thumb.Height / 2.0);
        thumb.Visibility = Visibility.Visible;
    }

    private void HideSelectionHandles()
    {
        SelectionMoveThumb.Visibility = Visibility.Collapsed;
        SelectionTopLeftThumb.Visibility = Visibility.Collapsed;
        SelectionTopRightThumb.Visibility = Visibility.Collapsed;
        SelectionBottomLeftThumb.Visibility = Visibility.Collapsed;
        SelectionBottomRightThumb.Visibility = Visibility.Collapsed;
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

    private void UpdateSelectionStatus()
    {
        if (_selectionPixelRect is { } selection)
        {
            OperationStatusTextBlock.Text = $"선택 영역: X {selection.X}, Y {selection.Y}, {selection.Width} × {selection.Height}px";
        }
    }

    private static Rect NormalizeRect(Point a, Point b) =>
        new(new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    private static Point ClampToRect(Point point, Rect rect) => rect.IsEmpty
        ? point
        : new Point(Math.Clamp(point.X, rect.Left, rect.Right), Math.Clamp(point.Y, rect.Top, rect.Bottom));

    private static bool TryGetSingleImagePath(IDataObject data, out string? imagePath)
    {
        imagePath = null;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
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
        var thumbnailPath = File.Exists(conversation.CurrentImagePath) ? conversation.CurrentImagePath : conversation.OriginalImagePath;
        var latestPrompt = latestEdit is null ? "새 이미지 대화" : NormalizePrompt(latestEdit.Prompt);
        var titleText = ToPreview(latestPrompt, 28);
        var detailText = latestEdit is null ? "아직 편집 요청이 없습니다." : ToPreview(latestPrompt, 44);

        var thumbnail = new Image
        {
            Width = 54,
            Height = 54,
            Stretch = Stretch.UniformToFill,
            Source = LoadBitmap(thumbnailPath, 96),
            Margin = new Thickness(0, 0, 10, 0)
        };

        var titlePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new TextBlock
        {
            Text = titleText,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 130
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = detailText,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.DimGray,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 130
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = conversation.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.Gray,
            FontSize = 10
        });

        var contentGrid = new Grid
        {
            ClipToBounds = true
        };
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
        panel.Children.Add(new TextBlock { Text = "대화가 없습니다.", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "새 대화에서 이미지를 선택하세요.",
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
        return new ListBoxItem { IsEnabled = false, Padding = new Thickness(10), Content = panel };
    }

    private void ShowConversation(ConversationRecord conversation)
    {
        _currentConversation = conversation;
        _currentImagePath = conversation.CurrentImagePath;
        _showingOriginal = false;
        CompareOriginalButton.Content = "원본 비교";
        ClearSelection(false);
        ShowImage(conversation.CurrentImagePath);

        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(CreateHistoryTextCard("최초 이미지", Color.FromRgb(241, 243, 246)));

        foreach (var edit in _conversationStore.GetEdits(conversation.Id))
        {
            ChatHistoryPanel.Children.Add(CreateHistoryTextCard(
                $"{NormalizePrompt(edit.Prompt)}\n\n[{edit.EditMode}]  {edit.ModelId}",
                Color.FromRgb(231, 240, 255)));

            if (File.Exists(edit.OutputImagePath))
            {
                ChatHistoryPanel.Children.Add(CreateHistoryResultCard(edit));
            }
        }

        SetEditMode(_editMode);
    }

    private Border CreateHistoryResultCard(EditRecord edit)
    {
        var panel = new StackPanel();
        var resultImage = new Image
        {
            Source = LoadBitmap(edit.OutputImagePath, 420),
            Stretch = Stretch.Uniform,
            MaxHeight = 260,
            Cursor = Cursors.Hand,
            Tag = edit.OutputImagePath,
            ToolTip = "클릭하면 이 이미지를 다음 편집의 기준으로 사용합니다."
        };
        resultImage.MouseLeftButtonUp += HistoryResultImage_MouseLeftButtonUp;
        panel.Children.Add(resultImage);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var selectButton = new Button
        {
            Content = "이 이미지에서 계속",
            Tag = edit.OutputImagePath,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0)
        };
        selectButton.Click += HistoryResultSelectButton_Click;
        buttonRow.Children.Add(selectButton);

        var retryButton = new Button
        {
            Content = "다시 시도",
            Tag = edit,
            Padding = new Thickness(8, 4, 8, 4)
        };
        retryButton.Click += RetryEditButton_Click;
        buttonRow.Children.Add(retryButton);
        panel.Children.Add(buttonRow);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(225, 228, 234)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 4),
            Child = panel
        };
    }

    private void HistoryResultImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isBusy && sender is Image { Tag: string imagePath })
        {
            SelectHistoryImage(imagePath, "선택한 이전 결과");
        }
    }

    private void HistoryResultSelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isBusy && sender is Button { Tag: string imagePath })
        {
            SelectHistoryImage(imagePath, "선택한 이전 결과");
        }
    }

    private void SelectHistoryImage(string imagePath, string label)
    {
        if (!File.Exists(imagePath))
        {
            return;
        }

        _currentImagePath = imagePath;
        _showingOriginal = false;
        CompareOriginalButton.Content = "원본 비교";
        ClearSelection(false);
        ShowImage(imagePath);
        OperationStatusTextBlock.Text = $"{label}: 이 이미지를 기준으로 다음 수정을 진행합니다.";
    }

    private void ShowImage(string imagePath)
    {
        ResetViewportTransform();
        EditorImage.Source = LoadBitmap(imagePath);
        EditorImage.Visibility = Visibility.Visible;
        CanvasPlaceholder.Visibility = Visibility.Collapsed;
        RenderSelectionRectangle();
    }

    private static Border CreateHistoryTextCard(string text, Color backgroundColor) => new()
    {
        Background = new SolidColorBrush(backgroundColor),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 8),
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
    };

    private void ClearWorkspace()
    {
        _currentConversation = null;
        _currentImagePath = null;
        _showingOriginal = false;
        ResetViewportTransform();
        EditorImage.Source = null;
        EditorImage.Visibility = Visibility.Collapsed;
        CanvasPlaceholder.Visibility = Visibility.Visible;
        CompareOriginalButton.Content = "원본 비교";
        ClearSelection(false);
        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(CreateHistoryTextCard("이미지를 선택하면 편집 대화가 시작됩니다.", Color.FromRgb(241, 243, 246)));
    }

    private static string NormalizePrompt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string ToPreview(string value, int maxLength)
    {
        var normalized = NormalizePrompt(value);
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }

    private static ImageEditMode ParseEditMode(string value) => value switch
    {
        "영역 편집" => ImageEditMode.Region,
        "잘라서 편집" => ImageEditMode.Crop,
        _ => ImageEditMode.Full
    };

    private static string GetEditModeDisplayName(ImageEditMode mode) => mode switch
    {
        ImageEditMode.Region => "영역 편집",
        ImageEditMode.Crop => "잘라서 편집",
        _ => "전체 편집"
    };

    private static string GetImageMediaType(string imagePath) => Path.GetExtension(imagePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "image/png"
    };

    private static string GetImageExtension(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        _ => ".png"
    };

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
