using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DKImageAIEditor.Models;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRequestModelSelector();
    }

    private void SettingsButtonWithRefresh_Click(object sender, RoutedEventArgs e)
    {
        SettingsButton_Click(sender, e);
        RefreshRequestModelSelector();
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

    private void ChatHistoryPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var textBlock = FindVisualAncestor<TextBlock>(source);
        if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return;
        }

        if (FindVisualAncestor<Button>(textBlock) is not null)
        {
            return;
        }

        Clipboard.SetText(textBlock.Text);
        OperationStatusTextBlock.Text = "대화 텍스트를 클립보드에 복사했습니다.";
        e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
