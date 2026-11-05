using System.Text.Json;
using Wun.Core;

var directory = Path.Combine(Path.GetTempPath(), "wun-tests-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
var database = Path.Combine(directory, "requests.db");
var store = new RequestStore(database);
var user = Guid.NewGuid();
var other = Guid.NewGuid();
var admin = Guid.NewGuid();
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    passed++;
    Console.WriteLine("PASS " + description);
}
void Reject<T>(Action action, string description) where T : Exception
{
    try { action(); } catch (T) { Check(true, description); return; }
    throw new Exception(description);
}

try
{
    Reject<ArgumentException>(() => store.Create(user, new(MediaKind.Movie, "1", "Movie", 1), [admin]),
        "Movies cannot request seasons");
    Reject<ArgumentException>(() => store.Create(user, new(MediaKind.Series, "1", "Series", Episode: 3), [admin]),
        "Episode requires season");
    Reject<ArgumentException>(() => store.Create(Guid.Empty, new(MediaKind.Movie, "1", "Movie"), [admin]),
        "Empty user is rejected");
    Reject<ArgumentException>(() => store.Create(user, new(MediaKind.Movie, "001", "Movie"), [admin]),
        "Identifiers must be canonical");
    var request = store.Create(user, new(MediaKind.Series, "42", "Series ' quoted"), [admin]);
    Check(request.Monitor && request.Availability == Availability.Unknown,
        "Whole series enables continuous monitoring without assuming availability");
    var duplicate = store.Create(other, request.Selection, [admin]);
    Check(duplicate.Id == request.Id && store.List().Total == 1, "Duplicates join the existing request");
    Check(store.Notifications(admin).Length == 1, "Joining a request does not notify administrators twice");
    Check(store.List(follower: other).Total == 1, "Followers can select their own requests");
    store.Decide(request.Id, request.Revision, admin, Decision.Deferred, "Later", "Private source", DateTimeOffset.UtcNow.AddDays(7));
    Check(store.Get(request.Id).Decision == Decision.Deferred, "Administrative decision is persisted");
    Reject<RevisionConflictException>(() => store.Decide(request.Id, request.Revision, admin, Decision.Rejected, "", ""),
        "Stale administrator edit is rejected");
    Check(store.AdminHistory(request.Id).Length == 1, "Conflicting edits leave no history or notifications");
    Check(!JsonSerializer.Serialize(store.History(request.Id)).Contains("Private source"),
        "Public history excludes internal notes");
    Check(!JsonSerializer.Serialize(store.List()).Contains(user.ToString()), "Public cards exclude requester identifiers");
    Check(store.AdminHistory(request.Id)[0].InternalNote == "Private source", "Private notes remain available to admin adapter");
    store.MarkNotificationsRead(user);
    Check(store.Notifications(user).All(x => x.Read) && store.Notifications(other).All(x => !x.Read),
        "Notification read state is isolated per user");
    store.Follow(request.Id, other, false);
    Check(store.List(follower: other).Total == 0 && store.List().Total == 1,
        "Unfollowing preserves the community request");
    store.Create(user, new(MediaKind.Movie, "42", "Unrelated movie"), [admin]);
    Check(store.List().Total == 2, "Movie and series provider identifier spaces remain distinct");
    Check(store.List(start: 1, limit: 1).Items.Length == 1 && store.List(start: 1, limit: 1).Total == 2,
        "Pagination returns total separately from page length");
    Check(new RequestStore(database).AdminHistory(request.Id).Length == 1, "Restart preserves history and schema");
    var ownership = store.Administrative(request.Id);
    Check(ownership.Requester == user && ownership.Created.HasValue, "New requests retain their original author and date");
    Check(store.Participation(request.Id, other).SupportCount == 2, "Duplicate requests contribute one support per person");
    store.Support(request.Id, other, true);
    store.Support(request.Id, other, true);
    Check(store.Participation(request.Id, other).SupportCount == 2, "Repeated support cannot inflate priority");
    Check(!store.Participation(request.Id, other).Following && store.Participation(request.Id, other).Supporting,
        "Support and notification following are independent");
    Check(store.List(participant: other).Total == 1, "A user's requests include supported requests after unfollowing");
    store.Support(request.Id, other, false);
    Check(new RequestStore(database).Participation(request.Id, other).SupportCount == 1,
        "Restart does not recreate withdrawn supports from followers");
    Check(store.List(participant: other).Total == 0 && store.List().Total == 2, "Removing interest never deletes shared requests");
    var firstNotice = store.WorkflowNotifications(admin).First();
    Check(firstNotice.Created.HasValue && firstNotice.Actor == user, "New notices retain event time and actor");
    Check(!store.MarkNotificationRead(other, firstNotice.Id), "An individual read cannot mark another user's notification");
    Reject<ArgumentException>(() => store.List(limit: 10000), "Unbounded pages are rejected");
    Console.WriteLine($"{passed} checks passed.");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(directory, recursive: true);
}
