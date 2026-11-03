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
 public MediaCard Resolve(Guid owner,Guid id)=>new(id,"catalogued","Example","Movie",["Drama"]);
 public IReadOnlyDictionary<Guid,MediaCard> ResolveMany(Guid owner,IReadOnlyList<Guid> ids)=>ids.ToDictionary(id=>id,id=>Resolve(owner,id));
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
   s.AddSingleton(store);s.AddSingleton<ICatalogAccess,FixtureCatalog>();s.AddSingleton<MirrorMutationGate>();s.AddSingleton<MirrorProtection>();s.AddControllers(o=>o.Filters.AddService<MirrorProtection>(int.MinValue)).AddApplicationPart(typeof(CollectionsController).Assembly).AddApplicationPart(typeof(NativeFixtureController).Assembly);
  }).Configure(a=>{a.UseRouting();a.UseAuthentication();a.UseAuthorization();a.UseEndpoints(e=>e.MapControllers());})).Start();
  _server=_host.GetTestServer();
 }
 private HttpClient Client(Guid? id=null){var c=_server.CreateClient();if(id.HasValue)c.DefaultRequestHeaders.Add("X-Test-User",id.ToString());return c;}
 private async Task<Guid> Create(HttpClient c){var r=await c.PostAsJsonAsync("/PLM/v1/me/collections",new{Name="Private"});r.EnsureSuccessStatusCode();var d=await r.Content.ReadFromJsonAsync<JsonElement>();return d.GetProperty("id").GetGuid();}
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
 public void Dispose(){_host.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(_path,true);}
}

[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class NativeFixtureController : Microsoft.AspNetCore.Mvc.ControllerBase
{
 [Microsoft.AspNetCore.Mvc.HttpGet("Playlists/{playlistId:guid}")]public IActionResult Read(Guid playlistId)=>Ok();
 [Microsoft.AspNetCore.Mvc.HttpPost("Playlists/{playlistId:guid}")]public IActionResult Update(Guid playlistId)=>NoContent();
 [Microsoft.AspNetCore.Mvc.HttpDelete("Items")]public IActionResult Bulk([Microsoft.AspNetCore.Mvc.FromQuery]string ids)=>NoContent();
 [Microsoft.AspNetCore.Mvc.HttpPost("Items/Delete")]public IActionResult BulkBody(BulkRequest body)=>NoContent();
}
public sealed record BulkRequest(Guid[] Ids);
