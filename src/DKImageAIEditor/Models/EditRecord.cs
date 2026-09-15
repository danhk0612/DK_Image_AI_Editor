namespace DKImageAIEditor.Models;

public sealed record EditRecord(
    string Id,
    string ConversationId,
    int Sequence,
    string Prompt,
    string ModelId,
    string EditMode,
    double? SelectionX,
    double? SelectionY,
    double? SelectionWidth,
    double? SelectionHeight,
    string InputImagePath,
    string OutputImagePath,
    DateTimeOffset CreatedAt);
