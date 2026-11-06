using System.Reflection;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.Wun;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Xunit;

public sealed class CacheTests
{
 sealed class Clock:TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
 [Fact]public async Task IdenticalLoadsAreSharedAndOneCanceledWaiterDoesNotCancelOthers()
 {
  using var cache=new BoundedAsyncCache<string>();using var canceled=new CancellationTokenSource();
  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
  async Task<string> Load(CancellationToken ct){Interlocked.Increment(ref calls);entered.SetResult();await release.Task.WaitAsync(ct);return "metadata";}
  var first=cache.Get("same",TimeSpan.FromMinutes(1),Load,canceled.Token);await entered.Task;
  var second=cache.Get("same",TimeSpan.FromMinutes(1),Load,default);canceled.Cancel();
  await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);release.SetResult();Assert.Equal("metadata",await second);Assert.Equal(1,calls);
  Assert.Equal("metadata",await cache.Get("same",TimeSpan.FromMinutes(1),Load,default));Assert.Equal(1,calls);
 }
 [Fact]public async Task ExpirationAndLocaleKeysDoNotReuseStaleValues()
 {
  var time=new Clock();using var cache=new BoundedAsyncCache<string>(4,time);int calls=0;
  Task<string> Load(CancellationToken _)=>Task.FromResult((++calls).ToString());
  Assert.Equal("1",await cache.Get("Movie:42:pt:BR",TimeSpan.FromSeconds(5),Load,default));
  Assert.Equal("2",await cache.Get("Movie:42:en:US",TimeSpan.FromSeconds(5),Load,default));
  time.Now=time.Now.AddSeconds(6);Assert.Equal("3",await cache.Get("Movie:42:pt:BR",TimeSpan.FromSeconds(5),Load,default));
 }
 [Fact]public async Task FailedOrMissingProviderResponsesAreRetried()
 {
  using var cache=new BoundedAsyncCache<string?>();int calls=0;
  Task<string?> Load(CancellationToken _)=>++calls==1?Task.FromException<string?>(new HttpRequestException()):Task.FromResult<string?>(calls==2?null:"ok");
  await Assert.ThrowsAsync<HttpRequestException>(()=>cache.Get("key",TimeSpan.FromMinutes(1),Load,default));
  Assert.Null(await cache.Get("key",TimeSpan.FromMinutes(1),Load,default));
  Assert.Equal("ok",await cache.Get("key",TimeSpan.FromMinutes(1),Load,default));Assert.Equal(3,calls);
 }
 [Fact]public async Task ReachingCapacityDoesNotClearAllHotEntries()
 {
  using var cache=new BoundedAsyncCache<string>(2);
  foreach(var key in new[]{"a","b","c"})await cache.Get(key,TimeSpan.FromMinutes(1),_=>Task.FromResult(key),default);
  int loads=0;
  await cache.Get("a",TimeSpan.FromMinutes(1),_=>{loads++;return Task.FromResult("a");},default);
  await cache.Get("b",TimeSpan.FromMinutes(1),_=>{loads++;return Task.FromResult("b");},default);
  Assert.True(loads<2,"Capacity pressure must not flush every existing entry.");
 }
 [Fact]public void LocalReadsAreScopedByCurrentPolicyUserAndLibraryChanges()
 {
  var library=DispatchProxy.Create<ILibraryManager,CacheLibraryProxy>();var events=(CacheLibraryProxy)(object)library;
  var users=DispatchProxy.Create<IUserManager,CacheUserProxy>();var proxy=(CacheUserProxy)(object)users;
  using var cache=new LocalReadCache(library,users);var owner=Guid.NewGuid();int calls=0;
  CatalogPresence Read(Guid id)=>cache.Get(cache.Scope(id),"Movie:42",()=>{calls++;return new CatalogPresence(false);});
  Read(owner);Read(owner);Assert.Equal(1,calls);Read(Guid.NewGuid());Assert.Equal(2,calls);
  proxy.Dto.Policy!.EnableAllFolders=false;Read(owner);Assert.Equal(3,calls);
  events.Change();Read(owner);Assert.Equal(4,calls);
  proxy.Exists=false;Assert.Throws<UnauthorizedAccessException>(()=>Read(owner));
 }
 [Fact]public async Task InFlightCoverageCannotRepopulateTheCurrentGenerationAfterLibraryChange()
 {
  var library=DispatchProxy.Create<ILibraryManager,CacheLibraryProxy>();var events=(CacheLibraryProxy)(object)library;
  var users=DispatchProxy.Create<IUserManager,CacheUserProxy>();using var cache=new LocalReadCache(library,users);var owner=Guid.NewGuid();
  var release=new TaskCompletionSource<CoverageObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
  var first=cache.Coverage(owner,"series",_=>release.Task,default);events.Change();release.SetResult(new(true,false,false,null));await first;
  var current=await cache.Coverage(owner,"series",_=>Task.FromResult(new CoverageObservation(true,true,true,null)),default);Assert.True(current.Complete);
 }
}
public class CacheLibraryProxy:DispatchProxy
{
 readonly Dictionary<string,Delegate?> handlers=new();
 protected override object? Invoke(MethodInfo? method,object?[]? args){var name=method!.Name;if(name.StartsWith("add_")){var key=name[4..];handlers.TryGetValue(key,out var old);handlers[key]=Delegate.Combine(old,(Delegate)args![0]!);return null;}if(name.StartsWith("remove_")){var key=name[7..];handlers[key]=Delegate.Remove(handlers[key],(Delegate)args![0]!);return null;}throw new NotSupportedException(name);}
 public void Change(){if(handlers.TryGetValue("ItemUpdated",out var handler))handler?.DynamicInvoke(null,null);}
}
public class CacheUserProxy:DispatchProxy
{
 public bool Exists=true;public UserDto Dto=new(){Policy=new(){EnableAllFolders=true}};
 protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {"GetUserById"=>Exists?RuntimeHelpers.GetUninitializedObject(method.ReturnType):null,"GetUserDto"=>Dto,_=>throw new NotSupportedException(method.Name)};
}
