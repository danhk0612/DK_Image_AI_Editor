namespace DKImageAIEditor.Models;

public sealed class AppSettings
{
    public string SelectedModelId { get; set; } = OpenRouterModelPreset.DefaultModelId;
    public bool UseCustomModel { get; set; }
    public string CustomModelId { get; set; } = string.Empty;
    public ImageStorageMode ImageStorageMode { get; set; } = ImageStorageMode.AppData;
    public string CustomImageStoragePath { get; set; } = string.Empty;

    public string EffectiveModelId =>
        UseCustomModel && !string.IsNullOrWhiteSpace(CustomModelId)
            ? CustomModelId.Trim()
            : SelectedModelId;
}
