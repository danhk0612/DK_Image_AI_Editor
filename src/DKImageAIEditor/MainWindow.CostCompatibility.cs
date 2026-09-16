using System.Windows.Controls;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private Task UpdateCostTextAsync(TextBlock target, string modelId, string? imagePath) =>
        UpdateCostTextAsync(target, modelId, imagePath, string.Empty);
}
