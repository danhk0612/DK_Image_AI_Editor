using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using DKImageAIEditor.Models;
using DKImageAIEditor.Services;

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

        ApiKeyPasswordBox.Password = CredentialStore.LoadApiKey();
        PresetModelComboBox.SelectedValue = _settings.SelectedModelId;
        CustomModelTextBox.Text = _settings.CustomModelId;

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

        UpdateModelControls();
    }

    private void ModelMode_Checked(object sender, RoutedEventArgs e)
    {
        if (PresetModelComboBox is null || CustomModelTextBox is null)
        {
            return;
        }

        UpdateModelControls();
    }

    private void UpdateModelControls()
    {
        var useCustomModel = CustomModelRadioButton.IsChecked == true;
        PresetModelComboBox.IsEnabled = !useCustomModel;
        CustomModelTextBox.IsEnabled = useCustomModel;
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

        var selectedPreset = PresetModelComboBox.SelectedItem as OpenRouterModelPreset;

        _settings.UseCustomModel = useCustomModel;
        _settings.SelectedModelId = selectedPreset?.ModelId ?? OpenRouterModelPreset.DefaultModelId;
        _settings.CustomModelId = customModelId;

        CredentialStore.SaveApiKey(ApiKeyPasswordBox.Password.Trim());
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
