namespace Wun.Core;

public sealed record RequestMetrics(int Total,int Pending,int Approved,int Deferred,int Rejected,
    int Movies,int Series,int Supports,int InterestedUsers,int Monitored,int ReviewsDue,
    int UnknownRequesters,int Completed,int Canceled,DateTimeOffset GeneratedAt,PopularRequest[] MostWanted);
public sealed record PopularRequest(long Id,string Title,MediaKind Kind,int? Season,int? Episode,int Supports);

public sealed partial class RequestStore
{
    public RequestMetrics Metrics()
    {
        using var connection=Open();
        using var transaction=connection.BeginTransaction();
        int Count(string sql,params object?[] args){using var c=Command(connection,sql,args);return Convert.ToInt32(c.ExecuteScalar());}
        var now=DateTimeOffset.UtcNow;
        var total=Count("SELECT COUNT(*) FROM requests");
        var states=Enumerable.Range(0,6).Select(i=>Count("SELECT COUNT(*) FROM requests WHERE decision=$0",i)).ToArray();
        var movies=Count("SELECT COUNT(*) FROM requests WHERE kind=$0",(int)MediaKind.Movie);
        var series=Count("SELECT COUNT(*) FROM requests WHERE kind=$0",(int)MediaKind.Series);
        var supports=Count("SELECT COUNT(*) FROM supporters");
        var people=Count("SELECT COUNT(DISTINCT user) FROM supporters");
        var monitored=Count("SELECT COUNT(*) FROM requests WHERE monitor=1");
        // Only the latest decision's review date counts. Superseded reviews are not outstanding work.
        var due=Count("""
            SELECT COUNT(*) FROM requests r JOIN history h ON h.id=(SELECT MAX(id) FROM history WHERE request=r.id)
            WHERE r.decision=$0 AND h.review_at IS NOT NULL AND julianday(h.review_at)<=julianday($1)
            """,(int)Decision.Deferred,now.ToString("O"));
        var unknown=Count("SELECT COUNT(*) FROM requests WHERE requested_by IS NULL");
        using var query=Command(connection,"""
            SELECT r.id,r.title,r.kind,r.season,r.episode,COUNT(s.user) AS votes
            FROM requests r LEFT JOIN supporters s ON s.request=r.id
            GROUP BY r.id ORDER BY votes DESC,r.id ASC LIMIT 5
            """);
        var popular=new List<PopularRequest>();
        using(var row=query.ExecuteReader())while(row.Read())popular.Add(new(row.GetInt64(0),row.GetString(1),
            (MediaKind)row.GetInt32(2),row.IsDBNull(3)?null:row.GetInt32(3),row.IsDBNull(4)?null:row.GetInt32(4),row.GetInt32(5)));
        transaction.Commit();
        return new(total,states[0],states[1],states[2],states[3],movies,series,supports,people,monitored,due,unknown,states[4],states[5],now,popular.ToArray());
    }
}
