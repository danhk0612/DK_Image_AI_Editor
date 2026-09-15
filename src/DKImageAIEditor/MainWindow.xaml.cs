using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DKImageAIEditor.Views;
using Microsoft.Win32;

namespace DKImageAIEditor;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    private string? _currentImagePath;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
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
        var settingsWindow = new SettingsWindow
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetSingleImagePath(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetSingleImagePath(e.Data, out var imagePath))
        {
            StartConversation(imagePath!);
        }
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
        _currentImagePath = imagePath;
        EditorImage.Source = LoadBitmap(imagePath);
        EditorImage.Visibility = Visibility.Visible;
        CanvasPlaceholder.Visibility = Visibility.Collapsed;

        ConversationList.Items.Clear();
        ConversationList.Items.Add(new ListBoxItem
        {
            IsSelected = true,
            Padding = new Thickness(10),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = Path.GetFileNameWithoutExtension(imagePath),
                        FontWeight = FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = "현재 작업",
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = System.Windows.Media.Brushes.Gray,
                        FontSize = 12
                    }
                }
            }
        });

        ChatHistoryPanel.Children.Clear();
        ChatHistoryPanel.Children.Add(new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 243, 246)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = $"원본 이미지: {Path.GetFileName(imagePath)}",
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private static BitmapImage LoadBitmap(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
