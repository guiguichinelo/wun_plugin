using Microsoft.Data.Sqlite;
namespace Wun.Core;
public sealed record Retention(int CompletedDays=0,int CanceledDays=0,int ReadDays=0);
public sealed record CleanupPreview(long[] Requests,long[] Notifications);
public sealed partial class RequestStore
{
 public static string? ExpirationReason(RequestCard card,bool known,DateTimeOffset? approved,DateTimeOffset? missing,int approvalDays,int missingDays,DateTimeOffset now)
 {
  if(!known||card.Decision is not(Decision.Approved or Decision.Deferred))return null;
  if(approvalDays>0&&approved.HasValue&&approved<=now.AddDays(-approvalDays))return "Availability deadline since approval expired.";
  if(card.Monitor&&missingDays>0&&missing.HasValue&&missing<=now.AddDays(-missingDays))return "Monitoring stopped: a released episode remained unavailable beyond the configured deadline.";
  return null;
 }
 static void UpgradeMaintenance(SqliteConnection c)
 {
  using var columns=Command(c,"PRAGMA table_info(notifications)");
  var names=new List<string>();using(var rows=columns.ExecuteReader())while(rows.Read())names.Add(rows.GetString(1));
  if(!names.Contains("read_at"))Execute(c,"ALTER TABLE notifications ADD COLUMN read_at TEXT");
  // Preserve IDs while upgrading old tables to non-reusable public identifiers.
  using var definition=Command(c,"SELECT sql FROM sqlite_master WHERE name='requests'");
  if(!Convert.ToString(definition.ExecuteScalar())!.Contains("AUTOINCREMENT"))
  {
   foreach(var table in new[]{"requests","notifications","history","followers","supporters"})Execute(c,$"CREATE TEMP TABLE copy_{table} AS SELECT * FROM {table}");
   foreach(var table in new[]{"notifications","history","followers","supporters","requests"})Execute(c,$"DROP TABLE {table}");
   Execute(c,"""
    CREATE TABLE requests(id INTEGER PRIMARY KEY AUTOINCREMENT,identity TEXT UNIQUE NOT NULL,kind INTEGER NOT NULL,tmdb TEXT NOT NULL,title TEXT NOT NULL,season INTEGER,episode INTEGER,decision INTEGER NOT NULL DEFAULT 0,availability INTEGER NOT NULL DEFAULT 0,monitor INTEGER NOT NULL,revision INTEGER NOT NULL DEFAULT 1,requested_by TEXT,created TEXT);
    CREATE TABLE notifications(id INTEGER PRIMARY KEY AUTOINCREMENT,user TEXT NOT NULL,request INTEGER NOT NULL REFERENCES requests(id),event TEXT NOT NULL,seen INTEGER NOT NULL DEFAULT 0,actor TEXT,created TEXT,message TEXT,read_at TEXT);
    CREATE TABLE history(id INTEGER PRIMARY KEY AUTOINCREMENT,request INTEGER NOT NULL REFERENCES requests(id),actor TEXT,decision INTEGER NOT NULL,message TEXT NOT NULL,internal_note TEXT NOT NULL,review_at TEXT,created TEXT NOT NULL);
    CREATE TABLE followers(request INTEGER NOT NULL REFERENCES requests(id),user TEXT NOT NULL,PRIMARY KEY(request,user));
    CREATE TABLE supporters(request INTEGER NOT NULL REFERENCES requests(id),user TEXT NOT NULL,created TEXT,PRIMARY KEY(request,user));
    """);
   foreach(var table in new[]{"requests","notifications","history","followers","supporters"}){Execute(c,$"INSERT INTO {table} SELECT * FROM copy_{table}");Execute(c,$"DROP TABLE copy_{table}");}
  }
  // Fresh schema also needs nullable actors for genuine system events.
  Execute(c,"CREATE TABLE IF NOT EXISTS system_history(id INTEGER PRIMARY KEY AUTOINCREMENT,request INTEGER NOT NULL REFERENCES requests(id),decision INTEGER NOT NULL,message TEXT NOT NULL,created TEXT NOT NULL)");
  Execute(c,"CREATE TABLE IF NOT EXISTS follow_preferences(request INTEGER NOT NULL REFERENCES requests(id),user TEXT NOT NULL,enabled INTEGER NOT NULL,PRIMARY KEY(request,user))");
  Execute(c,"CREATE TABLE IF NOT EXISTS maintenance(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
  Execute(c,"CREATE INDEX IF NOT EXISTS notifications_user ON notifications(user,id);CREATE INDEX IF NOT EXISTS supporters_user ON supporters(user,request);PRAGMA user_version=3;");
 }
 public RequestCard[] All(){using var c=Open();using var q=Command(c,$"SELECT {Columns} FROM requests ORDER BY id");using var rows=q.ExecuteReader();var result=new List<RequestCard>();while(rows.Read())result.Add(ReadCard(rows));return result.ToArray();}
 public DateTimeOffset? ApprovedAt(long id){using var c=Open();using var q=Command(c,"SELECT created FROM history WHERE request=$0 AND decision=1 ORDER BY id DESC LIMIT 1",id);return DateTimeOffset.TryParse(q.ExecuteScalar() as string,out var date)?date:null;}
 public void Transition(long id,long revision,Decision state,string message,IEnumerable<Guid>? administrators=null)
 {
  using var c=Open();using var tx=c.BeginTransaction();var now=DateTimeOffset.UtcNow.ToString("O");
  using var q=Command(c,"UPDATE requests SET decision=$0,revision=revision+1,monitor=CASE WHEN $0 IN (4,5) THEN 0 WHEN $0=0 AND kind=1 AND season IS NULL THEN 1 ELSE monitor END WHERE id=$1 AND revision=$2 AND decision<>$0",(int)state,id,revision);
  if(q.ExecuteNonQuery()!=1)throw new RevisionConflictException();
  Execute(c,"INSERT INTO system_history(request,decision,message,created) VALUES($0,$1,$2,$3)",id,(int)state,message,now);
  Execute(c,"INSERT INTO notifications(user,request,event,created,message) SELECT user,request,$0,$2,$3 FROM followers WHERE request=$1",state.ToString(),id,now,message);
  foreach(var user in (administrators??[]).Distinct())Execute(c,"INSERT INTO notifications(user,request,event,created,message) SELECT $0,$1,'Reopened',$2,$3 WHERE NOT EXISTS(SELECT 1 FROM followers WHERE request=$1 AND user=$0)",user.ToString(),id,now,message);
  tx.Commit();
 }
 public HistoryEntry[] SystemHistory(long id){using var c=Open();using var q=Command(c,"SELECT decision,message,created FROM system_history WHERE request=$0 ORDER BY id",id);using var rows=q.ExecuteReader();var list=new List<HistoryEntry>();while(rows.Read())list.Add(new((Decision)rows.GetInt32(0),rows.GetString(1),DateTimeOffset.Parse(rows.GetString(2))));return list.ToArray();}
 public CleanupPreview Cleanup(Retention policy,bool execute=false,CancellationToken ct=default)
 {
  if(policy.CompletedDays<0||policy.CanceledDays<0||policy.ReadDays<0)throw new ArgumentException();
  using var c=Open();using var tx=c.BeginTransaction();var ids=new List<long>();var notices=new List<long>();var now=DateTimeOffset.UtcNow;
  using(var q=Command(c,"""
   SELECT r.id,r.decision,MAX(h.created) FROM requests r JOIN
   (SELECT request,decision,created FROM history UNION ALL SELECT request,decision,created FROM system_history) h
   ON h.request=r.id AND h.decision=r.decision WHERE r.decision IN (4,5) GROUP BY r.id
   """))using(var rows=q.ExecuteReader())while(rows.Read()){var days=rows.GetInt32(1)==4?policy.CompletedDays:policy.CanceledDays;if(days>0&&DateTimeOffset.TryParse(rows.GetString(2),out var date)&&date<=now.AddDays(-days))ids.Add(rows.GetInt64(0));}
  if(policy.ReadDays>0){using var q=Command(c,"SELECT id FROM notifications WHERE seen=1 AND julianday(read_at)<=julianday($0)",now.AddDays(-policy.ReadDays).ToString("O"));using var rows=q.ExecuteReader();while(rows.Read())notices.Add(rows.GetInt64(0));}
  if(execute){foreach(var id in notices){ct.ThrowIfCancellationRequested();Execute(c,"DELETE FROM notifications WHERE id=$0",id);}foreach(var id in ids){ct.ThrowIfCancellationRequested();foreach(var table in new[]{"notifications","followers","supporters","history","system_history","follow_preferences"})Execute(c,$"DELETE FROM {table} WHERE request=$0",id);Execute(c,"DELETE FROM requests WHERE id=$0",id);}Execute(c,"INSERT OR REPLACE INTO maintenance VALUES('cleanup',$0)",System.Text.Json.JsonSerializer.Serialize(new{At=now,Requests=ids.Count,Notifications=notices.Count}));}
  tx.Commit();return new(ids.ToArray(),notices.ToArray());
 }
 public void DefaultFollow(long id,Guid user,bool enabled){using var c=Open();using var q=Command(c,"SELECT COUNT(*) FROM follow_preferences WHERE request=$0 AND user=$1",id,user.ToString());if(Convert.ToInt32(q.ExecuteScalar())==0){if(enabled)Execute(c,"INSERT OR IGNORE INTO followers VALUES($0,$1)",id,user.ToString());else Execute(c,"DELETE FROM followers WHERE request=$0 AND user=$1",id,user.ToString());}}
 public string? LastCleanup(){using var c=Open();using var q=Command(c,"SELECT value FROM maintenance WHERE key='cleanup'");return q.ExecuteScalar() as string;}
}
