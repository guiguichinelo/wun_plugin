using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.Wun;

/// <summary>Bounded values and shared, time-limited loads. Caller cancellation only cancels its wait.</summary>
public sealed class BoundedAsyncCache<T>(int capacity=512,TimeProvider? clock=null):IDisposable
{
 readonly MemoryCache values=new(new MemoryCacheOptions{SizeLimit=capacity});
 readonly ConcurrentDictionary<string,Lazy<Task<T>>> flights=new();
 readonly TimeProvider time=clock??TimeProvider.System;
 sealed record Entry(T Value,DateTimeOffset Until);
 public async Task<T> Get(string key,TimeSpan ttl,Func<CancellationToken,Task<T>> load,CancellationToken ct)
 {
  ct.ThrowIfCancellationRequested();
  if(values.TryGetValue(key,out Entry? hit)&&hit!.Until>time.GetUtcNow())return hit.Value;
  var flight=flights.GetOrAdd(key,_=>new Lazy<Task<T>>(()=>Load(key,ttl,load),LazyThreadSafetyMode.ExecutionAndPublication));
  return await flight.Value.WaitAsync(ct);
 }
 async Task<T> Load(string key,TimeSpan ttl,Func<CancellationToken,Task<T>> load)
 {
  try{
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
   var value=await load(timeout.Token).WaitAsync(timeout.Token);
   // Null/empty provider responses are not retained as successful metadata.
   if(value is not null)values.Set(key,new Entry(value,time.GetUtcNow()+ttl),new MemoryCacheEntryOptions{Size=1,AbsoluteExpirationRelativeToNow=ttl});
   return value;
  }finally{flights.TryRemove(key,out _);}
 }
 public void Dispose()=>values.Dispose();
}
