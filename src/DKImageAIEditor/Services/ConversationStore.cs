using System.Globalization;
using System.IO;
using System.Windows;
using DKImageAIEditor.Models;
using Microsoft.Data.Sqlite;

namespace DKImageAIEditor.Services;

public sealed class ConversationStore
{
    private readonly string _dataRoot;
    private readonly string _conversationsRoot;
    private readonly string _databasePath;
    private readonly AppSettingsService _settingsService = new();

    public ConversationStore()
    {
        _dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DKImageAIEditor");
        _conversationsRoot = Path.Combine(_dataRoot, "Conversations");
        _databasePath = Path.Combine(_dataRoot, "editor.db");

        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(_conversationsRoot);
        InitializeDatabase();
    }

    public ConversationRecord CreateConversation(string sourceImagePath)
    {
        var id = Guid.NewGuid().ToString("N");
        var settings = _settingsService.Load();
        var conversationDirectory = GetConversationDirectory(sourceImagePath, id, settings);
        Directory.CreateDirectory(conversationDirectory);

        var extension = Path.GetExtension(sourceImagePath).ToLowerInvariant();
        var originalImagePath = Path.Combine(conversationDirectory, $"original{extension}");
        File.Copy(sourceImagePath, originalImagePath, false);

        var now = DateTimeOffset.UtcNow;
        var conversation = new ConversationRecord(
            id,
            Path.GetFileNameWithoutExtension(sourceImagePath),
            originalImagePath,
            originalImagePath,
            now,
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Conversations
                (Id, Title, OriginalImagePath, CurrentImagePath, CreatedAt, UpdatedAt)
            VALUES
                ($id, $title, $originalImagePath, $currentImagePath, $createdAt, $updatedAt);
            """;
        command.Parameters.AddWithValue("$id", conversation.Id);
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$originalImagePath", conversation.OriginalImagePath);
        command.Parameters.AddWithValue("$currentImagePath", conversation.CurrentImagePath);
        command.Parameters.AddWithValue("$createdAt", conversation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", conversation.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();

        return conversation;
    }

    public IReadOnlyList<ConversationRecord> GetConversations()
    {
        var storedConversations = new List<ConversationRecord>();

        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id, Title, OriginalImagePath, CurrentImagePath, CreatedAt, UpdatedAt
                FROM Conversations
                ORDER BY UpdatedAt DESC;
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                storedConversations.Add(ReadConversation(reader));
            }
        }

        var availableConversations = new List<ConversationRecord>(storedConversations.Count);
        foreach (var conversation in storedConversations)
        {
            var currentImagePath = ResolveExistingImagePath(conversation);
            if (currentImagePath is null)
            {
                continue;
            }

            var originalImagePath = File.Exists(conversation.OriginalImagePath)
                ? conversation.OriginalImagePath
                : currentImagePath;

            availableConversations.Add(new ConversationRecord(
                conversation.Id,
                conversation.Title,
                originalImagePath,
                currentImagePath,
                conversation.CreatedAt,
                conversation.UpdatedAt));
        }

        return availableConversations;
    }

    public ConversationRecord? GetConversation(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, OriginalImagePath, CurrentImagePath, CreatedAt, UpdatedAt
            FROM Conversations
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadConversation(reader) : null;
    }

    public IReadOnlyList<EditRecord> GetEdits(string conversationId)
    {
        var edits = new List<EditRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ConversationId, SequenceNumber, Prompt, ModelId, EditMode,
                   SelectionX, SelectionY, SelectionWidth, SelectionHeight,
                   InputImagePath, OutputImagePath, CreatedAt, ActualCostUsd
            FROM Edits
            WHERE ConversationId = $conversationId
            ORDER BY SequenceNumber ASC;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            edits.Add(new EditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.GetString(10),
                reader.GetString(11),
                ParseDateTimeOffset(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetDouble(13)));
        }

        return edits;
    }

    public void AddEdit(EditRecord edit)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Edits
                    (Id, ConversationId, SequenceNumber, Prompt, ModelId, EditMode,
                     SelectionX, SelectionY, SelectionWidth, SelectionHeight,
                     InputImagePath, OutputImagePath, CreatedAt, ActualCostUsd)
                VALUES
                    ($id, $conversationId, $sequenceNumber, $prompt, $modelId, $editMode,
                     $selectionX, $selectionY, $selectionWidth, $selectionHeight,
                     $inputImagePath, $outputImagePath, $createdAt, $actualCostUsd);
                """;
            insert.Parameters.AddWithValue("$id", edit.Id);
            insert.Parameters.AddWithValue("$conversationId", edit.ConversationId);
            insert.Parameters.AddWithValue("$sequenceNumber", edit.Sequence);
            insert.Parameters.AddWithValue("$prompt", edit.Prompt);
            insert.Parameters.AddWithValue("$modelId", edit.ModelId);
            insert.Parameters.AddWithValue("$editMode", edit.EditMode);
            insert.Parameters.AddWithValue("$selectionX", (object?)edit.SelectionX ?? DBNull.Value);
            insert.Parameters.AddWithValue("$selectionY", (object?)edit.SelectionY ?? DBNull.Value);
            insert.Parameters.AddWithValue("$selectionWidth", (object?)edit.SelectionWidth ?? DBNull.Value);
            insert.Parameters.AddWithValue("$selectionHeight", (object?)edit.SelectionHeight ?? DBNull.Value);
            insert.Parameters.AddWithValue("$inputImagePath", edit.InputImagePath);
            insert.Parameters.AddWithValue("$outputImagePath", edit.OutputImagePath);
            insert.Parameters.AddWithValue("$createdAt", edit.CreatedAt.ToString("O"));
            insert.Parameters.AddWithValue("$actualCostUsd", (object?)edit.ActualCostUsd ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Conversations
                SET CurrentImagePath = $currentImagePath,
                    UpdatedAt = $updatedAt
                WHERE Id = $conversationId;
                """;
            update.Parameters.AddWithValue("$currentImagePath", edit.OutputImagePath);
            update.Parameters.AddWithValue("$updatedAt", edit.CreatedAt.ToString("O"));
            update.Parameters.AddWithValue("$conversationId", edit.ConversationId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public string GetVersionImagePath(string conversationId, int sequence, string extension = ".png")
    {
        var conversation = GetConversation(conversationId);
        var conversationDirectory = conversation is null
            ? Path.Combine(_conversationsRoot, conversationId)
            : Path.GetDirectoryName(conversation.OriginalImagePath) ?? Path.Combine(_conversationsRoot, conversationId);

        Directory.CreateDirectory(conversationDirectory);
        return Path.Combine(conversationDirectory, $"{sequence:0000}{extension}");
    }

    public void DeleteConversation(string id)
    {
        var conversation = GetConversation(id);
        var conversationDirectory = conversation is null
            ? Path.Combine(_conversationsRoot, id)
            : Path.GetDirectoryName(conversation.OriginalImagePath);

        var deleteImageFolder = false;
        if (!string.IsNullOrWhiteSpace(conversationDirectory) && Directory.Exists(conversationDirectory))
        {
            deleteImageFolder = MessageBox.Show(
                "이 대화의 이미지 저장 폴더도 함께 삭제할까요?\n\n아니오를 선택하면 대화 기록만 삭제되고 이미지 파일은 그대로 남습니다.",
                "이미지 폴더 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Conversations WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        if (deleteImageFolder &&
            !string.IsNullOrWhiteSpace(conversationDirectory) &&
            Directory.Exists(conversationDirectory))
        {
            try
            {
                Directory.Delete(conversationDirectory, true);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"대화 기록은 삭제했지만 이미지 폴더를 삭제하지 못했습니다.\n\n{exception.Message}",
                    "이미지 폴더 삭제 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private string? ResolveExistingImagePath(ConversationRecord conversation)
    {
        if (File.Exists(conversation.CurrentImagePath))
        {
            return conversation.CurrentImagePath;
        }

        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT OutputImagePath
                FROM Edits
                WHERE ConversationId = $conversationId
                ORDER BY SequenceNumber DESC;
                """;
            command.Parameters.AddWithValue("$conversationId", conversation.Id);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var outputImagePath = reader.GetString(0);
                if (File.Exists(outputImagePath))
                {
                    return outputImagePath;
                }
            }
        }

        return File.Exists(conversation.OriginalImagePath)
            ? conversation.OriginalImagePath
            : null;
    }

    private string GetConversationDirectory(string sourceImagePath, string id, AppSettings settings)
    {
        if (settings.ImageStorageMode == ImageStorageMode.AppData)
        {
            return Path.Combine(_conversationsRoot, id);
        }

        var root = settings.ImageStorageMode switch
        {
            ImageStorageMode.SourceFolder => Path.GetDirectoryName(sourceImagePath),
            ImageStorageMode.CustomFolder => settings.CustomImageStoragePath,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("이미지 저장 위치를 확인할 수 없습니다. 설정에서 저장 위치를 다시 지정하세요.");
        }

        Directory.CreateDirectory(root);
        var sourceName = SanitizeFolderName(Path.GetFileNameWithoutExtension(sourceImagePath));
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var folderName = $"{sourceName}_{timestamp}_{id[..8]}";
        return Path.Combine(root, folderName);
    }

    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "image" : sanitized;
    }

    private void InitializeDatabase()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Conversations (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                OriginalImagePath TEXT NOT NULL,
                CurrentImagePath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Edits (
                Id TEXT PRIMARY KEY,
                ConversationId TEXT NOT NULL,
                SequenceNumber INTEGER NOT NULL,
                Prompt TEXT NOT NULL,
                ModelId TEXT NOT NULL,
                EditMode TEXT NOT NULL,
                SelectionX REAL NULL,
                SelectionY REAL NULL,
                SelectionWidth REAL NULL,
                SelectionHeight REAL NULL,
                InputImagePath TEXT NOT NULL,
                OutputImagePath TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ActualCostUsd REAL NULL,
                FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE,
                UNIQUE (ConversationId, SequenceNumber)
            );

            CREATE INDEX IF NOT EXISTS IX_Conversations_UpdatedAt
                ON Conversations(UpdatedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_Edits_ConversationId_SequenceNumber
                ON Edits(ConversationId, SequenceNumber);
            """;
        command.ExecuteNonQuery();

        EnsureColumn(connection, "Edits", "ActualCostUsd", "REAL NULL");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string declaration)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declaration};";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Foreign Keys=True");
        connection.Open();
        return connection;
    }

    private static ConversationRecord ReadConversation(SqliteDataReader reader)
    {
        return new ConversationRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseDateTimeOffset(reader.GetString(4)),
            ParseDateTimeOffset(reader.GetString(5)));
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }
}
