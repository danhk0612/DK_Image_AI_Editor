using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DKImageAIEditor.Models;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private sealed record RetryEditContext(EditRecord Edit, ComboBox ModelSelector);

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRequestModelSelector();
        EnhanceRetryModelSelectors();
        ConversationList.SelectionChanged += ConversationList_RetryModelSelectorSelectionChanged;
    }

    private void SettingsButtonWithRefresh_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton_Click(sender, e);
        RefreshRequestModelSelector();
        EnhanceRetryModelSelectors();
    }

    private void ConversationList_RetryModelSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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

    private void EnhanceRetryModelSelectors()
    {
        var secondaryButtonStyle = Application.Current.TryFindResource("SecondaryButtonStyle") as Style;

        foreach (var retryButton in FindDescendants<Button>(ChatHistoryPanel)
                     .Where(button =>
                         string.Equals(button.Content?.ToString(), "다시 시도", StringComparison.Ordinal) &&
                         button.Tag is EditRecord)
                     .ToList())
        {
            if (retryButton.Tag is not EditRecord edit ||
                retryButton.Parent is not Panel currentRow ||
                currentRow.Parent is not Panel parentPanel)
            {
                continue;
            }

            var continueButton = currentRow.Children
                .OfType<Button>()
                .FirstOrDefault(button => string.Equals(
                    button.Content?.ToString(),
                    "이 이미지에서 계속",
                    StringComparison.Ordinal));

            if (continueButton is null)
            {
                continue;
            }

            if (secondaryButtonStyle is not null)
            {
                continueButton.Style = secondaryButtonStyle;
                retryButton.Style = secondaryButtonStyle;
            }

            var options = BuildRetryModelOptions(edit.ModelId, out var selectedOption);
            var selector = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = nameof(OpenRouterModelPreset.DisplayName),
                SelectedItem = selectedOption,
                MinHeight = 30,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "다시 시도에 사용할 모델"
            };

            var outerGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = currentRow.Margin
            };
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            currentRow.Children.Remove(continueButton);
            currentRow.Children.Remove(retryButton);

            continueButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            continueButton.Margin = new Thickness(0, 0, 0, 6);
            continueButton.MinWidth = 0;
            continueButton.Padding = new Thickness(8, 5, 8, 5);
            Grid.SetRow(continueButton, 0);
            outerGrid.Children.Add(continueButton);

            var retryRow = new Grid();
            retryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            retryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(selector, 0);
            retryRow.Children.Add(selector);

            retryButton.HorizontalAlignment = HorizontalAlignment.Right;
            retryButton.MinWidth = 76;
            retryButton.Margin = new Thickness(0);
            retryButton.Padding = new Thickness(8, 5, 8, 5);
            Grid.SetColumn(retryButton, 1);
            retryRow.Children.Add(retryButton);

            Grid.SetRow(retryRow, 1);
            outerGrid.Children.Add(retryRow);

            var rowIndex = parentPanel.Children.IndexOf(currentRow);
            parentPanel.Children.RemoveAt(rowIndex);
            parentPanel.Children.Insert(rowIndex, outerGrid);

            retryButton.Click -= RetryEditButton_Click;
            retryButton.Click += RetryEditWithModelButton_Click;
            retryButton.Tag = new RetryEditContext(edit, selector);
        }

        ApplyConversationDeleteButtonStyle(secondaryButtonStyle);
    }

    private void ApplyConversationDeleteButtonStyle(Style? secondaryButtonStyle)
    {
        if (secondaryButtonStyle is null)
        {
            return;
        }

        foreach (var item in ConversationList.Items.OfType<ListBoxItem>())
        {
            if (item.Content is not DependencyObject content)
            {
                continue;
            }

            foreach (var button in FindDescendants<Button>(content)
                         .Where(button => string.Equals(button.Content?.ToString(), "삭제", StringComparison.Ordinal)))
            {
                button.Style = secondaryButtonStyle;
            }
        }
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
