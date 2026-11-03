using Plm.Core;
using Xunit;
namespace Plm.Core.Tests;
public sealed class BrowsingTests : IDisposable
{
 private readonly string _dir=Path.Combine(Path.GetTempPath(),"plm-browse-"+Guid.NewGuid().ToString("N"));
 private readonly LibraryStore _store;
 private readonly Guid _a=Guid.NewGuid(),_b=Guid.NewGuid();
 public BrowsingTests(){_store=new(Path.Combine(_dir,"plm.db"));}
 public static byte[] Jpeg()=>[0xff,0xd8,0xff,0xc0,0,11,8,0,32,0,32,1,1,0x11,0,0xff,0xd9];
 [Fact] public void LargeListUsesBoundedStablePagesAndOwnerScope()
 {
  var ids=Enumerable.Range(0,1503).Select(_=>Guid.NewGuid()).ToArray();var c=Assert.Single(_store.Import(_a,new(1,[new("Large",false,ids)])));
  var page=_store.Page(_a,c.Id,1480,40);Assert.Equal(1503,page.TotalRecordCount);Assert.Equal(ids.Skip(1480),page.Items.Select(i=>i.ItemId));
  Assert.Empty(_store.Page(_a,c.Id,1600,40).Items);Assert.Throws<ArgumentException>(()=>_store.Page(_a,c.Id,0,201));Assert.Throws<CollectionMissingException>(()=>_store.Page(_b,c.Id,0,40));
  Assert.Throws<CollectionMissingException>(()=>_store.SelectionIds(_b,c.Id));Assert.Throws<CollectionMissingException>(()=>_store.SelectionDetails(_b,c.Id,[ids[0]]));
 }
 [Fact] public void ThumbnailsArePrivateValidatedAndIncludedInBackup()
 {
  var c=_store.Create(_a,"Artwork");_store.SaveArtwork(_a,c.Id.ToString(),Jpeg());_store.SaveArtwork(_a,"liked",Jpeg());Assert.Equal(2,_store.Artworks(_a).Count);Assert.Empty(_store.Artworks(_b));Assert.Null(_store.Artwork(_b,"liked"));
  Assert.Throws<CollectionMissingException>(()=>_store.Artwork(_b,c.Id.ToString()));Assert.Throws<CollectionMissingException>(()=>_store.SaveArtwork(_b,c.Id.ToString(),Jpeg()));Assert.Throws<CollectionMissingException>(()=>_store.RemoveArtwork(_b,c.Id.ToString()));
  var backup=Path.Combine(_dir,"backup.db");_store.Backup(backup);Assert.Equal(Jpeg(),new LibraryStore(backup).Artwork(_a,c.Id.ToString())!.Jpeg);
  Assert.Throws<ArgumentException>(()=>_store.SaveArtwork(_a,"liked",new byte[524289]));var tooLarge=Jpeg();tooLarge[9]=9;Assert.Throws<ArgumentException>(()=>_store.SaveArtwork(_a,"liked",tooLarge));Assert.Throws<ArgumentException>(()=>_store.SaveArtwork(_a,"liked",[1,2,3]));
  _store.SetDeleted(_a,c.Id,0,true);Assert.NotNull(_store.Artwork(_a,c.Id.ToString()));_store.Cleanup(DateTimeOffset.UtcNow.AddDays(31));Assert.Single(_store.Artworks(_a));
 }
 [Fact] public void ClockWritesAreIdempotentAndDoNotLoseOtherSelections()
 {
  var x=Guid.NewGuid();var y=Guid.NewGuid();_store.SetWatchMembership(_a,x,true);var c=Assert.Single(_store.List(_a));_store.SetWatchMembership(_a,x,true);Assert.Equal(c.Revision,Assert.Single(_store.List(_a)).Revision);
  _store.SetWatchMembership(_a,y,true);_store.SetWatchMembership(_a,x,false);_store.SetWatchMembership(_a,x,false);Assert.Equal(new[]{y},_store.WatchMemberships(_a,[x,y]));Assert.Empty(_store.WatchMemberships(_b,[x,y]));
  _store.SetWatchMembership(_a,x,true);Assert.Equal(2,_store.WatchMemberships(_a,[x,y]).Count);Assert.Throws<ArgumentException>(()=>_store.WatchMemberships(_a,Enumerable.Repeat(x,201).ToArray()));
 }
 public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(_dir,true);}
}
