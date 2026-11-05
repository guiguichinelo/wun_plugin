using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Wun;
using Wun.Core;
using Xunit;

public sealed partial class AccessTests
{
    [Fact]
    public async Task MetricsRequireAdministratorAndCountUniqueInterestWithoutFollowing()
    {
        using var anonymous=Client();
        using var regular=Client(user);
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/WUN/v1/admin/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await regular.GetAsync("/WUN/v1/admin/metrics")).StatusCode);
        var movie=store.Create(user,new(MediaKind.Movie,"42","Fixture movie"),[]);
        var series=store.Create(other,new(MediaKind.Series,"43","Fixture series",1),[]);
        store.Support(movie.Id,other,true);store.Support(movie.Id,other,true);
        store.Follow(series.Id,user,true);
        store.Decide(series.Id,1,user,Decision.Deferred,"Public fixture","Private fixture",DateTimeOffset.UtcNow.AddDays(-2));
        using var admin=Client(user,true);
        var result=await admin.GetFromJsonAsync<JsonElement>("/WUN/v1/admin/metrics");
        Assert.Equal(2,result.GetProperty("total").GetInt32());
        Assert.Equal(1,result.GetProperty("pending").GetInt32());
        Assert.Equal(1,result.GetProperty("deferred").GetInt32());
        Assert.Equal(1,result.GetProperty("movies").GetInt32());
        Assert.Equal(1,result.GetProperty("series").GetInt32());
        Assert.Equal(3,result.GetProperty("supports").GetInt32());
        Assert.Equal(2,result.GetProperty("interestedUsers").GetInt32());
        Assert.Equal(1,result.GetProperty("reviewsDue").GetInt32());
        Assert.Equal(movie.Id,result.GetProperty("mostWanted")[0].GetProperty("id").GetInt64());
        Assert.DoesNotContain("Private fixture",result.ToString());
        store.Decide(series.Id,2,user,Decision.Deferred,"","",DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal(0,store.Metrics().ReviewsDue);
        store.Decide(series.Id,3,user,Decision.Approved,"","",DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(0,store.Metrics().ReviewsDue);
    }

    [Fact]
    public void ConfigurationDefaultsAreBoundedAndPageIsEmbedded()
    {
        var config=new Configuration();
        Assert.Equal(30,config.PageSize);Assert.Equal("popular",config.AdministratorOrder);
        config.PageSize=10000;Assert.Equal(100,config.PageSize);
        config.PageSize=-1;Assert.Equal(10,config.PageSize);
        config.AdministratorOrder="injected";Assert.Equal("popular",config.AdministratorOrder);
        config.AdministratorOrder="newest";Assert.Equal("newest",config.AdministratorOrder);
        Assert.NotNull(typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Wun.Web.config.html"));
    }
}
