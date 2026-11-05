using Microsoft.Data.Sqlite;

namespace Wun.Core;

public sealed record Participation(int SupportCount, bool Supporting, bool Following, bool OwnRequest,
    DateTimeOffset? Created);
public sealed record AdministrativeRequest(Guid? Requester, Guid[] Supporters, DateTimeOffset? Created,
    AdminHistoryEntry? LastDecision);
public sealed record WorkflowNotification(long Id, long RequestId, string Event, bool Read,
    DateTimeOffset? Created, Guid? Actor, string Message);

public sealed partial class RequestStore
{
    public RequestCard? FindSelection(Selection selection)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT id FROM requests WHERE identity=$0", selection.Identity);
        var value = command.ExecuteScalar();
        return value is long id ? Get(id) : null;
    }
    private static void UpgradeWorkflow(SqliteConnection connection, int previousVersion)
    {
        bool HasColumn(string table, string column)
        {
            using var command = Command(connection, "PRAGMA table_info(" + table + ")");
            using var rows = command.ExecuteReader();
            while (rows.Read()) if (rows.GetString(1) == column) return true;
            return false;
        }
        if (!HasColumn("requests", "requested_by")) Execute(connection, "ALTER TABLE requests ADD COLUMN requested_by TEXT");
        if (!HasColumn("requests", "created")) Execute(connection, "ALTER TABLE requests ADD COLUMN created TEXT");
        foreach (var name in new[] { "actor", "created", "message" })
            if (!HasColumn("notifications", name)) Execute(connection, "ALTER TABLE notifications ADD COLUMN " + name + " TEXT");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS supporters (
                request INTEGER NOT NULL REFERENCES requests(id), user TEXT NOT NULL, created TEXT,
                PRIMARY KEY(request,user));
            CREATE INDEX IF NOT EXISTS supporters_user ON supporters(user,request);
            """);
        // Legacy followers represent existing interest, never proof of original authorship.
        if (previousVersion < 2)
            Execute(connection, "INSERT OR IGNORE INTO supporters(request,user) SELECT request,user FROM followers");
        Execute(connection, "PRAGMA user_version=2");
    }

    public Participation Participation(long id, Guid user)
    {
        ValidateUser(user);
        using var connection = Open();
        using var command = Command(connection, """
            SELECT (SELECT COUNT(*) FROM supporters WHERE request=r.id),
                EXISTS(SELECT 1 FROM supporters WHERE request=r.id AND user=$1),
                EXISTS(SELECT 1 FROM followers WHERE request=r.id AND user=$1),
                COALESCE(requested_by=$1,0),created FROM requests r WHERE id=$0
            """, id, user.ToString());
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new KeyNotFoundException();
        return new(row.GetInt32(0), row.GetBoolean(1), row.GetBoolean(2), row.GetBoolean(3),
            row.IsDBNull(4) ? null : DateTimeOffset.Parse(row.GetString(4)));
    }

    public void Support(long id, Guid user, bool enabled)
    {
        ValidateUser(user); Get(id);
        using var connection = Open();
        Execute(connection, enabled
            ? "INSERT OR IGNORE INTO supporters(request,user,created) VALUES($0,$1,$2)"
            : "DELETE FROM supporters WHERE request=$0 AND user=$1", id, user.ToString(), DateTimeOffset.UtcNow.ToString("O"));
    }

    public AdministrativeRequest Administrative(long id)
    {
        using var connection = Open();
        using var command = Command(connection, "SELECT requested_by,created FROM requests WHERE id=$0", id);
        Guid? requester; DateTimeOffset? created;
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) throw new KeyNotFoundException();
            requester = row.IsDBNull(0) ? null : Guid.Parse(row.GetString(0));
            created = row.IsDBNull(1) ? null : DateTimeOffset.Parse(row.GetString(1));
        }
        using var supporters = Command(connection, "SELECT user FROM supporters WHERE request=$0 ORDER BY created,user", id);
        using var people = supporters.ExecuteReader();
        var ids = new List<Guid>();
        while (people.Read()) ids.Add(Guid.Parse(people.GetString(0)));
        people.Close();
        return new(requester, ids.ToArray(), created, AdminHistory(id).LastOrDefault(x=>x.Actor.HasValue));
    }

    public WorkflowNotification[] WorkflowNotifications(Guid user, int start = 0, int limit = 30)
    {
        ValidateUser(user);
        if (start < 0 || limit is < 1 or > 100) throw new ArgumentException("Invalid pagination.");
        using var connection = Open();
        using var command = Command(connection, "SELECT id,request,event,seen,created,actor,message FROM notifications WHERE user=$0 ORDER BY id DESC LIMIT $1 OFFSET $2", user.ToString(), limit, start);
        using var row = command.ExecuteReader(); var result = new List<WorkflowNotification>();
        while (row.Read()) result.Add(new(row.GetInt64(0), row.GetInt64(1), row.GetString(2), row.GetBoolean(3),
            row.IsDBNull(4) ? null : DateTimeOffset.Parse(row.GetString(4)), row.IsDBNull(5) ? null : Guid.Parse(row.GetString(5)), row.IsDBNull(6) ? "" : row.GetString(6)));
        return result.ToArray();
    }

    public bool MarkNotificationRead(Guid user, long id)
    {
        ValidateUser(user);
        using var connection = Open();
        using var command = Command(connection, "UPDATE notifications SET seen=1,read_at=COALESCE(read_at,$2) WHERE user=$0 AND id=$1", user.ToString(), id, DateTimeOffset.UtcNow.ToString("O"));
        return command.ExecuteNonQuery() != 0;
    }
}
