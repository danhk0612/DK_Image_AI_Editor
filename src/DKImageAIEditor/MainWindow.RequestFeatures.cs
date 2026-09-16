using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DKImageAIEditor.Models;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private bool _retryModelSelectorHooked;

    private sealed record RetryEditContext(EditRecord Edit, ComboBox ModelSelector);

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRequestModelSelector();
        EnhanceRetryModelSelectors();

        if (!_retryModelSelectorHooked)
        {
            ChatHistoryPanel.LayoutUpdated += ChatHistoryPanel_LayoutUpdated;
            _retryModelSelectorHooked = true;
        }
    }

    private void SettingsButtonWithRefresh_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton_Click(sender, e);
        RefreshRequestModelSelector();
        EnhanceRetryModelSelectors();
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
            return;
        }

        RequestModelComboBox.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.ModelId, settings.SelectedModelId, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault();
    }

    private async void EditImageWithModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null || string.IsNullOrWhiteSpace(_currentImagePath))
        {
            if (!_isBusy)
            {
                MessageBox.Show(
                    this,
                    "먼저 편집할 이미지 대화를 선택하세요.",
                    "이미지 편집",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
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

        var prompt = NormalizePrompt(PromptTextBox.Text);
        if (prompt.Length == 0)
        {
            MessageBox.Show(
                this,
                "수정 요청을 입력하세요.",
                "이미지 편집",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    private void ChatHistoryPanel_LayoutUpdated(object? sender, EventArgs e)
    {
        EnhanceRetryModelSelectors();
    }

    private void EnhanceRetryModelSelectors()
    {
        foreach (var retryButton in FindDescendants<Button>(ChatHistoryPanel)
                     .Where(button =>
                         string.Equals(button.Content?.ToString(), "다시 시도", StringComparison.Ordinal) &&
                         button.Tag is EditRecord)
                     .ToList())
        {
            if (retryButton.Tag is not EditRecord edit || retryButton.Parent is not Panel currentRow)
            {
                continue;
            }

            var buttonRow = EnsureResponsiveHistoryActionRow(currentRow);
            foreach (var continueButton in buttonRow.Children
                         .OfType<Button>()
                         .Where(button => string.Equals(
                             button.Content?.ToString(),
                             "이 이미지에서 계속",
                             StringComparison.Ordinal)))
            {
                continueButton.MinWidth = 108;
            }

            var options = BuildRetryModelOptions(edit.ModelId, out var selectedOption);
            var selector = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = nameof(OpenRouterModelPreset.DisplayName),
                SelectedItem = selectedOption,
                Width = 160,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = "다시 시도에 사용할 모델"
            };

            var buttonIndex = buttonRow.Children.IndexOf(retryButton);
            buttonRow.Children.Insert(Math.Max(0, buttonIndex), selector);

            retryButton.Click -= RetryEditButton_Click;
            retryButton.Click += RetryEditWithModelButton_Click;
            retryButton.Tag = new RetryEditContext(edit, selector);
        }
    }

    private static Panel EnsureResponsiveHistoryActionRow(Panel currentRow)
    {
        if (currentRow is WrapPanel)
        {
            return currentRow;
        }

        if (currentRow is not StackPanel stackPanel || stackPanel.Parent is not Panel parentPanel)
        {
            return currentRow;
        }

        var rowIndex = parentPanel.Children.IndexOf(stackPanel);
        if (rowIndex < 0)
        {
            return currentRow;
        }

        var wrapPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = stackPanel.HorizontalAlignment,
            Margin = stackPanel.Margin
        };

        var children = stackPanel.Children.Cast<UIElement>().ToList();
        stackPanel.Children.Clear();
        foreach (var child in children)
        {
            wrapPanel.Children.Add(child);
        }

        parentPanel.Children.RemoveAt(rowIndex);
        parentPanel.Children.Insert(rowIndex, wrapPanel);
        return wrapPanel;
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

        if (selected is null &&
            !string.Equals(originalModelId, customModelId, StringComparison.OrdinalIgnoreCase))
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

        if (string.Equals(originalModelId, customModelId, StringComparison.OrdinalIgnoreCase) &&
            customOption is not null)
        {
            selected = customOption;
        }

        selectedOption = selected ?? options.First();
        return options;
    }

    private async void RetryEditWithModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _currentConversation is null ||
            sender is not Button { Tag: RetryEditContext context })
        {
            return;
        }

        var edit = context.Edit;
        if (!File.Exists(edit.InputImagePath))
        {
            MessageBox.Show(
                this,
                "이 작업에 사용된 입력 이미지를 찾을 수 없습니다.",
                "다시 시도",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var textBlock = FindAncestor<TextBlock>(source);
        if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return;
        }

        if (FindAncestor<Button>(textBlock) is not null)
        {
            return;
        }

        Clipboard.SetText(textBlock.Text);
        OperationStatusTextBlock.Text = "대화 텍스트를 클립보드에 복사했습니다.";
        e.Handled = true;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
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
