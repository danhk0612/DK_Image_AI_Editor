using System.Globalization;
using System.IO;
using DKImageAIEditor.Models;
using Microsoft.Data.Sqlite;

namespace DKImageAIEditor.Services;

public sealed class ConversationStore
{
    private readonly string _dataRoot;
    private readonly string _conversationsRoot;
    private readonly string _databasePath;

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
        var conversationDirectory = Path.Combine(_conversationsRoot, id);
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
        var conversations = new List<ConversationRecord>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, OriginalImagePath, CurrentImagePath, CreatedAt, UpdatedAt
            FROM Conversations
            ORDER BY UpdatedAt DESC;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            conversations.Add(ReadConversation(reader));
        }

        return conversations;
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
                   InputImagePath, OutputImagePath, CreatedAt
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
                ParseDateTimeOffset(reader.GetString(12))));
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
                     InputImagePath, OutputImagePath, CreatedAt)
                VALUES
                    ($id, $conversationId, $sequenceNumber, $prompt, $modelId, $editMode,
                     $selectionX, $selectionY, $selectionWidth, $selectionHeight,
                     $inputImagePath, $outputImagePath, $createdAt);
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
        var conversationDirectory = Path.Combine(_conversationsRoot, conversationId);
        Directory.CreateDirectory(conversationDirectory);
        return Path.Combine(conversationDirectory, $"{sequence:0000}{extension}");
    }

    public void DeleteConversation(string id)
    {
        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Conversations WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        var conversationDirectory = Path.Combine(_conversationsRoot, id);
        if (Directory.Exists(conversationDirectory))
        {
            Directory.Delete(conversationDirectory, true);
        }
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
                FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE,
                UNIQUE (ConversationId, SequenceNumber)
            );

            CREATE INDEX IF NOT EXISTS IX_Conversations_UpdatedAt
                ON Conversations(UpdatedAt DESC);

            CREATE INDEX IF NOT EXISTS IX_Edits_ConversationId_SequenceNumber
                ON Edits(ConversationId, SequenceNumber);
            """;
        command.ExecuteNonQuery();
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
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
