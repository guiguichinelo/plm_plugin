using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plm.Core;
namespace Jellyfin.Plugin.Plm;
public sealed record ArtworkUpload(byte[] Jpeg);
[ApiController][Authorize][Route("PLM/v1/me")]
public sealed class ArtworkController(LibraryStore store):ControllerBase
{
    private Guid Owner=>Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value,out var id)&&id!=Guid.Empty?id:throw new UnauthorizedAccessException();
    private IActionResult Handle(Func<object?> action){Response.Headers.CacheControl="private, no-store";try{return Ok(action());}catch(CollectionMissingException){return NotFound();}catch(UnauthorizedAccessException){return Unauthorized();}catch(ArgumentException){return BadRequest(new{error="invalid_thumbnail"});}}
    [HttpGet("thumbnails")]public IActionResult Index()=>Handle(()=>store.Artworks(Owner));
    [HttpGet("lists/{key}/thumbnail")]public IActionResult Get(string key)=>Handle(()=>{var image=store.Artwork(Owner,key);return image is null?null:new{image.Version,DataUrl="data:image/jpeg;base64,"+Convert.ToBase64String(image.Jpeg)};});
    [HttpPut("lists/{key}/thumbnail")][RequestSizeLimit(750000)]public IActionResult Upload(string key,ArtworkUpload body)=>Handle(()=>{store.SaveArtwork(Owner,key,body.Jpeg);return new{saved=true};});
    [HttpDelete("lists/{key}/thumbnail")]public IActionResult Delete(string key)=>Handle(()=>{store.RemoveArtwork(Owner,key);return null;});
}
