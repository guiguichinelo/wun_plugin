using System.Collections.Concurrent;
using System.Security.Claims;
using Jellyfin.Plugin.Wun;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wun.Core;
using Xunit;

public sealed class ListLoadingTests
{
 sealed class Metadata:IMetadataAccess
 {
  public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously),Four=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public readonly ConcurrentBag<Guid> Owners=[];
  public int Active,Maximum,Calls;
  public Task<SearchCard[]> Search(Guid u,string q,string k,CancellationToken ct,SearchFilters? f=null)=>Task.FromResult(Array.Empty<SearchCard>());
  public async Task<BaseItem?> Resolve(Guid user,string kind,string id,CancellationToken ct)
  {
   Owners.Add(user);Interlocked.Increment(ref Calls);var active=Interlocked.Increment(ref Active);
   int before;do{before=Maximum;}while(active>before&&Interlocked.CompareExchange(ref Maximum,active,before)!=before);
   if(active==4)Four.TrySetResult();
   try{await Release.Task.WaitAsync(ct);return id=="4"?null:new Movie{Name="Visible title"};}finally{Interlocked.Decrement(ref Active);}
  }
 }
 static RequestsController Controller(RequestStore store,Metadata metadata,Guid owner)
 {
  var access=new FixtureAccess();return new(store,metadata,access,access){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId",owner.ToString())],"fixture"))}}};
 }
 [Fact]public async Task QueueLoadsAtMostFourCardsInParallelAndRetainsOrderAndVisibility()
 {
  var path=Path.Combine(Path.GetTempPath(),"wun-list-"+Guid.NewGuid());Directory.CreateDirectory(path);
  try{
   var store=new RequestStore(Path.Combine(path,"wun.db"));var owner=Guid.NewGuid();for(var i=1;i<=9;i++)store.Create(owner,new(MediaKind.Movie,i.ToString(),"Title "+i),[]);
   var metadata=new Metadata();var expected=store.List(limit:30,sort:"popular").Items.Where(c=>c.Selection.TmdbId!="4").Select(c=>c.Id).ToArray();
   var task=Controller(store,metadata,owner).AdminList(ct:default);await metadata.Four.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(4,metadata.Active);metadata.Release.SetResult();
   var result=Assert.IsType<OkObjectResult>(await task);var json=System.Text.Json.JsonSerializer.SerializeToElement(result.Value);var ids=json.GetProperty("Items").EnumerateArray().Select(c=>c.GetProperty("Id").GetInt64()).ToArray();
   Assert.Equal(expected,ids);Assert.Equal(4,metadata.Maximum);Assert.Equal(9,metadata.Calls);Assert.All(metadata.Owners,id=>Assert.Equal(owner,id));
  }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(path,true);}
 }
 [Fact]public async Task PendingFilterRunsBeforeMetadataAndCancellationStopsQueuedLoads()
 {
  var path=Path.Combine(Path.GetTempPath(),"wun-list-"+Guid.NewGuid());Directory.CreateDirectory(path);
  try{
   var store=new RequestStore(Path.Combine(path,"wun.db"));var owner=Guid.NewGuid();for(var i=1;i<=9;i++){var card=store.Create(owner,new(MediaKind.Movie,i.ToString(),"Title "+i),[]);if(i>5)store.Decide(card.Id,card.Revision,owner,Decision.Completed,"","");}
   var metadata=new Metadata();using var ct=new CancellationTokenSource();var task=Controller(store,metadata,owner).AdminList(decision:Decision.Pending,ct:ct.Token);await metadata.Four.Task.WaitAsync(TimeSpan.FromSeconds(5));ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.InRange(metadata.Calls,4,5);Assert.Equal(0,metadata.Active);
   var fresh=new Metadata();fresh.Release.SetResult();await Controller(store,fresh,owner).AdminList(decision:Decision.Pending,ct:default);Assert.Equal(5,fresh.Calls);Assert.Equal(9,store.List().Total);
  }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(path,true);}
 }
}
