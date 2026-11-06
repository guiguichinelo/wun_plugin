using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Plugin.Wun;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using Wun.Core;
using Xunit;
public sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync(){if(!Guid.TryParse(Request.Headers["X-Test-User"],out var id))return Task.FromResult(AuthenticateResult.NoResult());var claims=new List<Claim>{new("Jellyfin-UserId",id.ToString())};if(Request.Headers["X-Test-Admin"]=="true")claims.Add(new("admin","true"));return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));}
}
public sealed class FixtureAccess:IMetadataAccess,IUserAccess,ICatalogLookup
{
 public int ResolveCalls {get;private set;}
 public Guid Denied {get;set;}
 public bool Obtained {get;set;}
 public bool SpecificEpisodeObtained {get;set;}
 public Guid CatalogDenied {get;set;}
 public Guid LastCatalogOwner {get;private set;}
 public int? LastSeason {get;private set;}
 public int? LastEpisode {get;private set;}
 public Task<SearchCard[]> Search(Guid id,string q,string k,CancellationToken ct,SearchFilters? filters=null)=>Task.FromResult(id==Denied?Array.Empty<SearchCard>():new[]{new SearchCard(k,"42","Verified title",2020)});
 public Task<BaseItem?> Resolve(Guid id,string kind,string tmdb,CancellationToken ct){ResolveCalls++;return Task.FromResult<BaseItem?>(id==Denied?null:new Movie{Name="Verified title"});}
 public CatalogPresence Find(Guid owner,string kind,IReadOnlyDictionary<string,string> ids,int? season=null,int? episode=null){LastCatalogOwner=owner;LastSeason=season;LastEpisode=episode;var obtained=Obtained&&owner!=Denied&&owner!=CatalogDenied&&(episode==null||SpecificEpisodeObtained);return new(obtained,obtained?Guid.Parse("12345678-1234-1234-1234-123456789abc"):null);}
 public HashSet<Guid> AdministratorUsers {get;}=[];
 public bool Administrator(Guid id)=>AdministratorUsers.Contains(id);
 public Guid[] Administrators()=>AdministratorUsers.ToArray();
 public UserSummary? Summary(Guid id)=>new(id,"Fixture account",false,null);
}
public sealed partial class AccessTests:IDisposable
{
 readonly string path=Path.Combine(Path.GetTempPath(),"wun-api-"+Guid.NewGuid());readonly IHost host;readonly RequestStore store;readonly FixtureAccess access=new();readonly Guid user=Guid.NewGuid(),other=Guid.NewGuid();
 public AccessTests(){store=new(Path.Combine(path,"wun.db"));host=new HostBuilder().ConfigureWebHost(w=>w.UseTestServer().ConfigureServices(s=>{s.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});s.AddAuthorization(o=>o.AddPolicy("RequiresElevation",p=>p.RequireClaim("admin","true")));s.AddSingleton(store);s.AddSingleton<IMetadataAccess>(access);s.AddSingleton<IUserAccess>(access);s.AddSingleton<ICatalogLookup>(access);s.AddControllers().AddApplicationPart(typeof(RequestsController).Assembly);}).Configure(a=>{a.UseRouting();a.UseAuthentication();a.UseAuthorization();a.UseEndpoints(e=>e.MapControllers());})).Start();}
 HttpClient Client(Guid? id=null,bool admin=false){var c=host.GetTestClient();if(id!=null)c.DefaultRequestHeaders.Add("X-Test-User",id.ToString());if(admin)c.DefaultRequestHeaders.Add("X-Test-Admin","true");return c;}
 [Fact]public async Task AnonymousCannotReadOrCreate(){using var c=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/WUN/v1/requests")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42"})).StatusCode);}
 [Fact]public async Task NativeAdministratorPolicyProtectsDecisionsAndNotes(){var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[]);using var c=Client(user);Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync($"/WUN/v1/admin/requests/{card.Id}/history")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await c.PutAsJsonAsync($"/WUN/v1/admin/requests/{card.Id}",new{Revision=1,Decision=1})).StatusCode);}
 [Fact]public async Task CreateIgnoresForgedOwnerAndTitle(){using var c=Client(user);var r=await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42",Title="Forged",Owner=other});r.EnsureSuccessStatusCode();Assert.Equal("Verified title",Assert.Single(store.List().Items).Selection.Title);Assert.Equal(0,store.List(follower:other).Total);Assert.Equal(1,store.List(follower:user).Total);}
 [Fact]public async Task DeniedMetadataCannotBeReadCreatedOrFollowed(){var card=store.Create(user,new(MediaKind.Movie,"42","Secret"),[]);access.Denied=other;using var c=Client(other);var r=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/requests");Assert.Equal(0,r.GetProperty("items").GetArrayLength());Assert.Equal(HttpStatusCode.NotFound,(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42"})).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/WUN/v1/requests/{card.Id}/history")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await c.PutAsJsonAsync($"/WUN/v1/requests/{card.Id}/follow",new{})).StatusCode);}
 [Fact]public async Task PublicHistoryRedactsNotesAndStaleUpdatesConflict(){var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[]);using var admin=Client(user,true);(await admin.PutAsJsonAsync($"/WUN/v1/admin/requests/{card.Id}",new{Revision=1,Decision=2,Message="Later",InternalNote="Private source"})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await admin.PutAsJsonAsync($"/WUN/v1/admin/requests/{card.Id}",new{Revision=1,Decision=1})).StatusCode);using var c=Client(other);var r=await c.GetAsync($"/WUN/v1/requests/{card.Id}/history");Assert.DoesNotContain("Private source",await r.Content.ReadAsStringAsync());Assert.True(r.Headers.CacheControl!.NoStore);}
 [Fact]public async Task InvalidScopeAndUnboundedPaginationAreRejected(){using var c=Client(user);Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42",Season=1})).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/WUN/v1/requests?limit=1000")).StatusCode);}
 [Fact]public async Task NotificationReadsAreScopedToAuthenticatedIdentity(){var card=store.Create(user,new(MediaKind.Movie,"42","Title"),[]);store.Follow(card.Id,other,true);store.Decide(card.Id,1,user,Decision.Approved,"","");using var c=Client(user);(await c.PostAsJsonAsync("/WUN/v1/notifications/read",new{Owner=other})).EnsureSuccessStatusCode();Assert.All(store.Notifications(user),n=>Assert.True(n.Read));Assert.All(store.Notifications(other),n=>Assert.False(n.Read));}
 [Fact]public async Task ObtainedMovieIsRejectedEvenWithForgedOwner(){access.Obtained=true;using var c=Client(user);var r=await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42",Owner=other});Assert.Equal(HttpStatusCode.Conflict,r.StatusCode);Assert.Equal(user,access.LastCatalogOwner);Assert.Equal(0,store.List().Total);Assert.Contains("already_obtained",await r.Content.ReadAsStringAsync());}
 [Fact]public async Task ObtainedSeriesCanStillRequestMissingEpisodes(){access.Obtained=true;using var c=Client(user);(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Series",TmdbId="42",Season=2,Episode=3})).EnsureSuccessStatusCode();var r=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/requests");Assert.Equal(2,access.LastSeason);Assert.Equal(3,access.LastEpisode);Assert.False(r.GetProperty("items")[0].GetProperty("catalog").GetProperty("obtained").GetBoolean());}
 [Theory][InlineData("yearFrom=2020&yearTo=1900")][InlineData("minRating=11")][InlineData("obtained=hidden")][InlineData("yearFrom=5000")]
 public async Task InvalidAdvancedFiltersAreRejected(string query){using var c=Client(user);Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync("/WUN/v1/search?query=Example&"+query)).StatusCode);}
 [Fact]public async Task AlreadyObtainedEpisodeIsRejected(){access.Obtained=true;access.SpecificEpisodeObtained=true;using var c=Client(user);Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Series",TmdbId="42",Season=2,Episode=3})).StatusCode);Assert.Equal(0,store.List().Total);}
 [Fact]public async Task APrivateCatalogMatchIsNotExposedToAnotherUser(){access.Obtained=true;access.CatalogDenied=other;var request=store.Create(user,new(MediaKind.Movie,"42","Public provider title"),[]);using var c=Client(other);var mine=await c.GetFromJsonAsync<JsonElement>("/WUN/v1/requests");Assert.Equal(0,mine.GetProperty("items").GetArrayLength());var item=await c.GetFromJsonAsync<JsonElement>($"/WUN/v1/requests/{request.Id}");Assert.False(item.GetProperty("catalog").GetProperty("obtained").GetBoolean());Assert.Equal(JsonValueKind.Null,item.GetProperty("catalog").GetProperty("itemId").ValueKind);Assert.Equal(other,access.LastCatalogOwner);(await c.PostAsJsonAsync("/WUN/v1/requests",new{Kind="Movie",TmdbId="42"})).EnsureSuccessStatusCode();}
 public void Dispose(){host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(path,true);}
}
