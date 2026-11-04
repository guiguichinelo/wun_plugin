using Microsoft.Data.Sqlite;

namespace Wun.Core;

/// <summary>
/// Local plugin persistence. The Jellyfin adapter must enforce user visibility and administrator
/// policy before calling this class; this store is not an authorization boundary.
/// </summary>
public sealed class RequestStore
{
    private readonly string connectionString;

    public RequestStore(string path)
    {
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        connectionString = new SqliteConnectionStringBuilder
            { DataSource = absolute, DefaultTimeout = 10 }.ToString();
        using var connection = Open();
        using var version = Command(connection, "PRAGMA user_version");
        var schema = Convert.ToInt32(version.ExecuteScalar());
        if (schema > 1) throw new InvalidOperationException("Unsupported WUN database schema.");
        using var transaction = connection.BeginTransaction();
        using var migration = Command(connection, """
            CREATE TABLE IF NOT EXISTS requests (
                id INTEGER PRIMARY KEY, identity TEXT UNIQUE NOT NULL,
                kind INTEGER NOT NULL, tmdb TEXT NOT NULL, title TEXT NOT NULL,
                season INTEGER, episode INTEGER, decision INTEGER NOT NULL DEFAULT 0,
                availability INTEGER NOT NULL DEFAULT 0, monitor INTEGER NOT NULL,
                revision INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS followers (
                request INTEGER NOT NULL REFERENCES requests(id), user TEXT NOT NULL,
                PRIMARY KEY(request,user));
            CREATE TABLE IF NOT EXISTS history (
                id INTEGER PRIMARY KEY, request INTEGER NOT NULL REFERENCES requests(id),
                actor TEXT NOT NULL, decision INTEGER NOT NULL, message TEXT NOT NULL,
                internal_note TEXT NOT NULL, review_at TEXT, created TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS notifications (
                id INTEGER PRIMARY KEY, user TEXT NOT NULL,
                request INTEGER NOT NULL REFERENCES requests(id), event TEXT NOT NULL,
                seen INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS notifications_user ON notifications(user,id);
            PRAGMA user_version=1;
            """);
        migration.ExecuteNonQuery();
        transaction.Commit();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var pragmas = Command(connection, "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL;");
        pragmas.ExecuteNonQuery();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params object?[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue("$" + index, values[index] ?? DBNull.Value);
        return command;
    }

    private static void Execute(SqliteConnection connection, string sql, params object?[] values)
    {
        using var command = Command(connection, sql, values);
        command.ExecuteNonQuery();
    }

    private const string Columns = "id,kind,tmdb,title,season,episode,decision,availability,monitor,revision";

    private static RequestCard ReadCard(SqliteDataReader row) => new(row.GetInt64(0),
        new((MediaKind)row.GetInt32(1), row.GetString(2), row.GetString(3),
            row.IsDBNull(4) ? null : row.GetInt32(4), row.IsDBNull(5) ? null : row.GetInt32(5)),
        (Decision)row.GetInt32(6), (Availability)row.GetInt32(7), row.GetBoolean(8), row.GetInt64(9));

    private static void ValidateUser(Guid user)
    {
        if (user == Guid.Empty) throw new ArgumentException("Missing authenticated user.");
    }

    public RequestCard Get(long id)
    {
        using var connection = Open();
        using var command = Command(connection, $"SELECT {Columns} FROM requests WHERE id=$0", id);
        using var row = command.ExecuteReader();
        return row.Read() ? ReadCard(row) : throw new KeyNotFoundException();
    }

    public RequestCard Create(Guid actor, Selection selection, IReadOnlyCollection<Guid> administrators)
    {
        ValidateUser(actor);
        selection.Validate();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var insert = Command(connection, """
            INSERT OR IGNORE INTO requests(identity,kind,tmdb,title,season,episode,monitor)
            VALUES($0,$1,$2,$3,$4,$5,$6)
            """, selection.Identity, (int)selection.Kind, selection.TmdbId, selection.Title,
            selection.Season, selection.Episode, selection.Kind == MediaKind.Series && selection.Season is null);
        var created = insert.ExecuteNonQuery() == 1;
        using var lookup = Command(connection, "SELECT id FROM requests WHERE identity=$0", selection.Identity);
        var id = (long)lookup.ExecuteScalar()!;
        Execute(connection, "INSERT OR IGNORE INTO followers VALUES($0,$1)", id, actor.ToString());
        if (created)
            foreach (var administrator in administrators.Where(x => x != Guid.Empty).Distinct())
                Execute(connection, "INSERT INTO notifications(user,request,event) VALUES($0,$1,'request')",
                    administrator.ToString(), id);
        transaction.Commit();
        return Get(id);
    }

    public Page<RequestCard> List(int start = 0, int limit = 30, Guid? follower = null, Decision? decision = null)
    {
        if (start < 0 || limit is < 1 or > 100 || (decision.HasValue && !Enum.IsDefined(decision.Value)))
            throw new ArgumentException("Invalid pagination or filter.");
        if (follower.HasValue) ValidateUser(follower.Value);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        const string filter = " WHERE ($0 IS NULL OR decision=$0) AND ($1 IS NULL OR EXISTS(SELECT 1 FROM followers f WHERE f.request=requests.id AND f.user=$1))";
        using var count = Command(connection, "SELECT COUNT(*) FROM requests" + filter,
            decision.HasValue ? (int)decision.Value : null, follower?.ToString());
        var total = (long)count.ExecuteScalar()!;
        using var command = Command(connection, $"SELECT {Columns} FROM requests" + filter + " ORDER BY id DESC LIMIT $2 OFFSET $3",
            decision.HasValue ? (int)decision.Value : null, follower?.ToString(), limit, start);
        using var row = command.ExecuteReader();
        var items = new List<RequestCard>();
        while (row.Read()) items.Add(ReadCard(row));
        return new(items.ToArray(), total, start, limit);
    }

    public void Follow(long id, Guid user, bool follow)
    {
        ValidateUser(user);
        Get(id);
        using var connection = Open();
        Execute(connection, follow ? "INSERT OR IGNORE INTO followers VALUES($0,$1)"
            : "DELETE FROM followers WHERE request=$0 AND user=$1", id, user.ToString());
    }

    public void Decide(long id, long revision, Guid actor, Decision decision, string message,
        string internalNote, DateTimeOffset? reviewAt = null, bool? monitor = null)
    {
        ValidateUser(actor);
        if (!Enum.IsDefined(decision) || message is null || internalNote is null
            || message.Length > 2000 || internalNote.Length > 4000)
            throw new ArgumentException("Invalid administrative decision.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var update = Command(connection, """
            UPDATE requests SET decision=$0,monitor=COALESCE($1,monitor),revision=revision+1
            WHERE id=$2 AND revision=$3
            """, (int)decision, monitor, id, revision);
        if (update.ExecuteNonQuery() != 1) throw new RevisionConflictException();
        Execute(connection, """
            INSERT INTO history(request,actor,decision,message,internal_note,review_at,created)
            VALUES($0,$1,$2,$3,$4,$5,$6)
            """, id, actor.ToString(), (int)decision, message, internalNote,
            reviewAt?.ToUniversalTime().ToString("O"), DateTimeOffset.UtcNow.ToString("O"));
        Execute(connection, """
            INSERT INTO notifications(user,request,event)
            SELECT user,request,$0 FROM followers WHERE request=$1
            """, decision.ToString(), id);
        transaction.Commit();
    }

    public HistoryEntry[] History(long id) => AdminHistory(id).Select(x => x.Public).ToArray();

    public AdminHistoryEntry[] AdminHistory(long id)
    {
        using var connection = Open();
        using var command = Command(connection,
            "SELECT actor,decision,message,internal_note,review_at,created FROM history WHERE request=$0 ORDER BY id", id);
        using var row = command.ExecuteReader();
        var history = new List<AdminHistoryEntry>();
        while (row.Read()) history.Add(new(new((Decision)row.GetInt32(1), row.GetString(2),
            DateTimeOffset.Parse(row.GetString(5))), Guid.Parse(row.GetString(0)), row.GetString(3),
            row.IsDBNull(4) ? null : DateTimeOffset.Parse(row.GetString(4))));
        return history.ToArray();
    }

    public Notification[] Notifications(Guid user)
    {
        ValidateUser(user);
        using var connection = Open();
        using var command = Command(connection,
            "SELECT id,request,event,seen FROM notifications WHERE user=$0 ORDER BY id DESC LIMIT 100", user.ToString());
        using var row = command.ExecuteReader();
        var notices = new List<Notification>();
        while (row.Read()) notices.Add(new(row.GetInt64(0), row.GetInt64(1), row.GetString(2), row.GetBoolean(3)));
        return notices.ToArray();
    }

    public void MarkNotificationsRead(Guid user)
    {
        ValidateUser(user);
        using var connection = Open();
        Execute(connection, "UPDATE notifications SET seen=1 WHERE user=$0", user.ToString());
    }
}
