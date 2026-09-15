using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
    private ConversationRecord? _currentConversation;
    private string? _currentImagePath;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        ConversationList.SelectionChanged += ConversationList_SelectionChanged;
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

        SetOperationState(true, "요청 준비 중...");
        await Task.Yield();

        try
        {
            await UpdateOperationStatusAsync("원본 이미지를 읽는 중...");
            var sourceBytes = await File.ReadAllBytesAsync(sourceImagePath);

            await UpdateOperationStatusAsync($"OpenRouter 응답 대기 중... ({modelId})");
            var result = await _openRouterImageService.EditImageAsync(
                apiKey,
                modelId,
                sourceBytes,
                GetImageMediaType(sourceImagePath),
                prompt);

            await UpdateOperationStatusAsync("결과 이미지를 저장하는 중...");
            var outputPath = _conversationStore.GetVersionImagePath(
                conversationId,
                sequence,
                GetImageExtension(result.MediaType));
            await File.WriteAllBytesAsync(outputPath, result.ImageBytes);

            await UpdateOperationStatusAsync("편집 기록을 저장하는 중...");
            var createdAt = DateTimeOffset.UtcNow;
            _conversationStore.AddEdit(new EditRecord(
                Guid.NewGuid().ToString("N"),
                conversationId,
                sequence,
                prompt,
                modelId,
                "전체 편집",
                null,
                null,
                null,
                null,
                sourceImagePath,
                outputPath,
                createdAt));

            await UpdateOperationStatusAsync("화면을 갱신하는 중...");
            PromptTextBox.Clear();

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
    }

    private async Task UpdateOperationStatusAsync(string statusMessage)
    {
        OperationStatusTextBlock.Text = statusMessage;
        await Task.Yield();
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
        var thumbnail = new Image
        {
            Width = 54,
            Height = 54,
            Stretch = Stretch.UniformToFill,
            Source = LoadBitmap(conversation.OriginalImagePath, 96),
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
            Text = conversation.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = Brushes.Gray,
            FontSize = 11
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
        EditorImage.Source = null;
        EditorImage.Visibility = Visibility.Collapsed;
        CanvasPlaceholder.Visibility = Visibility.Visible;

        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(CreateHistoryTextCard(
            "이미지를 선택하면 편집 대화가 시작됩니다.",
            Color.FromRgb(241, 243, 246)));
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
