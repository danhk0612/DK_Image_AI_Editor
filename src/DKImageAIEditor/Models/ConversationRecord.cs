namespace DKImageAIEditor.Models;

public sealed record ConversationRecord(
    string Id,
    string Title,
    string OriginalImagePath,
    string CurrentImagePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
