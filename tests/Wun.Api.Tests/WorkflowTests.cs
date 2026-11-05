using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Wun.Core;
using Xunit;

public sealed partial class AccessTests
{
    [Fact]
    public async Task UserListIsPersonalEvenWhenMineFalseIsForged()
    {
        var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[]);
        using var c=Client(other);
        var empty=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/requests?mine=false");
        Assert.Equal(0,empty.GetProperty("items").GetArrayLength());
        (await c.PutAsJsonAsync($"/WUN/v1/requests/{card.Id}/support?enabled=true",new{})).EnsureSuccessStatusCode();
        var mine=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/requests");
        var row=Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal(2,row.GetProperty("supportCount").GetInt32());
        Assert.True(row.GetProperty("supporting").GetBoolean());
        Assert.False(row.TryGetProperty("requester",out _));
        Assert.DoesNotContain(user.ToString(),row.GetRawText());
    }

    [Fact]
    public async Task AdministrativeQueueAndPeopleRequireElevation()
    {
        store.Create(other,new(MediaKind.Movie,"42","Title"),[]);
        using var regular=Client(user);
        Assert.Equal(HttpStatusCode.Forbidden,(await regular.GetAsync("/WUN/v1/admin/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await regular.GetAsync("/WUN/v1/admin/requests/1")).StatusCode);
        using var admin=Client(user,true);
        var queue=await admin.GetFromJsonAsync<JsonElement>("/WUN/v1/admin/requests");
        var row=Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(other,row.GetProperty("requester").GetProperty("id").GetGuid());
        Assert.True(row.GetProperty("requesterKnown").GetBoolean());
    }

    [Fact]
    public async Task DuplicateCreatesAddUniqueSupportWithoutDuplicatingQueue()
    {
        using var a=Client(user);using var b=Client(other);
        var body=new{Kind="Movie",TmdbId="42"};
        (await a.PostAsJsonAsync("/WUN/v1/requests",body)).EnsureSuccessStatusCode();
        for(var i=0;i<3;i++)(await b.PostAsJsonAsync("/WUN/v1/requests",body)).EnsureSuccessStatusCode();
        Assert.Equal(1,store.List().Total);
        var card=store.List().Items[0];Assert.Equal(2,store.Participation(card.Id,user).SupportCount);
        Assert.Equal(user,store.Administrative(card.Id).Requester);
        (await b.PutAsJsonAsync($"/WUN/v1/requests/{card.Id}/support?enabled=false",new{})).EnsureSuccessStatusCode();
        Assert.Equal(1,store.Participation(card.Id,user).SupportCount);
        Assert.True(store.Participation(card.Id,other).Following);
    }

    [Fact]
    public async Task RichNotificationsIncludeTitlePublicMessageAndDateButNotPrivateNotes()
    {
        var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[other]);
        store.Decide(card.Id,1,other,Decision.Approved,"Public reply","PRIVATE_NOTE");
        using var c=Client(user);
        var notices=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/notifications");
        var notification=Assert.Single(notices.EnumerateArray());
        Assert.Equal("Public reply",notification.GetProperty("message").GetString());
        Assert.Equal("Title",notification.GetProperty("request").GetProperty("selection").GetProperty("title").GetString());
        Assert.NotEqual(JsonValueKind.Null,notification.GetProperty("created").ValueKind);
        Assert.DoesNotContain("PRIVATE_NOTE",notification.GetRawText());
        Assert.DoesNotContain(other.ToString(),notification.GetRawText());
        var ownId=notification.GetProperty("id").GetInt64();
        using var otherClient=Client(other);
        Assert.Equal(HttpStatusCode.NotFound,(await otherClient.PostAsJsonAsync($"/WUN/v1/notifications/{ownId}/read",new{})).StatusCode);
        (await c.PostAsJsonAsync($"/WUN/v1/notifications/{ownId}/read",new{})).EnsureSuccessStatusCode();
        Assert.True(store.Notifications(user)[0].Read);
        Assert.False(store.Notifications(other)[0].Read);
    }

    [Fact]
    public void VersionOneMigrationNeverInventsAnAuthorOrRecreatesWithdrawnSupport()
    {
        var legacy=Path.Combine(path,"legacy.db");
        using(var db=new SqliteConnection("Data Source="+legacy))
        {
            db.Open();using var cmd=db.CreateCommand();cmd.CommandText="""
                CREATE TABLE requests(id INTEGER PRIMARY KEY,identity TEXT UNIQUE NOT NULL,kind INTEGER NOT NULL,tmdb TEXT NOT NULL,title TEXT NOT NULL,season INTEGER,episode INTEGER,decision INTEGER NOT NULL DEFAULT 0,availability INTEGER NOT NULL DEFAULT 0,monitor INTEGER NOT NULL,revision INTEGER NOT NULL DEFAULT 1);
                CREATE TABLE followers(request INTEGER NOT NULL REFERENCES requests(id),user TEXT NOT NULL,PRIMARY KEY(request,user));
                CREATE TABLE history(id INTEGER PRIMARY KEY,request INTEGER NOT NULL REFERENCES requests(id),actor TEXT NOT NULL,decision INTEGER NOT NULL,message TEXT NOT NULL,internal_note TEXT NOT NULL,review_at TEXT,created TEXT NOT NULL);
                CREATE TABLE notifications(id INTEGER PRIMARY KEY,user TEXT NOT NULL,request INTEGER NOT NULL REFERENCES requests(id),event TEXT NOT NULL,seen INTEGER NOT NULL DEFAULT 0);
                INSERT INTO requests(id,identity,kind,tmdb,title,monitor) VALUES(1,'Movie:Tmdb:42::',0,'42','Legacy title',0);
                INSERT INTO followers VALUES(1,$user);
                INSERT INTO notifications(user,request,event) VALUES($user,1,'request');
                PRAGMA user_version=1;
                """;cmd.Parameters.AddWithValue("$user",user.ToString());cmd.ExecuteNonQuery();
        }
        var migrated=new RequestStore(legacy);
        Assert.Null(migrated.Administrative(1).Requester);Assert.Null(migrated.Administrative(1).Created);
        Assert.Equal(1,migrated.Participation(1,user).SupportCount);
        Assert.Single(migrated.Notifications(user));Assert.Null(migrated.WorkflowNotifications(user)[0].Created);
        migrated.Support(1,user,false);
        Assert.Equal(0,new RequestStore(legacy).Participation(1,user).SupportCount);
    }
}
