using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Navigation;
using DKImageAIEditor.Models;
using DKImageAIEditor.Services;
using Microsoft.Win32;

namespace DKImageAIEditor.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettingsService _settingsService = new();
    private readonly AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();

        PresetModelComboBox.ItemsSource = OpenRouterModelPreset.All;
        _settings = _settingsService.Load();

        var apiKey = CredentialStore.LoadApiKey();
        ApiKeyPasswordBox.Password = apiKey;
        ApiKeyTextBox.Text = apiKey;
        PresetModelComboBox.SelectedValue = _settings.SelectedModelId;
        CustomModelTextBox.Text = _settings.CustomModelId;
        CustomStoragePathTextBox.Text = _settings.CustomImageStoragePath;
        CurrentVersionTextBlock.Text = $"현재 버전: v{UpdateService.CurrentVersionText}";

        if (PresetModelComboBox.SelectedItem is null)
        {
            PresetModelComboBox.SelectedValue = OpenRouterModelPreset.DefaultModelId;
        }

        if (_settings.UseCustomModel)
        {
            CustomModelRadioButton.IsChecked = true;
        }
        else
        {
            PresetModelRadioButton.IsChecked = true;
        }

        switch (_settings.ImageStorageMode)
        {
            case ImageStorageMode.SourceFolder:
                SourceFolderStorageRadioButton.IsChecked = true;
                break;
            case ImageStorageMode.CustomFolder:
                CustomFolderStorageRadioButton.IsChecked = true;
                break;
            default:
                AppDataStorageRadioButton.IsChecked = true;
                break;
        }

        UpdateModelControls();
        UpdateStorageControls();
    }

    private void ToggleApiKeyVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApiKeyTextBox.Visibility == Visibility.Visible)
        {
            ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
            ToggleApiKeyVisibilityButton.ToolTip = "API Key 표시";
            ApiKeyPasswordBox.Focus();
            ApiKeyPasswordBox.SelectAll();
            return;
        }

        ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
        ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
        ApiKeyTextBox.Visibility = Visibility.Visible;
        ToggleApiKeyVisibilityButton.ToolTip = "API Key 숨김";
        ApiKeyTextBox.Focus();
        ApiKeyTextBox.SelectAll();
    }

    private void ModelMode_Checked(object sender, RoutedEventArgs e)
    {
        if (PresetModelComboBox is null || CustomModelTextBox is null)
        {
            return;
        }

        UpdateModelControls();
    }

    private void StorageMode_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomStoragePathTextBox is null || BrowseStorageFolderButton is null)
        {
            return;
        }

        UpdateStorageControls();
    }

    private void UpdateModelControls()
    {
        var useCustomModel = CustomModelRadioButton.IsChecked == true;
        PresetModelComboBox.IsEnabled = !useCustomModel;
        CustomModelTextBox.IsEnabled = useCustomModel;
    }

    private void UpdateStorageControls()
    {
        var useCustomFolder = CustomFolderStorageRadioButton.IsChecked == true;
        CustomStoragePathTextBox.IsEnabled = useCustomFolder;
        BrowseStorageFolderButton.IsEnabled = useCustomFolder;
    }

    private void BrowseStorageFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "이미지 저장 폴더 선택"
        };

        if (Directory.Exists(CustomStoragePathTextBox.Text))
        {
            dialog.InitialDirectory = CustomStoragePathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            CustomStoragePathTextBox.Text = dialog.FolderName;
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusTextBlock.Text = "최신 버전을 확인하고 있습니다.";

        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null)
            {
                UpdateStatusTextBlock.Text = "현재 최신 버전을 사용하고 있습니다.";
                return;
            }

            UpdateStatusTextBlock.Text = $"새 버전 {update.TagName}을 사용할 수 있습니다.";

            var result = MessageBox.Show(
                this,
                $"새 버전 {update.TagName}을 사용할 수 있습니다.\n\n지금 다운로드하고 업데이트한 뒤 프로그램을 다시 시작할까요?",
                "DK Image AI Editor 업데이트",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            UpdateStatusTextBlock.Text = "업데이트 패키지를 다운로드하고 있습니다.";
            var packagePath = await UpdateService.DownloadUpdateAsync(update);

            UpdateStatusTextBlock.Text = "업데이트를 적용하고 프로그램을 다시 시작합니다.";
            UpdateService.StartUpdate(packagePath);
        }
        catch (Exception exception)
        {
            UpdateStatusTextBlock.Text = "업데이트를 확인하거나 적용하지 못했습니다.";
            MessageBox.Show(
                this,
                $"업데이트 처리 중 오류가 발생했습니다.\n\n{exception.Message}",
                "업데이트 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (IsLoaded)
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var useCustomModel = CustomModelRadioButton.IsChecked == true;
        var customModelId = CustomModelTextBox.Text.Trim();

        if (useCustomModel && string.IsNullOrWhiteSpace(customModelId))
        {
            MessageBox.Show(this, "Custom 모델 ID를 입력하세요.", "설정", MessageBoxButton.OK, MessageBoxImage.Information);
            CustomModelTextBox.Focus();
            return;
        }

        var storageMode = CustomFolderStorageRadioButton.IsChecked == true
            ? ImageStorageMode.CustomFolder
            : SourceFolderStorageRadioButton.IsChecked == true
                ? ImageStorageMode.SourceFolder
                : ImageStorageMode.AppData;
        var customStoragePath = CustomStoragePathTextBox.Text.Trim();

        if (storageMode == ImageStorageMode.CustomFolder)
        {
            if (string.IsNullOrWhiteSpace(customStoragePath))
            {
                MessageBox.Show(this, "이미지를 저장할 폴더를 지정하세요.", "설정", MessageBoxButton.OK, MessageBoxImage.Information);
                CustomStoragePathTextBox.Focus();
                return;
            }

            try
            {
                Directory.CreateDirectory(customStoragePath);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, $"저장 폴더를 사용할 수 없습니다.\n\n{exception.Message}", "설정", MessageBoxButton.OK, MessageBoxImage.Warning);
                CustomStoragePathTextBox.Focus();
                return;
            }
        }

        var selectedPreset = PresetModelComboBox.SelectedItem as OpenRouterModelPreset;

        _settings.UseCustomModel = useCustomModel;
        _settings.SelectedModelId = selectedPreset?.ModelId ?? OpenRouterModelPreset.DefaultModelId;
        _settings.CustomModelId = customModelId;
        _settings.ImageStorageMode = storageMode;
        _settings.CustomImageStoragePath = customStoragePath;

        var apiKey = ApiKeyTextBox.Visibility == Visibility.Visible
            ? ApiKeyTextBox.Text
            : ApiKeyPasswordBox.Password;
        CredentialStore.SaveApiKey(apiKey.Trim());
        _settingsService.Save(_settings);

        DialogResult = true;
        Close();
    }

    private void ImageModelsLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
