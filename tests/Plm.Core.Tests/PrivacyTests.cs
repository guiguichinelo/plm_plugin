using Plm.Core;
using Xunit;
namespace Plm.Core.Tests;
public sealed class PrivacyTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"plm-tests-"+Guid.NewGuid().ToString("N"));
    private readonly Guid _owner=Guid.NewGuid(),_other=Guid.NewGuid();
    private readonly LibraryStore _store;
    public PrivacyTests(){_store=new(Path.Combine(_directory,"plm.db"));}
    [Fact] public void OtherUserCannotReadOrMutateCollection()
    {
        var c=_store.Create(_owner,"Private");Assert.Empty(_store.List(_other));
        Assert.Throws<CollectionMissingException>(()=>_store.Get(_other,c.Id));
        Assert.Throws<CollectionMissingException>(()=>_store.Add(_other,c.Id,Guid.NewGuid(),0));
        Assert.Throws<CollectionMissingException>(()=>_store.SetDeleted(_other,c.Id,0,true));
        Assert.Equal(0,_store.Get(_owner,c.Id).Collection.Revision);
    }
    [Fact] public void WatchLaterIsIdempotentAndProtected()
    {var a=_store.Create(_owner,"Later",true);var b=_store.Create(_owner,"Later",true);Assert.Equal(a.Id,b.Id);Assert.Throws<ArgumentException>(()=>_store.SetDeleted(_owner,a.Id,0,true));}
    [Fact] public void StaleEditsDoNotOverwriteAnotherSession()
    {var c=_store.Create(_owner,"A");_store.Add(_owner,c.Id,Guid.NewGuid(),0);Assert.Throws<RevisionConflictException>(()=>_store.Rename(_owner,c.Id,"B",0));Assert.Equal("A",_store.Get(_owner,c.Id).Collection.Name);}
    [Fact] public void RemovalIsRecoverableAndDoesNotAffectAnotherList()
    {var a=_store.Create(_owner,"A");var b=_store.Create(_owner,"B");var item=Guid.NewGuid();_store.Add(_owner,a.Id,item,0);_store.Add(_owner,b.Id,item,0);_store.Remove(_owner,a.Id,item,1);Assert.Empty(_store.Get(_owner,a.Id).Items);Assert.Single(_store.Get(_owner,b.Id).Items);_store.RestoreSelection(_owner,a.Id,item,2);Assert.Single(_store.Get(_owner,a.Id).Items);}
    [Fact] public void DuplicateAndReorderValidation()
    {var a=_store.Create(_owner,"A");var x=Guid.NewGuid();var y=Guid.NewGuid();_store.Add(_owner,a.Id,x,0);_store.Add(_owner,a.Id,x,1);Assert.Single(_store.Get(_owner,a.Id).Items);_store.Add(_owner,a.Id,y,2);Assert.Throws<ArgumentException>(()=>_store.Reorder(_owner,a.Id,[x,x],3));_store.Reorder(_owner,a.Id,[y,x],3);Assert.Equal(y,_store.Get(_owner,a.Id).Items[0].ItemId);}
    [Fact] public void BackupRestoresOwnerScopedData()
    {var a=_store.Create(_owner,"Backup");_store.Add(_owner,a.Id,Guid.NewGuid(),0);var path=Path.Combine(_directory,"restore.db");_store.Backup(path);var restored=new LibraryStore(path);Assert.Single(restored.Get(_owner,a.Id).Items);Assert.Empty(restored.List(_other));}
    [Fact] public void CleanupDoesNotConsiderUnavailableActiveSelections()
    {var a=_store.Create(_owner,"A");_store.Add(_owner,a.Id,Guid.NewGuid(),0);Assert.Equal(new MaintenancePreview(0,0),_store.PreviewCleanup(DateTimeOffset.UtcNow.AddYears(10)));}
    [Fact] public void ImportIsAtomicAndNeverCopiesAnotherOwner()
    {var a=_store.Create(_owner,"A");_store.Add(_owner,a.Id,Guid.NewGuid(),0);var copy=_store.Import(_other,_store.Export(_owner));Assert.Single(copy);Assert.NotEqual(a.Id,copy[0].Id);Assert.Single(_store.Get(_other,copy[0].Id).Items);Assert.Throws<CollectionMissingException>(()=>_store.Get(_owner,copy[0].Id));Assert.Throws<ArgumentException>(()=>_store.Import(_other,new(1,[new("Valid",false,[]),new("",false,[])])));Assert.Single(_store.List(_other));}
    [Fact] public void CleanupPreservesMirrorOwnershipUntilMirrorHasBeenRemoved()
    {var a=_store.Create(_owner,"A");var playlist=Guid.NewGuid();_store.SaveMirror(a.Id,"Video",playlist,0);_store.SetDeleted(_owner,a.Id,0,true);Assert.Equal(0,_store.Cleanup(DateTimeOffset.UtcNow.AddDays(31)).ExpiredCollections);Assert.Single(_store.Mirrors());_store.ForgetMirror(playlist);Assert.Equal(1,_store.Cleanup(DateTimeOffset.UtcNow.AddDays(31)).ExpiredCollections);}
    [Fact]public void MissingAccountRetentionUsesGuidAndDoesNotResetTheDeadline()
    {var c=_store.Create(_owner,"Retained");var now=DateTimeOffset.UtcNow;Assert.Equal(now,_store.ObserveOwner(_owner,false,now));Assert.Equal(now,_store.ObserveOwner(_owner,false,now.AddDays(1)));_store.PurgeMissingOwner(_owner,now.AddDays(29));Assert.Single(_store.List(_owner));_store.PurgeMissingOwner(_owner,now.AddDays(31));Assert.Empty(_store.List(_owner));Assert.Empty(_store.List(_other));}
    [Fact]public void ReturningAccountCancelsItsPurgeDeadline()
    {_store.Create(_owner,"Retained");var now=DateTimeOffset.UtcNow;_store.ObserveOwner(_owner,false,now);Assert.Null(_store.ObserveOwner(_owner,true,now.AddDays(1)));_store.PurgeMissingOwner(_owner,now.AddDays(31));Assert.Single(_store.List(_owner));}
    [Fact]public void MembershipsExcludeOtherOwnersDeletedListsAndRemovedSelections()
    {var item=Guid.NewGuid();var mine=_store.Create(_owner,"Mine");var other=_store.Create(_other,"Private");_store.Add(_owner,mine.Id,item,0);_store.Add(_other,other.Id,item,0);var own=Assert.Single(_store.Memberships(_owner,item));Assert.Equal(mine.Id,own.Collection.Id);Assert.True(own.Contains);Assert.Equal(1,own.Collection.Revision);_store.Remove(_owner,mine.Id,item,1);Assert.False(Assert.Single(_store.Memberships(_owner,item)).Contains);_store.SetDeleted(_owner,mine.Id,2,true);Assert.Empty(_store.Memberships(_owner,item));Assert.True(Assert.Single(_store.Memberships(_other,item)).Contains);}
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(_directory,true);}
}
