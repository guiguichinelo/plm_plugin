using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Plugin.Plm;
using Microsoft.Extensions.DependencyInjection;
using Plm.Core;
using Xunit;
namespace Plm.Api.Tests;

public sealed partial class HttpPrivacyTests
{
 [Fact]public async Task LibraryShelvesAndFacetsHandleEmptyOwnersAndFilterBeforePaging()
 {
  using var a=Client(_a);using var b=Client(_b);var store=_host.Services.GetRequiredService<LibraryStore>();var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  var scope=Guid.NewGuid();var film=Guid.NewGuid();var outside=Guid.NewGuid();catalog.Scopes[scope]=[film];
  var url=$"/PLM/v1/me/library-sections?libraryId={scope}";
  var empty=await a.GetFromJsonAsync<JsonElement>(url);Assert.Empty(empty.GetProperty("sections").EnumerateArray());Assert.Equal(0,empty.GetProperty("totalItems").GetInt32());
  var facets=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/facets?libraryId={scope}");Assert.Empty(facets.GetProperty("genres").EnumerateArray());
  var list=await Create(a);var other=await Create(a);store.Add(_a,list,film,0);store.Add(_a,other,outside,0);catalog.EnsureFavorite(_a,film);
  var rows=await a.GetFromJsonAsync<JsonElement>(url+"&limit=1&rowLimit=1");Assert.Equal(2,rows.GetProperty("totalRecordCount").GetInt32());Assert.True(rows.GetProperty("hasMore").GetBoolean());Assert.Equal("liked",rows.GetProperty("sections")[0].GetProperty("id").GetString());
  var second=await a.GetFromJsonAsync<JsonElement>(url+"&startIndex=1&limit=1");Assert.Equal(list.ToString("N"),second.GetProperty("sections")[0].GetProperty("id").GetString());
  var foreign=await b.GetFromJsonAsync<JsonElement>(url);Assert.Empty(foreign.GetProperty("sections").EnumerateArray());
  Assert.Equal(HttpStatusCode.NotFound,(await b.GetAsync($"/PLM/v1/me/facets?list={list}&libraryId={scope}")).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync(url+"&rowLimit=21")).StatusCode);
  catalog.HiddenScopes.Add((_a,scope));Assert.Equal(HttpStatusCode.NotFound,(await a.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await a.GetAsync($"/PLM/v1/me/facets?libraryId={scope}")).StatusCode);
 }
 [Fact]public async Task SavedUnionIsOwnerScopedDeduplicatedAndPaginatedAfterLibraryFilter()
 {
  using var a=Client(_a);using var b=Client(_b);var list=await Create(a);var other=await Create(b);
  var store=_host.Services.GetRequiredService<LibraryStore>();var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  var scope=Guid.NewGuid();var film=Guid.NewGuid();var listOnly=Guid.NewGuid();var animeFilm=Guid.NewGuid();var privateToB=Guid.NewGuid();
  catalog.Scopes[scope]=[film,listOnly,privateToB];catalog.EnsureFavorite(_a,film);
  store.Add(_a,list,film,0);store.Add(_a,list,animeFilm,1);store.Add(_a,list,listOnly,2);store.Add(_b,other,privateToB,0);
  var url=$"/PLM/v1/me/saved?libraryId={scope}&groupVersions=true&limit=1";
  var first=await a.GetFromJsonAsync<JsonElement>(url);Assert.Equal(2,first.GetProperty("totalRecordCount").GetInt32());Assert.Single(first.GetProperty("items").EnumerateArray());
  var second=await a.GetFromJsonAsync<JsonElement>(url+"&startIndex=1");
  Assert.NotEqual(first.GetProperty("items")[0].GetProperty("itemId").GetGuid(),second.GetProperty("items")[0].GetProperty("itemId").GetGuid());
  var all=await a.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/saved?libraryId={scope}&groupVersions=true");
  Assert.Equal(new[]{film,listOnly}.Order(),all.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("itemId").GetGuid()).Order());
  Assert.Equal(3,store.Get(_a,list).Items.Count);Assert.Single(catalog.Favorites);Assert.DoesNotContain((_a,listOnly),catalog.Favorites);
  var ownB=await b.GetFromJsonAsync<JsonElement>($"/PLM/v1/me/saved?libraryId={scope}");Assert.Single(ownB.GetProperty("items").EnumerateArray());Assert.Equal(privateToB,ownB.GetProperty("items")[0].GetProperty("itemId").GetGuid());
 }
 [Fact]public async Task CollectionScopeAppliesBeforeGroupedPaginationAndRejectsHiddenRoots()
 {
  using var a=Client(_a);var list=await Create(a);var store=_host.Services.GetRequiredService<LibraryStore>();var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  var scope=Guid.NewGuid();var movie=Guid.NewGuid();var anime=Guid.NewGuid();catalog.Scopes[scope]=[movie];store.Add(_a,list,anime,0);store.Add(_a,list,movie,1);
  var url=$"/PLM/v1/me/collections/{list}?libraryId={scope}&groupVersions=true&limit=1&sort=custom";
  var result=await a.GetFromJsonAsync<JsonElement>(url);Assert.Equal(1,result.GetProperty("totalRecordCount").GetInt32());Assert.Equal(movie,result.GetProperty("items")[0].GetProperty("itemId").GetGuid());
  catalog.HiddenScopes.Add((_a,scope));Assert.Equal(HttpStatusCode.NotFound,(await a.GetAsync(url)).StatusCode);
  Assert.Equal(HttpStatusCode.NotFound,(await a.GetAsync($"/PLM/v1/me/saved?libraryId={scope}")).StatusCode);
  using var anonymous=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/PLM/v1/me/saved")).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync("/PLM/v1/me/saved?libraryId="+Guid.Empty)).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await a.GetAsync("/PLM/v1/me/saved?limit=201")).StatusCode);
 }
 [Fact]public async Task AllSavedCollapsesVersionAliasesAndPreservesTheUnderlyingLists()
 {
  using var a=Client(_a);var one=await Create(a);var two=await Create(a);var store=_host.Services.GetRequiredService<LibraryStore>();var catalog=(FixtureCatalog)_host.Services.GetRequiredService<ICatalogAccess>();
  var primary=Guid.NewGuid();var alternate=Guid.NewGuid();catalog.Aliases[alternate]=primary;catalog.EnsureFavorite(_a,primary);store.Add(_a,one,alternate,0);store.Add(_a,two,primary,0);
  var result=await a.GetFromJsonAsync<JsonElement>("/PLM/v1/me/saved?groupVersions=true");Assert.Equal(1,result.GetProperty("totalRecordCount").GetInt32());Assert.Equal(primary,result.GetProperty("items")[0].GetProperty("displayItemId").GetGuid());
  Assert.Equal(alternate,Assert.Single(store.Get(_a,one).Items).ItemId);Assert.Equal(primary,Assert.Single(store.Get(_a,two).Items).ItemId);
 }
}
