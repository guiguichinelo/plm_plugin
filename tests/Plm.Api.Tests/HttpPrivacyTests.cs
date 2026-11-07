using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Plugin.Plm;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Plm.Core;
using Xunit;

namespace Plm.Api.Tests;

// This authentication scheme exists only in the test assembly. Production relies
// exclusively on Jellyfin authentication and its Jellyfin-UserId claim.
public sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
 protected override Task<AuthenticateResult> HandleAuthenticateAsync()
 {
  if(!Guid.TryParse(Request.Headers["X-Test-User"],out var id))return Task.FromResult(AuthenticateResult.NoResult());
  var claims=new List<Claim>{new("Jellyfin-UserId",id.ToString())};
  if(Request.Headers["X-Test-Admin"]=="true")claims.Add(new("admin","true"));
  return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
 }
}
public sealed class FixtureCatalog : ICatalogAccess
{
 public readonly HashSet<(Guid Owner, Guid Item)> Favorites = [];
 public readonly HashSet<Guid> Unavailable = [];
 public readonly Dictionary<Guid,Guid> Aliases = [];
 public IReadOnlyDictionary<Guid,Guid> Displays(Guid owner,IReadOnlyList<Guid> ids)=>ids.Distinct().Where(id=>!Unavailable.Contains(id)).ToDictionary(id=>id,id=>Aliases.GetValueOrDefault(id,id));
 public void EnsureFavorite(Guid owner, Guid id) => Favorites.Add((owner, id));
 public MediaCard Resolve(Guid owner,Guid id)=>Unavailable.Contains(id)?new(id,"unavailable",null,null,[]):new(id,"catalogued","Example","Movie",["Drama"]);
 public IReadOnlyDictionary<Guid,MediaCard> ResolveMany(Guid owner,IReadOnlyList<Guid> ids)=>ids.Where(id=>!Unavailable.Contains(id)).ToDictionary(id=>id,id=>Resolve(owner,id));
 public CatalogPage Query(Guid owner,IReadOnlyList<Guid> ids,CatalogFilter filter,int start,int limit){var result=ids.Select(id=>Resolve(owner,id)).Where(m=>filter.Genres.Length==0||filter.Genres.Any(g=>m.Genres.Contains(g))).ToArray();return new(result.Length,result.Skip(start).Take(limit).ToArray());}
}

public sealed class HttpPrivacyTests : IDisposable
{
 private readonly string _path=Path.Combine(Path.GetTempPath(),"plm-api-"+Guid.NewGuid().ToString("N"));
 private readonly TestServer _server;
 private readonly IHost _host;
 private readonly Guid _a=Guid.NewGuid(),_b=Guid.NewGuid();
 public HttpPrivacyTests()
 {
  var store=new LibraryStore(Path.Combine(_path,"plm.db"));
  _host=new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().ConfigureServices(s=>{
   s.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuth>("fixture",_=>{});
   s.AddAuthorization(o=>o.AddPolicy("RequiresElevation",p=>p.RequireClaim("admin","true")));
   s.AddSingleton(store);s.AddSingleton(new CatalogResilienceStore(Path.Combine(_path,"plm.db")));s.AddSingleton<ICatalogAccess,FixtureCatalog>();s.AddSingleton<ProgressBarrier>();s.AddSingleton<MirrorMutationGate>();s.AddSingleton<MirrorProtection>();s.AddSingleton<NativeFavoriteIntent>();s.AddControllers(o=>{o.Filters.AddService<MirrorProtection>(int.MinValue);o.Filters.AddService<NativeFavoriteIntent>(int.MinValue+1);}).AddApplicationPart(typeof(CollectionsController).Assembly).AddApplicationPart(typeof(NativeFixtureController).Assembly);
  }).Configure(a=>{a.UseRouting();a.UseAuthentication();a.UseAuthorization();a.UseEndpoints(e=>e.MapControllers());})).Start();
  _server=_host.GetTestServer();
 }
 private HttpClient Client(Guid? id=null){var c=_server.CreateClient();if(id.HasValue)c.DefaultRequestHeaders.Add("X-Test-User",id.ToString());return c;}
 private async Task<Guid> Create(HttpClient c){var r=await c.PostAsJsonAsync("/PLM/v1/me/collections",new{Name="Private"});r.EnsureSuccessStatusCode();var d=await r.Content.ReadFromJsonAsync<JsonElement>();return d.GetProperty("id").GetGuid();}
 [Fact]public async Task PartialNativeUpdatesAreNotMistakenForUnlikesAndMetricsRequireAdmin()
 {
  using var c=Client(_a);
  foreach(var (body,expected) in new[]{(new MediaBrowser.Model.Dto.UpdateUserItemDataDto{Played=true},false),(new MediaBrowser.Model.Dto.UpdateUserItemDataDto{IsFavorite=false},true),(new MediaBrowser.Model.Dto.UpdateUserItemDataDto{IsFavorite=true},true),(new MediaBrowser.Model.Dto.UpdateUserItemDataDto{PlayCount=3},false)})
  {
   var response=await c.PostAsJsonAsync("/Fixture/UserData",body);response.EnsureSuccessStatusCode();
   Assert.Equal(expected,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("explicitFavorite").GetBoolean());
  }
  var favorite=await c.PostAsJsonAsync("/Fixture/FavoriteItems/"+Guid.NewGuid(),new{});
  Assert.True((await favorite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("explicitFavorite").GetBoolean());
  Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/PLM/v1/admin/resilience")).StatusCode);
  c.DefaultRequestHeaders.Add("X-Test-Admin","true");
  var result=await c.GetFromJsonAsync<JsonElement>("/PLM/v1/admin/resilience");
  Assert.False(result.GetProperty("enabled").GetBoolean());Assert.Equal(0,result.GetProperty("protectedReferences").GetInt32());Assert.Equal(0,result.GetProperty("autoMatchableReferences").GetInt32());
 }
 [Fact]public async Task MembershipReadDoesNotLikeAndIncludesQueueWithoutLeakingOwners()
 {
  using var a=Client(_a);using var b=Client(_b);var first=await Create(a);var second=await Create(a);var item=Guid.NewGuid();
  (await a.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/items",new{Revision=0,ItemId=item})).EnsureSuccessStatusCode();
  (await a.PostAsJsonAsync($"/PLM/v1/me/collections/{second}/items",new{Revision=0,ItemId=item})).EnsureSuccessStatusCode();
  var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();Assert.Empty(catalog.Favorites);
  var path=$"/PLM/v1/me/items/memberships?ids={item}";
  Assert.Single(await a.GetFromJsonAsync<Guid[]>(path)??[]);Assert.Empty(await b.GetFromJsonAsync<Guid[]>(path)??[]);
  (await a.DeleteAsync($"/PLM/v1/me/collections/{first}/items/{item}?revision=1")).EnsureSuccessStatusCode();Assert.Single(await a.GetFromJsonAsync<Guid[]>(path)??[]);
  (await a.DeleteAsync($"/PLM/v1/me/collections/{second}/items/{item}?revision=1")).EnsureSuccessStatusCode();Assert.Empty(await a.GetFromJsonAsync<Guid[]>(path)??[]);
  (await a.PutAsJsonAsync($"/PLM/v1/me/watch-later/items/{item}",new{Saved=true})).EnsureSuccessStatusCode();Assert.Single(await a.GetFromJsonAsync<Guid[]>(path)??[]);Assert.Empty(catalog.Favorites);
  Assert.Equal(HttpStatusCode.NotFound,(await a.PostAsync("/PLM/v1/me/favorites/reconcile",null)).StatusCode);
 }
 [Fact]public async Task WatchQueueAndRejectedWritesDoNotLikeTitles()
 {
  using var a=Client(_a);using var b=Client(_b);var id=await Create(a);var item=Guid.NewGuid();
  var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync($"/PLM/v1/me/collections/{id}/items",new{Revision=0,ItemId=item})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync($"/PLM/v1/me/collections/{id}/items",new{Revision=99,ItemId=item})).StatusCode);
  (await a.PutAsJsonAsync($"/PLM/v1/me/watch-later/items/{item}",new{Saved=true})).EnsureSuccessStatusCode();
  Assert.Empty(catalog.Favorites);
 }
 [Fact]public async Task AnonymousCannotEnumerateOrCreate()
 {using var c=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/PLM/v1/me/collections")).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/PLM/v1/me/collections",new{Name="A"})).StatusCode);}
 [Fact]public async Task CrossAccountIdsCannotBeUsedForReadsOrWrites()
 {
  using var a=Client(_a);using var b=Client(_b);var id=await Create(a);
  Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/PLM/v1/me/collections/{id}")).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await b.PutAsJsonAsync($"/PLM/v1/me/collections/{id}",new{Revision=0,Name="Stolen"})).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync($"/PLM/v1/me/collections/{id}/items",new{Revision=0,ItemId=Guid.NewGuid()})).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await b.DeleteAsync($"/PLM/v1/me/collections/{id}?revision=0")).StatusCode);
  var list=await b.GetFromJsonAsync<JsonElement>("/PLM/v1/me/collections");Assert.Equal(0,list.GetArrayLength());
 }
 [Fact]public async Task StaleClientGetsConflictAndAdminDataIsProtected()
 {
  using var c=Client(_a);var id=await Create(c);
  Assert.Equal(HttpStatusCode.NoContent,(await c.PutAsJsonAsync($"/PLM/v1/me/collections/{id}",new{Revision=0,Name="New"})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync($"/PLM/v1/me/collections/{id}",new{Revision=0,Name="Old"})).StatusCode);
  Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/PLM/v1/admin/maintenance/preview")).StatusCode);
 }
 [Fact]public async Task NativeMirrorCannotBeSharedDeletedOrReadByAnotherAccount()
 {
  var store=_host.Services.GetRequiredService<LibraryStore>();var c=store.Create(_a,"Mirror");var playlist=Guid.NewGuid();store.SaveMirror(c.Id,"Video",playlist,0);
  using var a=Client(_a);using var b=Client(_b);
  Assert.Equal(HttpStatusCode.OK,(await a.GetAsync($"/Playlists/{playlist}")).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/Playlists/{playlist}")).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync($"/Playlists/{playlist}",new{IsPublic=true})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.DeleteAsync($"/Items?ids={playlist}")).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync("/Items/Delete",new{Ids=new[]{playlist}})).StatusCode);
  Assert.Equal(HttpStatusCode.NoContent,(await a.PostAsJsonAsync($"/Playlists/{Guid.NewGuid()}",new{IsPublic=true})).StatusCode);
 }
 [Fact]public async Task EmptySelectionFromAnotherPluginDoesNotFailOrBypassMirrorPrivacy()
 {
  var store=_host.Services.GetRequiredService<LibraryStore>();var collection=store.Create(_a,"Mirror");var playlist=Guid.NewGuid();store.SaveMirror(collection.Id,"Video",playlist,0);
  Assert.Empty(store.FindMirrors([Guid.Empty]));
  Assert.Equal(playlist,Assert.Single(store.FindMirrors([Guid.Empty,playlist,Guid.Empty])).PlaylistId);
  using var a=Client(_a);using var b=Client(_b);
  Assert.Equal(HttpStatusCode.NoContent,(await a.GetAsync($"/Fixture/SourceCollection/{Guid.Empty}")).StatusCode);
  Assert.Equal(HttpStatusCode.NoContent,(await a.PostAsJsonAsync("/Items/Delete",new{Ids=new[]{Guid.Empty,Guid.NewGuid()}})).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync("/Items/Delete",new{Ids=new[]{Guid.Empty,playlist}})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync("/Items/Delete",new{Ids=new[]{Guid.Empty,playlist}})).StatusCode);
 }
 [Fact]public async Task IndependentProgressWritesCanEnterTogetherWithoutWeakeningMirrorPrivacy()
 {
  using var a=Client(_a);using var b=Client(_b);
  var barrier=_host.Services.GetRequiredService<ProgressBarrier>();
  var first=a.PostAsJsonAsync($"/UserItems/{Guid.NewGuid()}/UserData",new{PlaybackPositionTicks=10});
  await barrier.First.Task.WaitAsync(TimeSpan.FromSeconds(3));
  var second=b.PostAsJsonAsync($"/UserItems/{Guid.NewGuid()}/UserData",new{PlaybackPositionTicks=20});
  try { await barrier.Both.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
  finally { barrier.Release.TrySetResult(); }
  Assert.Equal(HttpStatusCode.OK,(await first).StatusCode);Assert.Equal(HttpStatusCode.OK,(await second).StatusCode);
  var store=_host.Services.GetRequiredService<LibraryStore>();var c=store.Create(_a,"Mirror");var playlist=Guid.NewGuid();store.SaveMirror(c.Id,"Video",playlist,0);
  Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync($"/UserItems/{playlist}/UserData",new{PlaybackPositionTicks=30})).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await a.PostAsJsonAsync($"/UserItems/{playlist}/UserData",new{PlaybackPositionTicks=40})).StatusCode);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync($"/Playlists/{playlist}",new{IsPublic=true})).StatusCode);
 }
 [Fact]public async Task SlowRemoteImageDoesNotBlockTaskCancellationOrProgress()
 {
  using var a=Client(_a);using var b=Client(_b);
  var barrier=_host.Services.GetRequiredService<ProgressBarrier>();
  var image=a.PostAsync($"/Fixture/RemoteImages/{Guid.NewGuid()}",null);
  await barrier.First.Task.WaitAsync(TimeSpan.FromSeconds(3));
  try
  {
   var cancel=await b.DeleteAsync($"/Fixture/Tasks/{Guid.NewGuid()}").WaitAsync(TimeSpan.FromSeconds(3));
   Assert.Equal(HttpStatusCode.NoContent,cancel.StatusCode);
   var progress=b.PostAsJsonAsync($"/UserItems/{Guid.NewGuid()}/UserData",new{PlaybackPositionTicks=10});
   await barrier.Both.Task.WaitAsync(TimeSpan.FromSeconds(3));
   barrier.Release.TrySetResult();Assert.Equal(HttpStatusCode.OK,(await progress).StatusCode);
  }
  finally { barrier.Release.TrySetResult(); }
  Assert.Equal(HttpStatusCode.NoContent,(await image).StatusCode);
 }
 [Fact]public async Task CreationBarrierDoesNotBlockTaskCancellationButStillProtectsNewMirrors()
 {
  using var a=Client(_a);var gate=_host.Services.GetRequiredService<MirrorMutationGate>();
  var store=_host.Services.GetRequiredService<LibraryStore>();var collection=store.Create(_a,"Mirror");var item=Guid.NewGuid();
  await gate.Semaphore.WaitAsync();
  Task<HttpResponseMessage>? deletion=null;
  try
  {
   var cancel=await a.DeleteAsync($"/Fixture/Tasks/{Guid.NewGuid()}").WaitAsync(TimeSpan.FromSeconds(3));
   Assert.Equal(HttpStatusCode.NoContent,cancel.StatusCode);
   deletion=a.DeleteAsync($"/Fixture/Items/{item}");
   Assert.NotSame(deletion,await Task.WhenAny(deletion,Task.Delay(100)));
   store.SaveMirror(collection.Id,"Video",item,0);
  }
  finally { gate.Semaphore.Release(); }
  Assert.Equal(HttpStatusCode.Conflict,(await deletion!).StatusCode);
  using var b=Client(_b);Assert.Equal(HttpStatusCode.NotFound,(await b.DeleteAsync($"/Fixture/Items/{item}")).StatusCode);
 }
 [Fact]public async Task UnavailableSelectionsCanBeRemovedRestoredAndDoNotReappearAfterRebind()
 {
  using var a=Client(_a);using var b=Client(_b);var first=await Create(a);var second=await Create(a);var item=Guid.NewGuid();
  var store=_host.Services.GetRequiredService<LibraryStore>();store.Add(_a,first,item,0);store.Add(_a,second,item,0);
  ((FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>()).Unavailable.Add(item);
  var memory=_host.Services.GetRequiredService<CatalogResilienceStore>();
  var identity=new CatalogIdentity(item,"Movie","Remembered title","/fixture/film.mkv",2020,new(){["imdb"]="tt1234567"},null,null);
  var remembered=memory.Remember(_a,identity);
  var page=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{first}");
  var media=page.GetProperty("items")[0].GetProperty("media");Assert.Equal("unavailable",media.GetProperty("status").GetString());Assert.Equal("Remembered title",media.GetProperty("name").GetString());
  Assert.Equal(HttpStatusCode.NotFound,(await b.DeleteAsync($"/PLM/v1/me/collections/{first}/items/{item}?revision=1")).StatusCode);
  Assert.Equal(HttpStatusCode.NoContent,(await a.DeleteAsync($"/PLM/v1/me/collections/{first}/items/{item}?revision=1")).StatusCode);
  Assert.Empty(store.Get(_a,first).Items);Assert.Single(store.Get(_a,second).Items);
  Assert.Equal(HttpStatusCode.Conflict,(await a.DeleteAsync($"/PLM/v1/me/collections/{first}/items/{item}?revision=1")).StatusCode);
  var replacement=Guid.NewGuid();Assert.True(memory.Rebind(remembered,identity with{ItemId=replacement}));
  Assert.Empty(store.Get(_a,first).Items);Assert.NotNull(Assert.Single(store.Get(_a,first,true).Items).DeletedAt);
  Assert.Equal(HttpStatusCode.NoContent,(await a.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/items/{replacement}/restore",new{Revision=store.Get(_a,first).Collection.Revision})).StatusCode);
  Assert.Single(store.Get(_a,first).Items);
  var other=store.Create(_b,"Other");store.Add(_b,other.Id,item,0);
  var hidden=await b.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{other.Id}");
  Assert.Equal(JsonValueKind.Null,hidden.GetProperty("items")[0].GetProperty("media").GetProperty("name").ValueKind);
 }
 [Fact]public async Task AdminBackupIsConsistentButAdminCannotReadOtherOwnersCollections()
 {
  using var a=Client(_a);using var admin=Client(_b);admin.DefaultRequestHeaders.Add("X-Test-Admin","true");var id=await Create(a);
  Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/PLM/v1/me/collections/{id}")).StatusCode);
  var response=await admin.GetAsync("/PLM/v1/admin/backup");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var bytes=await response.Content.ReadAsByteArrayAsync();Assert.Equal("SQLite format 3\0",System.Text.Encoding.ASCII.GetString(bytes,0,16));
  Assert.Equal(HttpStatusCode.Forbidden,(await a.GetAsync("/PLM/v1/admin/backup")).StatusCode);
 }
 [Fact]public async Task PaginationRejectsUnboundedRequests()
 {
  using var a=Client(_a);var id=await Create(a);
  Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync($"/PLM/v1/me/collections/{id}?limit=201")).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync($"/PLM/v1/me/collections/{id}?startIndex=-1")).StatusCode);
 }
 [Fact]public async Task MembershipPickerCannotDiscloseAnotherAccountsLists()
 {
  using var a=Client(_a);using var b=Client(_b);var id=await Create(a);var item=Guid.NewGuid();
  (await a.PostAsJsonAsync($"/PLM/v1/me/collections/{id}/items",new{Revision=0,ItemId=item})).EnsureSuccessStatusCode();
  var own=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/items/{item}/memberships");Assert.Contains(own.EnumerateArray(),x=>x.GetProperty("collection").GetProperty("id").GetGuid()==id&&x.GetProperty("contains").GetBoolean());
  var other=await b.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/items/{item}/memberships");Assert.DoesNotContain(other.EnumerateArray(),x=>x.GetProperty("collection").GetProperty("id").GetGuid()==id);Assert.All(other.EnumerateArray(),x=>Assert.False(x.GetProperty("contains").GetBoolean()));
 }
 [Fact]public async Task ArtworkRoutesAreAuthenticatedOwnerScopedAndValidated()
 {
  using var a=Client(_a);using var b=Client(_b);using var anonymous=Client();var id=await Create(a);var url=$"/PLM/v1/me/lists/{id}/thumbnail";
  byte[] jpeg=[0xff,0xd8,0xff,0xc0,0,11,8,0,32,0,32,1,1,0x11,0,0xff,0xd9];
  Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(url)).StatusCode);
  Assert.Equal(HttpStatusCode.OK,(await a.PutAsJsonAsync(url,new{jpeg})).StatusCode);var response=await a.GetAsync(url);Assert.True(response.Headers.CacheControl!.NoStore);Assert.Contains("data:image/jpeg;base64,",await response.Content.ReadAsStringAsync());
  Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await b.PutAsJsonAsync(url,new{jpeg})).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await b.DeleteAsync(url)).StatusCode);
  Assert.Equal(0,(await b.GetFromJsonAsync<JsonElement>("/PLM/v1/me/thumbnails")).GetArrayLength());Assert.Equal(HttpStatusCode.BadRequest,(await a.PutAsJsonAsync(url,new{jpeg=new byte[]{1,2,3}})).StatusCode);
 }
 [Fact]public async Task ServerPagesAndClockUpdatesRemainIndependentAcrossOwners()
 {
  using var a=Client(_a);using var b=Client(_b);var store=_host.Services.GetRequiredService<LibraryStore>();var ids=Enumerable.Range(0,105).Select(_=>Guid.NewGuid()).ToArray();var c=Assert.Single(store.Import(_a,new(1,[new("Large",false,ids)])));
  var page=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{c.Id}?startIndex=80&limit=40&genres=Drama&sort=name");Assert.Equal(105,page.GetProperty("totalRecordCount").GetInt32());Assert.Equal(25,page.GetProperty("items").GetArrayLength());
  Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/PLM/v1/me/collections/{c.Id}?sort=name")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync($"/PLM/v1/me/collections/{c.Id}?limit=201")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync($"/PLM/v1/me/collections/{c.Id}?types=Playlist")).StatusCode);
  var clock=$"/PLM/v1/me/watch-later/items/{ids[0]}";Assert.Equal(HttpStatusCode.OK,(await a.PutAsJsonAsync(clock,new{saved=true})).StatusCode);Assert.Equal(HttpStatusCode.OK,(await a.PutAsJsonAsync(clock,new{saved=true})).StatusCode);
  Assert.Single(await a.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/watch-later/membership?ids={ids[0]}")??[]);Assert.Empty(await b.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/watch-later/membership?ids={ids[0]}")??[]);Assert.Equal(HttpStatusCode.BadRequest,(await a.PutAsJsonAsync(clock,new{})).StatusCode);
 }
 [Fact]public async Task PublicLocalizationResourcesAreBoundedAndEmbedded()
 {
  using var c=Client();var index=await c.GetFromJsonAsync<JsonElement>("/PLM/web/locales/index.json");Assert.Equal(64,index.GetProperty("locales").EnumerateObject().Count());
  var english=await c.GetFromJsonAsync<JsonElement>("/PLM/web/locales/en-us.json");Assert.Equal("Search",english.GetProperty("PlmSearch").GetString());
  Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/PLM/web/locales/ar.json")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/PLM/web/locales/unknown.json")).StatusCode);
 }
 [Theory][InlineData(false)][InlineData(true)]
 public async Task VersionGroupsPreserveSelectionsAndRemoveAtomically(bool split)
 {
  using var a=Client(_a);using var b=Client(_b);var first=await Create(a);var second=await Create(a);var original=Guid.NewGuid();var primary=Guid.NewGuid();
  var store=_host.Services.GetRequiredService<LibraryStore>();var fixture=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  fixture.Aliases[original]=primary;store.Add(_a,first,original,0);store.Add(_a,first,primary,1);store.Add(_a,second,original,0);
  var page=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{first}?groupVersions=true&limit=1");
  Assert.Equal(1,page.GetProperty("totalRecordCount").GetInt32());Assert.Equal(2,page.GetProperty("items")[0].GetProperty("selectionIds").GetArrayLength());
  var raw=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{first}");Assert.Equal(2,raw.GetProperty("totalRecordCount").GetInt32());
  var saved=await a.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/items/memberships?groupVersions=true&ids={primary}");Assert.Contains(primary,saved!);
  Assert.Empty(await b.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/items/memberships?groupVersions=true&ids={primary}")??[]);
  var body=new{Revision=2,DisplayItemId=primary,SelectionIds=new[]{original,primary}};
  Assert.Equal(HttpStatusCode.NotFound,(await b.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/version-groups/remove",body)).StatusCode);
  if(split)fixture.Aliases.Clear();
  var response=await a.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/version-groups/remove",body);
  Assert.Equal(split?HttpStatusCode.Conflict:HttpStatusCode.OK,response.StatusCode);
  if(split){Assert.Equal(2,store.Get(_a,first).Items.Count);return;}
  Assert.Empty(store.Get(_a,first).Items);Assert.Equal(2,store.Get(_a,first,true).Items.Count);Assert.Single(store.Get(_a,second).Items);Assert.Empty(fixture.Favorites);
  Assert.Equal(HttpStatusCode.Conflict,(await a.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/version-groups/remove",body)).StatusCode);
  (await a.PostAsJsonAsync($"/PLM/v1/me/collections/{first}/version-groups/restore",new{Revision=3,DisplayItemId=primary,SelectionIds=new[]{original,primary}})).EnsureSuccessStatusCode();
  fixture.Aliases.Clear();var separated=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{first}?groupVersions=true");Assert.Equal(2,separated.GetProperty("totalRecordCount").GetInt32());
 }
 [Fact]public async Task GroupAddAndWatchAreIdempotentAcrossVersions()
 {
  using var a=Client(_a);var id=await Create(a);var old=Guid.NewGuid();var primary=Guid.NewGuid();var fixture=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();fixture.Aliases[old]=primary;
  var store=_host.Services.GetRequiredService<LibraryStore>();store.Add(_a,id,old,0);
  (await a.PostAsJsonAsync($"/PLM/v1/me/collections/{id}/items?groupVersions=true",new{Revision=1,ItemId=primary})).EnsureSuccessStatusCode();Assert.Single(store.Get(_a,id).Items);Assert.Equal(1,store.Get(_a,id).Collection.Revision);
  (await a.PutAsJsonAsync($"/PLM/v1/me/watch-later/items/{old}?groupVersions=true",new{saved=true})).EnsureSuccessStatusCode();
  var saved=await a.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/watch-later/membership?groupVersions=true&ids={primary}");Assert.Contains(primary,saved!);
  (await a.PutAsJsonAsync($"/PLM/v1/me/watch-later/items/{primary}?groupVersions=true",new{saved=false})).EnsureSuccessStatusCode();
  Assert.Empty(await a.GetFromJsonAsync<Guid[]>($"/PLM/v1/me/watch-later/membership?groupVersions=true&ids={primary}")??[]);
 }
 [Fact]public async Task VersionGroupsPaginateAfterDeduplicationAndKeepRawOrder()
 {
  using var a=Client(_a);var id=await Create(a);var fixture=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();var store=_host.Services.GetRequiredService<LibraryStore>();
  var ids=Enumerable.Range(0,42).Select(_=>Guid.NewGuid()).ToArray();fixture.Aliases[ids[41]]=ids[0];for(var n=0;n<ids.Length;n++)store.Add(_a,id,ids[n],n);
  var first=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{id}?groupVersions=true&limit=40");var last=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{id}?groupVersions=true&limit=40&startIndex=40");
  Assert.Equal(41,first.GetProperty("totalRecordCount").GetInt32());Assert.Equal(40,first.GetProperty("items").GetArrayLength());Assert.Equal(1,last.GetProperty("items").GetArrayLength());Assert.Equal(2,first.GetProperty("items")[0].GetProperty("selectionIds").GetArrayLength());
  var filtered=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/collections/{id}?groupVersions=true&limit=40&genres=Drama");Assert.Equal(41,filtered.GetProperty("totalRecordCount").GetInt32());
  Assert.Equal(ids,store.Get(_a,id).Items.Select(x=>x.ItemId));
 }
 public void Dispose(){_host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(_path,true);}
}

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class NativeFixtureController : Microsoft.AspNetCore.Mvc.ControllerBase
{
 [Microsoft.AspNetCore.Mvc.HttpGet("Fixture/SourceCollection/{collectionId:guid}")]public IActionResult SourceCollection(Guid collectionId)=>NoContent();
 [Microsoft.AspNetCore.Mvc.HttpPost("Fixture/UserData")]public IActionResult UserData(MediaBrowser.Model.Dto.UpdateUserItemDataDto body)=>
  Ok(new{explicitFavorite=(bool)typeof(NativeFavoriteIntent).GetProperty("HasFavoriteField",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!});
 [Microsoft.AspNetCore.Mvc.HttpPost("Fixture/FavoriteItems/{id:guid}")]public IActionResult Favorite(Guid id)=>
  Ok(new{explicitFavorite=(bool)typeof(NativeFavoriteIntent).GetProperty("HasFavoriteField",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!});
 [Microsoft.AspNetCore.Mvc.HttpGet("Playlists/{playlistId:guid}")]public IActionResult Read(Guid playlistId)=>Ok();
 [Microsoft.AspNetCore.Mvc.HttpPost("Playlists/{playlistId:guid}")]public IActionResult Update(Guid playlistId)=>NoContent();
 [Microsoft.AspNetCore.Mvc.HttpDelete("Items")]public IActionResult Bulk([Microsoft.AspNetCore.Mvc.FromQuery]string ids)=>NoContent();
 [Microsoft.AspNetCore.Mvc.HttpPost("Items/Delete")]public IActionResult BulkBody(BulkRequest body)=>NoContent();
}
public sealed record BulkRequest(Guid[] Ids);

[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class RemoteImageController(ProgressBarrier barrier) : ControllerBase
{
 [HttpPost("Fixture/RemoteImages/{itemId:guid}")]
 public async Task<IActionResult> Download(Guid itemId){await barrier.Enter(HttpContext.RequestAborted);return NoContent();}
}
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class ScheduledTasksController : ControllerBase
{
 [HttpDelete("Fixture/Tasks/{taskId:guid}")]public IActionResult Cancel(Guid taskId)=>NoContent();
}
[ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class ItemsController : ControllerBase
{
 [HttpDelete("Fixture/Items/{itemId:guid}")]public IActionResult Delete(Guid itemId)=>NoContent();
}

public sealed class ProgressBarrier
{
 private int _entered;
 public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
 public TaskCompletionSource Both { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
 public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
 public async Task Enter(CancellationToken cancellation)
 {
  var entered=Interlocked.Increment(ref _entered);First.TrySetResult();if(entered>=2)Both.TrySetResult();
  await Release.Task.WaitAsync(cancellation);
 }
}
[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class UserDataController(ProgressBarrier barrier) : Microsoft.AspNetCore.Mvc.ControllerBase
{
 [Microsoft.AspNetCore.Mvc.HttpPost("UserItems/{itemId:guid}/UserData")]
 public async Task<IActionResult> UpdateItemUserData(Guid itemId,MediaBrowser.Model.Dto.UpdateUserItemDataDto body)
 {await barrier.Enter(HttpContext.RequestAborted);return Ok();}
}
