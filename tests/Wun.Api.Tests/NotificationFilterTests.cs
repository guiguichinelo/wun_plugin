using System.Net.Http.Json;
using System.Text.Json;
using Wun.Core;
using Xunit;
public sealed partial class AccessTests
{
 [Fact]public async Task ReadFilterAppliesBeforePaginationAndCannotExposeOtherRecipients()
 {
  store.Create(user,new(MediaKind.Movie,"42","First"),[other]);
  store.Create(user,new(MediaKind.Movie,"43","Second"),[other]);
  var newest=store.WorkflowNotifications(other)[0];store.MarkNotificationRead(other,newest.Id);
  using var client=Client(other);
  var unread=await client.GetFromJsonAsync<JsonElement>("/WUN/v1/notifications?read=false&limit=1");
  Assert.Equal(1,unread.GetArrayLength());Assert.False(unread[0].GetProperty("read").GetBoolean());
  Assert.NotEqual(newest.Id,unread[0].GetProperty("id").GetInt64());
  var read=await client.GetFromJsonAsync<JsonElement>("/WUN/v1/notifications?read=true&limit=1");
  Assert.Equal(newest.Id,read[0].GetProperty("id").GetInt64());
  using var author=Client(user);Assert.Empty(await author.GetFromJsonAsync<JsonElement[]>("/WUN/v1/notifications?read=true")??[]);
 }
}
