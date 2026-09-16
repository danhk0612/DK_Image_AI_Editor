using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DKImageAIEditor.Models;
using DKImageAIEditor.Services;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private sealed record RetryEditContext(EditRecord Edit, ComboBox ModelSelector);
    private TextBlock? _requestCostTextBlock;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureRequestCostDisplay();
        RefreshRequestModelSelector();
    }

    private void SettingsButtonWithRefresh_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton_Click(sender, e);
        RefreshRequestModelSelector();
    }

    private void EnsureRequestCostDisplay()
    {
        if (_requestCostTextBlock is not null || RequestModelComboBox.Parent is not Grid modelGrid)
        {
            return;
        }

        if (modelGrid.RowDefinitions.Count == 0)
        {
            modelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            modelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(RequestModelComboBox, 0);
        Grid.SetRow(EditImageButton, 0);

        _requestCostTextBlock = new TextBlock
        {
            Text = "예상 비용: 모델을 선택하세요.",
            Foreground = new SolidColorBrush(Color.FromRgb(105, 113, 126)),
            FontSize = 11,
            Margin = new Thickness(2, 5, 2, 0),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(_requestCostTextBlock, 1);
        Grid.SetColumn(_requestCostTextBlock, 0);
        Grid.SetColumnSpan(_requestCostTextBlock, 2);
        modelGrid.Children.Add(_requestCostTextBlock);

        RequestModelComboBox.SelectionChanged += RequestModelComboBox_SelectionChanged;
    }

    private void RefreshRequestModelSelector()
    {
        var settings = _settingsService.Load();
        var options = new List<OpenRouterModelPreset>(OpenRouterModelPreset.All);
        OpenRouterModelPreset? customOption = null;

        if (!string.IsNullOrWhiteSpace(settings.CustomModelId))
        {
            var customModelId = settings.CustomModelId.Trim();
            customOption = new OpenRouterModelPreset($"Custom · {customModelId}", customModelId);
            options.Add(customOption);
        }

        RequestModelComboBox.ItemsSource = options;

        if (settings.UseCustomModel && customOption is not null)
        {
            RequestModelComboBox.SelectedItem = customOption;
        }
        else
        {
            RequestModelComboBox.SelectedItem = options.FirstOrDefault(option =>
                string.Equals(option.ModelId, settings.SelectedModelId, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault();
        }

        _ = UpdateRequestCostAsync();
    }

    private async void RequestModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await UpdateRequestCostAsync();
    }

    private async Task UpdateRequestCostAsync()
    {
        if (_requestCostTextBlock is null ||
            RequestModelComboBox.SelectedItem is not OpenRouterModelPreset selected)
        {
            return;
        }

        await UpdateCostTextAsync(_requestCostTextBlock, selected.ModelId, _currentImagePath);
    }

    private async Task UpdateCostTextAsync(TextBlock target, string modelId, string? imagePath)
    {
        target.Text = "예상 비용: 조회 중...";
        try
        {
            var apiKey = CredentialStore.LoadApiKey();
            var megapixels = GetImageMegapixels(imagePath);
            var estimate = await _openRouterImageService.GetImageCostEstimateAsync(
                apiKey ?? string.Empty,
                modelId,
                megapixels);
            target.Text = $"예상 비용: {estimate.DisplayText}";
        }
        catch
        {
            target.Text = "예상 비용: 조회 불가 · 완료 후 실제 비용 표시";
        }
    }

    private static double GetImageMegapixels(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return 1.0;
        }

        try
        {
            using var stream = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return Math.Max(0.01, frame.PixelWidth * frame.PixelHeight / 1_000_000.0);
        }
        catch
        {
            return 1.0;
        }
    }

    private async void EditImageWithModelButton_Click(object sender, RoutedEventArgs e)
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

        var selectedModelId = RequestModelComboBox.SelectedItem is OpenRouterModelPreset selected
            ? selected.ModelId
            : _settingsService.Load().EffectiveModelId;

        await ExecuteEditAsync(
            _currentConversation.Id,
            _currentImagePath,
            prompt,
            selectedModelId,
            _editMode,
            _selectionPixelRect);
    }

    private IReadOnlyList<OpenRouterModelPreset> BuildRetryModelOptions(
        string originalModelId,
        out OpenRouterModelPreset selectedOption)
    {
        var settings = _settingsService.Load();
        var options = new List<OpenRouterModelPreset>(OpenRouterModelPreset.All);
        var customModelId = string.IsNullOrWhiteSpace(settings.CustomModelId)
            ? null
            : settings.CustomModelId.Trim();

        var selected = options.FirstOrDefault(option =>
            string.Equals(option.ModelId, originalModelId, StringComparison.OrdinalIgnoreCase));

        if (selected is null && !string.Equals(originalModelId, customModelId, StringComparison.OrdinalIgnoreCase))
        {
            selected = new OpenRouterModelPreset($"작업 모델 · {originalModelId}", originalModelId);
            options.Add(selected);
        }

        OpenRouterModelPreset? customOption = null;
        if (customModelId is not null)
        {
            customOption = new OpenRouterModelPreset($"Custom · {customModelId}", customModelId);
            options.Add(customOption);
        }

        if (string.Equals(originalModelId, customModelId, StringComparison.OrdinalIgnoreCase) && customOption is not null)
        {
            selected = customOption;
        }

        selectedOption = selected ?? options.First();
        return options;
    }

    private async void RetryModelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (sender is ComboBox { Tag: TextBlock costText, SelectedItem: OpenRouterModelPreset selected } selector &&
            selector.DataContext is EditRecord edit)
        {
            await UpdateCostTextAsync(costText, selected.ModelId, edit.InputImagePath);
        }
    }

    private async void RetryEditWithModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || sender is not Button { Tag: RetryEditContext context })
        {
            return;
        }

        var edit = context.Edit;
        if (!File.Exists(edit.InputImagePath))
        {
            MessageBox.Show(this, "이 작업에 사용된 입력 이미지를 찾을 수 없습니다.", "다시 시도", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = ParseEditMode(edit.EditMode);
        Int32Rect? selection = null;
        if (edit.SelectionX.HasValue && edit.SelectionY.HasValue &&
            edit.SelectionWidth.HasValue && edit.SelectionHeight.HasValue)
        {
            selection = new Int32Rect(
                (int)Math.Round(edit.SelectionX.Value),
                (int)Math.Round(edit.SelectionY.Value),
                (int)Math.Round(edit.SelectionWidth.Value),
                (int)Math.Round(edit.SelectionHeight.Value));
        }

        var modelId = context.ModelSelector.SelectedItem is OpenRouterModelPreset selected
            ? selected.ModelId
            : edit.ModelId;

        SelectHistoryImage(edit.InputImagePath, "재시도 입력 이미지");
        await ExecuteEditAsync(
            _currentConversation.Id,
            edit.InputImagePath,
            NormalizePrompt(edit.Prompt),
            modelId,
            mode,
            selection);
    }

    private void ChatHistoryPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            FindAncestor<ComboBox>(source) is not null ||
            FindAncestor<ComboBoxItem>(source) is not null)
        {
            return;
        }

        var textBlock = FindAncestor<TextBlock>(source);
        if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text) || FindAncestor<Button>(textBlock) is not null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(textBlock.Text);
            OperationStatusTextBlock.Text = "대화 텍스트를 클립보드에 복사했습니다.";
            e.Handled = true;
        }
        catch
        {
            OperationStatusTextBlock.Text = "클립보드에 복사하지 못했습니다.";
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                ContentElement contentElement =>
                    ContentOperations.GetParent(contentElement) ??
                    (contentElement as FrameworkContentElement)?.Parent,
                _ => VisualTreeHelper.GetParent(current)
            };
        }

        return null;
    }
}
