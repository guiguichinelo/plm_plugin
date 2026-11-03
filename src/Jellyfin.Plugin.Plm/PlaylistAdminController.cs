using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plm.Core;

namespace Jellyfin.Plugin.Plm;
[ApiController][Authorize(Policy="RequiresElevation")][Route("PLM/v1/admin/playlists")]
public sealed class PlaylistAdminController(ILibraryManager library,LibraryStore store):ControllerBase
{
 [HttpPost("clear")]
 public IActionResult Clear()
 {
  if(Plugin.Instance?.Configuration.DisableNativePlaylists!=true)return Conflict(new{error="disable_playlists_first"});
  var count=0;
  while(true){var items=library.GetItemsResult(new InternalItemsQuery{IncludeItemTypes=[BaseItemKind.Playlist],Recursive=true,Limit=100}).Items;
   if(items.Count==0)break;
   foreach(var item in items){if(item is not Playlist playlist)throw new InvalidOperationException("Unexpected item type.");library.DeleteItem(playlist,new DeleteOptions{DeleteFileLocation=false,DeleteFromExternalProvider=false});store.ForgetMirror(playlist.Id);count++;}
  }
  return Ok(new{removed=count});
 }
}
