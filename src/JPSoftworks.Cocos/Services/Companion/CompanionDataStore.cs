using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace JPSoftworks.Cocos.Services.Companion;

internal enum CompanionScopeType
{
    Window = 0,
    App = 1,
    Unattached = 2
}

internal sealed record CompanionScope(CompanionScopeType ScopeType, string ScopeKey, string AppName, string WindowTitle);

internal sealed record CompanionSession(
    Guid Id,
    CompanionScopeType ScopeType,
    string ScopeKey,
    string AppName,
    string WindowTitle,
    string Emoji,
    string AccentColor,
    bool IsSaved,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt);

internal enum ChatMessageRole
{
    User,
    Assistant,
    System
}

internal sealed record CompanionNote(
    long Id,
    Guid SessionId,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool IsPinned,
    bool IsFavorite,
    bool IsFlagged);

internal sealed record CompanionChatMessage(
    Guid SessionId,
    ChatMessageRole Role,
    string Content,
    DateTimeOffset CreatedAt);

internal interface ICompanionDataStore
{
    CompanionSession GetOrCreateSession(CompanionScope scope);

    CompanionSession? GetLatestSession();

    CompanionSession UpdateSessionAppearance(Guid sessionId, string emoji, string accentColor);

    CompanionSession UpdateSessionSaved(Guid sessionId, bool isSaved);

    void AddChatMessage(Guid sessionId, ChatMessageRole role, string content);

    IReadOnlyList<CompanionChatMessage> GetChatMessages(Guid sessionId);

    void DeleteChatMessages(Guid sessionId);

    IReadOnlyList<CompanionNote> GetNotes(Guid sessionId);

    CompanionNote AddNote(Guid sessionId, string content);

    void UpdateNoteFlags(long noteId, bool isPinned, bool isFavorite, bool isFlagged);

    void DeleteNote(long noteId);
}

internal sealed class SqliteCompanionDataStore : ICompanionDataStore
{
    private const string TimestampFormat = "O";
    private readonly string _connectionString;
    private readonly ILogger<SqliteCompanionDataStore> _logger;

    public SqliteCompanionDataStore(ILogger<SqliteCompanionDataStore> logger)
    {
        this._logger = logger;
        var directory = App.GetAppDataDirectory();
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "companion.db");
        this._connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        this.InitializeSchema();
    }

    public CompanionSession GetOrCreateSession(CompanionScope scope)
    {
        using var connection = this.OpenConnection();
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, scope_type, scope_key, app_name, window_title, emoji, accent_color, is_saved, created_at, last_active_at
            FROM companion_sessions
            WHERE scope_type = $scopeType AND scope_key = $scopeKey
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$scopeType", (int)scope.ScopeType);
        select.Parameters.AddWithValue("$scopeKey", scope.ScopeKey);

        CompanionSession? existing = null;
        using (var reader = select.ExecuteReader())
        {
            if (reader.Read())
            {
                existing = ReadSession(reader);
            }
        }

        if (existing is not null)
        {
            UpdateSessionLastActive(connection, existing.Id);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var created = now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var newSession = new CompanionSession(
            Guid.NewGuid(),
            scope.ScopeType,
            scope.ScopeKey,
            scope.AppName,
            scope.WindowTitle,
            string.Empty,
            string.Empty,
            false,
            now,
            now);

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO companion_sessions
                (id, scope_type, scope_key, app_name, window_title, emoji, accent_color, is_saved, created_at, last_active_at)
            VALUES
                ($id, $scopeType, $scopeKey, $appName, $windowTitle, $emoji, $accentColor, $isSaved, $createdAt, $lastActiveAt);
            """;
        insert.Parameters.AddWithValue("$id", newSession.Id.ToString());
        insert.Parameters.AddWithValue("$scopeType", (int)newSession.ScopeType);
        insert.Parameters.AddWithValue("$scopeKey", newSession.ScopeKey);
        insert.Parameters.AddWithValue("$appName", newSession.AppName);
        insert.Parameters.AddWithValue("$windowTitle", newSession.WindowTitle);
        insert.Parameters.AddWithValue("$emoji", newSession.Emoji);
        insert.Parameters.AddWithValue("$accentColor", newSession.AccentColor);
        insert.Parameters.AddWithValue("$isSaved", newSession.IsSaved ? 1 : 0);
        insert.Parameters.AddWithValue("$createdAt", created);
        insert.Parameters.AddWithValue("$lastActiveAt", created);
        insert.ExecuteNonQuery();

        return newSession;
    }

    public CompanionSession? GetLatestSession()
    {
        using var connection = this.OpenConnection();
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, scope_type, scope_key, app_name, window_title, emoji, accent_color, is_saved, created_at, last_active_at
            FROM companion_sessions
            ORDER BY last_active_at DESC
            LIMIT 1;
            """;
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadSession(reader);
    }

    public CompanionSession UpdateSessionAppearance(Guid sessionId, string emoji, string accentColor)
    {
        using var connection = this.OpenConnection();
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE companion_sessions
            SET emoji = $emoji,
                accent_color = $accentColor,
                last_active_at = $lastActiveAt
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$emoji", emoji);
        update.Parameters.AddWithValue("$accentColor", accentColor);
        update.Parameters.AddWithValue("$lastActiveAt", DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$id", sessionId.ToString());
        update.ExecuteNonQuery();

        var session = GetSessionById(connection, sessionId);
        if (session is null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found.");
        }

        return session;
    }

    public CompanionSession UpdateSessionSaved(Guid sessionId, bool isSaved)
    {
        using var connection = this.OpenConnection();
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE companion_sessions
            SET is_saved = $isSaved,
                last_active_at = $lastActiveAt
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$isSaved", isSaved ? 1 : 0);
        update.Parameters.AddWithValue("$lastActiveAt", DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$id", sessionId.ToString());
        update.ExecuteNonQuery();

        var session = GetSessionById(connection, sessionId);
        if (session is null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found.");
        }

        return session;
    }

    public void AddChatMessage(Guid sessionId, ChatMessageRole role, string content)
    {
        using var connection = this.OpenConnection();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO chat_messages
                (session_id, role, content, created_at)
            VALUES
                ($sessionId, $role, $content, $createdAt);
            """;
        insert.Parameters.AddWithValue("$sessionId", sessionId.ToString());
        insert.Parameters.AddWithValue("$role", role.ToString());
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();

        UpdateSessionLastActive(connection, sessionId);
    }

    public IReadOnlyList<CompanionChatMessage> GetChatMessages(Guid sessionId)
    {
        using var connection = this.OpenConnection();
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT session_id, role, content, created_at
            FROM chat_messages
            WHERE session_id = $sessionId
            ORDER BY created_at ASC;
            """;
        select.Parameters.AddWithValue("$sessionId", sessionId.ToString());

        using var reader = select.ExecuteReader();
        var results = new List<CompanionChatMessage>();
        while (reader.Read())
        {
            results.Add(ReadChatMessage(reader));
        }

        return results;
    }

    public void DeleteChatMessages(Guid sessionId)
    {
        using var connection = this.OpenConnection();
        using var delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM chat_messages
            WHERE session_id = $sessionId;
            """;
        delete.Parameters.AddWithValue("$sessionId", sessionId.ToString());
        delete.ExecuteNonQuery();

        UpdateSessionLastActive(connection, sessionId);
    }

    public IReadOnlyList<CompanionNote> GetNotes(Guid sessionId)
    {
        using var connection = this.OpenConnection();
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, session_id, content, created_at, updated_at, is_pinned, is_favorite, is_flagged
            FROM companion_notes
            WHERE session_id = $sessionId
            ORDER BY is_pinned DESC, is_favorite DESC, is_flagged DESC, created_at DESC;
            """;
        select.Parameters.AddWithValue("$sessionId", sessionId.ToString());

        using var reader = select.ExecuteReader();
        var results = new List<CompanionNote>();
        while (reader.Read())
        {
            results.Add(ReadNote(reader));
        }

        return results;
    }

    public CompanionNote AddNote(Guid sessionId, string content)
    {
        using var connection = this.OpenConnection();
        var now = DateTimeOffset.UtcNow;
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO companion_notes
                (session_id, content, created_at, updated_at, is_pinned, is_favorite, is_flagged)
            VALUES
                ($sessionId, $content, $createdAt, $updatedAt, 0, 0, 0);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$sessionId", sessionId.ToString());
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$createdAt", now.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$updatedAt", now.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        var id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);

        UpdateSessionLastActive(connection, sessionId);

        return new CompanionNote(id, sessionId, content, now, now, false, false, false);
    }

    public void UpdateNoteFlags(long noteId, bool isPinned, bool isFavorite, bool isFlagged)
    {
        using var connection = this.OpenConnection();
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE companion_notes
            SET is_pinned = $pinned,
                is_favorite = $favorite,
                is_flagged = $flagged,
                updated_at = $updatedAt
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$pinned", isPinned ? 1 : 0);
        update.Parameters.AddWithValue("$favorite", isFavorite ? 1 : 0);
        update.Parameters.AddWithValue("$flagged", isFlagged ? 1 : 0);
        update.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$id", noteId);
        update.ExecuteNonQuery();
    }

    public void DeleteNote(long noteId)
    {
        using var connection = this.OpenConnection();
        using var delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM companion_notes
            WHERE id = $id;
            """;
        delete.Parameters.AddWithValue("$id", noteId);
        delete.ExecuteNonQuery();
    }

    private void InitializeSchema()
    {
        using var connection = this.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS companion_sessions (
                id TEXT PRIMARY KEY,
                scope_type INTEGER NOT NULL,
                scope_key TEXT NOT NULL,
                app_name TEXT NOT NULL,
                window_title TEXT NOT NULL,
                emoji TEXT NOT NULL DEFAULT '',
                accent_color TEXT NOT NULL DEFAULT '',
                is_saved INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                last_active_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_companion_sessions_scope
                ON companion_sessions(scope_type, scope_key);

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(session_id) REFERENCES companion_sessions(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_chat_messages_session
                ON chat_messages(session_id, created_at);

            CREATE TABLE IF NOT EXISTS companion_notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                is_pinned INTEGER NOT NULL DEFAULT 0,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                is_flagged INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(session_id) REFERENCES companion_sessions(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_companion_notes_session
                ON companion_notes(session_id, created_at);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "companion_sessions", "emoji", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "companion_sessions", "accent_color", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "companion_sessions", "is_saved", "INTEGER NOT NULL DEFAULT 0");
        this._logger.LogDebug("Companion database initialized.");
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(this._connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static CompanionSession ReadSession(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        var scopeType = (CompanionScopeType)reader.GetInt32(1);
        var scopeKey = reader.GetString(2);
        var appName = reader.GetString(3);
        var windowTitle = reader.GetString(4);
        var emoji = reader.GetString(5);
        var accentColor = reader.GetString(6);
        var isSaved = reader.GetInt32(7) == 1;
        var createdAt = DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var lastActiveAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new CompanionSession(id, scopeType, scopeKey, appName, windowTitle, emoji, accentColor, isSaved, createdAt, lastActiveAt);
    }

    private static CompanionChatMessage ReadChatMessage(SqliteDataReader reader)
    {
        var sessionId = Guid.Parse(reader.GetString(0));
        var roleText = reader.GetString(1);
        var content = reader.GetString(2);
        var createdAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var role = Enum.TryParse<ChatMessageRole>(roleText, true, out var parsed)
            ? parsed
            : ChatMessageRole.System;
        return new CompanionChatMessage(sessionId, role, content, createdAt);
    }

    private static CompanionNote ReadNote(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = Guid.Parse(reader.GetString(1));
        var content = reader.GetString(2);
        var createdAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var updatedAt = DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var pinned = reader.GetInt32(5) == 1;
        var favorite = reader.GetInt32(6) == 1;
        var flagged = reader.GetInt32(7) == 1;
        return new CompanionNote(id, sessionId, content, createdAt, updatedAt, pinned, favorite, flagged);
    }

    private static void UpdateSessionLastActive(SqliteConnection connection, Guid sessionId)
    {
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE companion_sessions
            SET last_active_at = $lastActiveAt
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$lastActiveAt", DateTimeOffset.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$id", sessionId.ToString());
        update.ExecuteNonQuery();
    }

    private static CompanionSession? GetSessionById(SqliteConnection connection, Guid sessionId)
    {
        using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, scope_type, scope_key, app_name, window_title, emoji, accent_color, is_saved, created_at, last_active_at
            FROM companion_sessions
            WHERE id = $id
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("$id", sessionId.ToString());
        using var reader = select.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return ReadSession(reader);
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }
}
