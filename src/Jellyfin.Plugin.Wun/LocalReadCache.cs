using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.Wun;

/// <summary>Short-lived, user/policy scoped observations. Never caches workflow state or authorization.</summary>
public sealed class LocalReadCache:IDisposable
{
 readonly ILibraryManager library;
 readonly IUserManager users;
 readonly MemoryCache values=new(new MemoryCacheOptions{SizeLimit=1024});
 readonly BoundedAsyncCache<CoverageObservation> coverage=new(512);
 long generation;
 public LocalReadCache(ILibraryManager library,IUserManager users)
 {
  this.library=library;this.users=users;
  library.ItemAdded+=Changed;library.ItemUpdated+=Changed;library.ItemRemoved+=Changed;
 }
 void Changed(object? sender,ItemChangeEventArgs args)=>Interlocked.Increment(ref generation);
 public string Scope(Guid owner)
 {
  var user=users.GetUserById(owner)??throw new UnauthorizedAccessException();
  var dto=users.GetUserDto(user);
  var policy=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto.Policy))));
  return owner+":"+policy+":"+Interlocked.Read(ref generation);
 }
 public T Get<T>(string scope,string key,Func<T> load) where T:class
 {
  var full=scope+":"+key;
  if(values.TryGetValue(full,out T? found))return found!;
  var result=load();values.Set(full,result,new MemoryCacheEntryOptions{Size=1,AbsoluteExpirationRelativeToNow=TimeSpan.FromSeconds(10)});return result;
 }
 public Task<CoverageObservation> Coverage(Guid owner,string key,Func<CancellationToken,Task<CoverageObservation>> load,CancellationToken ct)
  =>coverage.Get(Scope(owner)+":"+key,TimeSpan.FromSeconds(10),load,ct);
 public void Dispose(){library.ItemAdded-=Changed;library.ItemUpdated-=Changed;library.ItemRemoved-=Changed;values.Dispose();coverage.Dispose();}
}
