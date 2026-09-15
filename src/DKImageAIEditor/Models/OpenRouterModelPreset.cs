namespace DKImageAIEditor.Models;

public sealed record OpenRouterModelPreset(string DisplayName, string ModelId)
{
    public const string ImageModelsCollectionUrl = "https://openrouter.ai/collections/image-models";
    public const string DefaultModelId = "google/gemini-3.1-flash-image";

    public static IReadOnlyList<OpenRouterModelPreset> All { get; } =
    [
        new("Nano Banana 2 (Gemini 3.1 Flash Image)", "google/gemini-3.1-flash-image"),
        new("Grok Imagine Image 2.0", "x-ai/grok-imagine-image-2.0"),
        new("Grok Imagine Image Quality", "x-ai/grok-imagine-image-quality"),
        new("Seedream 4.5", "bytedance-seed/seedream-4.5"),
        new("Nano Banana 2 Lite (Gemini 3.1 Flash Lite Image)", "google/gemini-3.1-flash-lite-image"),
        new("GPT Image 2", "openai/gpt-image-2"),
        new("Nano Banana Pro (Gemini 3 Pro Image)", "google/gemini-3-pro-image"),
        new("FLUX.2 Pro", "black-forest-labs/flux.2-pro")
    ];
}
