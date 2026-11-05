using Wun.Core;
using Microsoft.Data.Sqlite;
using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Jellyfin.Plugin.Wun;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
public class LibraryFixtureProxy:DispatchProxy
{
 public bool Present {get;set;}=true;
 protected override object? Invoke(MethodInfo? method,object?[]? args)
 {
  if(method!.Name!="GetItemsResult")throw new NotSupportedException(method.Name);
  var result=Activator.CreateInstance(method.ReturnType)!;
  method.ReturnType.GetProperty("Items")!.SetValue(result,Present?new BaseItem[]{new Movie{Path="/fixture/movie.mkv"}}:Array.Empty<BaseItem>());
  return result;
 }
}
public sealed partial class AccessTests
{
 [Theory]
 [InlineData(0,0,100,100,true,true,false)]
 [InlineData(30,0,30,0,true,true,true)]
 [InlineData(30,0,29,0,true,true,false)]
 [InlineData(0,30,0,31,true,true,true)]
 [InlineData(0,30,0,31,false,true,false)]
 [InlineData(30,30,100,100,true,false,false)]
 [InlineData(30,30,-10,-10,true,true,false)]
 public void ExpirationUsesIndependentClocksAndPreservesUnknownCoverage(int approvalDays,int missingDays,int approvedAge,int missingAge,bool monitor,bool known,bool expired)
 {
  var now=DateTimeOffset.UtcNow;
  var card=new RequestCard(1,new(MediaKind.Series,"42","Fixture"),Decision.Approved,Availability.Absent,monitor,1);
  Assert.Equal(expired,RequestStore.ExpirationReason(card,known,now.AddDays(-approvedAge),now.AddDays(-missingAge),approvalDays,missingDays,now)!=null);
  Assert.Null(RequestStore.ExpirationReason(card,known,null,null,approvalDays,missingDays,now));
  Assert.Null(RequestStore.ExpirationReason(card with{Decision=Decision.Completed},true,now.AddDays(-1000),now.AddDays(-1000),30,30,now));
 }
 [Fact] public async Task AvailabilityFinalizesEvenRejectedOrCanceledAndDoesNotRepeatNotifications()
 {
  var library=DispatchProxy.Create<ILibraryManager,LibraryFixtureProxy>();
  var runner=new MaintenanceRunner(store,library,null!,null!);
  foreach(var state in new[]{Decision.Pending,Decision.Rejected,Decision.Canceled})
  {
   var card=store.Create(user,new(MediaKind.Movie,((int)state+100).ToString(),"Fixture"),[]);
   if(state!=Decision.Pending)store.Transition(card.Id,card.Revision,state,"Fixture transition");
  }
  await runner.Run(false,new Progress<double>(),default);
  Assert.All(store.All(),c=>Assert.Equal(Decision.Completed,c.Decision));
  var count=store.Notifications(user).Length;
  await runner.Run(false,new Progress<double>(),default);Assert.Equal(count,store.Notifications(user).Length);
 }
 [Fact] public async Task RejectedRequestRequiresConfirmationAndReopensWithoutDuplicateInterest()
 {
  var c=store.Create(user,new(MediaKind.Movie,"42","Fixture"),[]);store.Decide(c.Id,1,user,Decision.Rejected,"Not now","");
  using var client=Client(other);
  Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42"})).StatusCode);
  Assert.Equal(Decision.Rejected,store.Get(c.Id).Decision);
  (await client.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42",ConfirmReopen=true})).EnsureSuccessStatusCode();
  Assert.Equal(Decision.Pending,store.Get(c.Id).Decision);Assert.Equal(2,store.Participation(c.Id,other).SupportCount);
  Assert.Null(store.AdminHistory(c.Id).Last().Actor);
 }
 [Fact] public void RetentionPreservesByDefaultAndDeletesOnlyEligibleRelationsWithoutReusingIds()
 {
  var c=store.Create(user,new(MediaKind.Movie,"42","Fixture"),[]);
  store.Transition(c.Id,1,Decision.Completed,"Available");
  Assert.Empty(store.Cleanup(new()).Requests);
  Assert.Empty(store.Cleanup(new(1)).Requests);
  using(var db=new SqliteConnection("Data Source="+Path.Combine(path,"wun.db"))){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE system_history SET created='2000-01-01T00:00:00Z'";q.ExecuteNonQuery();}
  Assert.Single(store.Cleanup(new(1)).Requests);Assert.Single(store.All());
  Assert.Single(store.Cleanup(new(1),true).Requests);Assert.Empty(store.All());Assert.Empty(store.Notifications(user));
  var next=store.Create(other,new(MediaKind.Movie,"43","Next"),[]);Assert.True(next.Id>c.Id);
  Assert.NotNull(store.LastCleanup());
 }
 [Fact] public void UnknownReadDatesArePreservedAndOnlyReadNotificationsExpire()
 {
  var c=store.Create(user,new(MediaKind.Movie,"42","Fixture"),[other]);store.Follow(c.Id,user,true);store.Decide(c.Id,1,user,Decision.Approved,"","");
  store.MarkNotificationsRead(user);
  using(var db=new SqliteConnection("Data Source="+Path.Combine(path,"wun.db"))){db.Open();using var q=db.CreateCommand();q.CommandText="UPDATE notifications SET read_at='2000-01-01T00:00:00Z' WHERE user=$u";q.Parameters.AddWithValue("$u",user.ToString());q.ExecuteNonQuery();}
  Assert.Single(store.Cleanup(new(ReadDays:1),true).Notifications);Assert.NotEmpty(store.Notifications(other));Assert.Single(store.All());
 }
}
