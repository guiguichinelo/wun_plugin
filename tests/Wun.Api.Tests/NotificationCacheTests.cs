using System.Net.Http.Json;
using System.Text.Json;
using Wun.Core;
using Xunit;

public sealed partial class AccessTests
{
 [Fact]public async Task MultipleEventsPresentEachRequestOnceButNeverCacheItsWorkflowAcrossRequests()
 {
  var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[other]);
  store.Decide(card.Id,1,other,Decision.Approved,"Approved","");
  store.Decide(card.Id,2,other,Decision.Deferred,"Later","");
  using var c=Client(user);
  var before=access.ResolveCalls;var first=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/notifications?read=false");
  Assert.Equal(2,first.GetArrayLength());Assert.Equal(1,access.ResolveCalls-before);
  store.Decide(card.Id,3,other,Decision.Approved,"Ready","");
  var second=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/notifications?read=false");Assert.Equal(3,second.GetArrayLength());
  Assert.All(second.EnumerateArray(),x=>Assert.Equal((int)Decision.Approved,x.GetProperty("request").GetProperty("decision").GetInt32()));
  Assert.Equal(2,access.ResolveCalls-before);
 }
}
