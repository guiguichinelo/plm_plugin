using Plm.Core;
using Xunit;
namespace Plm.Core.Tests;

public sealed class ResilienceTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"plm-resilience-"+Guid.NewGuid().ToString("N"));
    private readonly LibraryStore _lists;
    private readonly CatalogResilienceStore _memory;
    private readonly Guid _owner=Guid.NewGuid(),_other=Guid.NewGuid();
    public ResilienceTests()
    {
        var path=Path.Combine(_dir,"plm.db");_lists=new(path);_memory=new(path);
    }
    private static CatalogIdentity Movie(Guid? id=null,string provider="tt001") =>
        new(id??Guid.NewGuid(),"Movie","Example","/media/example.mkv",2020,new(){["imdb"]=provider,["tmdb"]="123"});
    [Fact] public void RebuildRebindsAllListsPreservingOrderDatesAndOwners()
    {
        var old=Movie();var next=old with {ItemId=Guid.NewGuid(),Path="/media/new.mkv"};
        var first=_lists.Create(_owner,"First");var second=_lists.Create(_owner,"Second");var foreign=_lists.Create(_other,"Other");
        var before=Guid.NewGuid();_lists.Add(_owner,first.Id,before,0);_lists.Add(_owner,first.Id,old.ItemId,1);
        _lists.Add(_owner,second.Id,old.ItemId,0);_lists.Add(_other,foreign.Id,old.ItemId,0);
        var previous=_lists.Get(_owner,first.Id).Items.Last();
        var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,true);
        r=Assert.Single(_memory.References(_owner));_memory.MarkMissing(_owner,old.ItemId);
        Assert.True(_memory.Rebind(r,next));Assert.False(_memory.Rebind(r,next));
        var list=_lists.Get(_owner,first.Id);Assert.Equal(new[]{before,next.ItemId},list.Items.Select(s=>s.ItemId));
        Assert.Equal(previous.AddedAt,list.Items.Last().AddedAt);Assert.Equal(previous.Position,list.Items.Last().Position);
        Assert.Equal(next.ItemId,Assert.Single(_lists.Get(_owner,second.Id).Items).ItemId);
        Assert.Equal(old.ItemId,Assert.Single(_lists.Get(_other,foreign.Id).Items).ItemId);
        Assert.Throws<RevisionConflictException>(()=>_lists.Rename(_owner,first.Id,"Stale",2));
        var pending=Assert.Single(_memory.References(_owner));Assert.True(pending.PendingFavorite);Assert.True(pending.Favorite);
    }
    [Fact] public void ExplicitUnlikeSurvivesRebuildAndLateBaseline()
    {
        var old=Movie();var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,true);
        _memory.RecordFavorite(_owner,r.ReferenceId,false);
        _memory.RecordFavorite(_owner,r.ReferenceId,true,seedOnly:true);
        r=Assert.Single(_memory.References(_owner));Assert.False(r.Favorite);
        Assert.True(_memory.Rebind(r,old with {ItemId=Guid.NewGuid()}));
        var pending=Assert.Single(_memory.References(_owner));Assert.True(pending.PendingFavorite);Assert.False(pending.Favorite);
        Assert.True(_memory.IsPendingFavorite(pending));
        _memory.RecordFavorite(_owner,r.ReferenceId,true);
        Assert.False(_memory.IsPendingFavorite(pending));
        _memory.AcknowledgeFavorite(pending); // stale acknowledgement cannot clear a newer action
        Assert.True(Assert.Single(_memory.References(_owner)).Favorite);
    }
    [Fact] public void DeletedSelectionsAndListsAreNeverResurrected()
    {
        var old=Movie();var next=old with {ItemId=Guid.NewGuid()};
        var c=_lists.Create(_owner,"Deleted");_lists.Add(_owner,c.Id,old.ItemId,0);_lists.Remove(_owner,c.Id,old.ItemId,1);
        _lists.SetDeleted(_owner,c.Id,2,true);
        var deleted=_lists.Get(_owner,c.Id,true);var r=_memory.Remember(_owner,old);
        Assert.True(_memory.Rebind(r,next));Assert.Empty(_lists.List(_owner));
        var after=_lists.Get(_owner,c.Id,true);Assert.Equal(deleted.Collection.DeletedAt,after.Collection.DeletedAt);
        Assert.Equal(Assert.Single(deleted.Items).DeletedAt,Assert.Single(after.Items).DeletedAt);
    }
    [Fact] public void ConflictOrUnidentifiedSamePathCannotStealIntent()
    {
        var old=Movie();var r=_memory.Remember(_owner,old);
        Assert.False(_memory.Rebind(r,old with {ItemId=Guid.NewGuid(),Providers=[]}));
        Assert.False(_memory.Rebind(r,Movie(provider:"tt999")));
        Assert.False(_memory.Rebind(r,old with {ItemId=Guid.NewGuid(),Kind="Series"}));
        var retained=Assert.Single(_memory.References(_owner)).Identity;
        Assert.Equal(old.ItemId,retained.ItemId);Assert.Equal(old.Providers,retained.Providers);
    }
    [Fact] public void DestinationUserActionOrDeletionTombstoneBlocksStaleMerge()
    {
        var old=Movie();var next=old with {ItemId=Guid.NewGuid()};
        var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,true);
        var fresh=_memory.Remember(_owner,next);_memory.RecordFavorite(_owner,fresh.ReferenceId,false);
        Assert.False(_memory.Rebind(_memory.References(_owner).First(x=>x.ReferenceId==r.ReferenceId),next));
        Assert.False(_memory.References(_owner).First(x=>x.ReferenceId==fresh.ReferenceId).Favorite);
        var c=_lists.Create(_other,"Conflict");_lists.Add(_other,c.Id,old.ItemId,0);_lists.Add(_other,c.Id,next.ItemId,1);
        _lists.Remove(_other,c.Id,next.ItemId,2);
        Assert.False(_memory.Rebind(_memory.Remember(_other,old),next));
        Assert.Equal(old.ItemId,Assert.Single(_lists.Get(_other,c.Id).Items).ItemId);
    }
    [Fact] public void MissingStateAndIntentSurviveRestartAndBackup()
    {
        var old=Movie();var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,true);
        _memory.MarkMissing(_owner,old.ItemId);
        var backup=Path.Combine(_dir,"backup.db");_lists.Backup(backup);
        var after=new CatalogResilienceStore(backup);var row=Assert.Single(after.References(_owner));
        Assert.True(row.Missing);Assert.True(row.Favorite);
        after.Reappeared(row,old with {Providers=[]});Assert.True(Assert.Single(after.References()).Missing);
        after.Reappeared(row,old);Assert.True(Assert.Single(after.References()).PendingFavorite);
        var pending=Assert.Single(after.References());after.AcknowledgeFavorite(pending);after.Reappeared(pending,old);
        Assert.False(Assert.Single(after.References()).PendingFavorite);
    }
    [Fact] public void LateMetadataEnrichesWithoutErasingKnownProviders()
    {
        var old=Movie();var blank=old with {Providers=[]};var r=_memory.Remember(_owner,blank);
        _memory.Remember(_owner,old);_memory.Remember(_owner,blank);
        var row=Assert.Single(_memory.References());Assert.Equal(r.ReferenceId,row.ReferenceId);
        Assert.Equal(2,row.Identity.Providers.Count);
        _memory.Remember(_owner,Movie(old.ItemId,"tt999"));
        Assert.Equal("tt001",Assert.Single(_memory.References()).Identity.Providers["imdb"]);
    }
    [Fact] public void EpisodeIdentityIncludesSeriesAndSeasonIncludingSpecials()
    {
        var first=new CatalogIdentity(Guid.NewGuid(),"Episode","Special",null,2020,new(){["series:tmdb"]="123"},0,1);
        Assert.True(first.Matches(first with {ItemId=Guid.NewGuid()}));
        Assert.False(first.Matches(first with {Season=1}));
        Assert.False(first.Matches(first with {Episode=2}));
        Assert.False(first.Matches(first with {Providers=new(){["tmdb"]="123"}}));
        Assert.False(first.Matches(first with {Providers=new(){["series:tmdb"]="456"}}));
    }
    [Fact] public void RepeatedRebuildRetainsIdentityAndUnlikeTombstone()
    {
        var old=Movie();var c=_lists.Create(_owner,"Queue");_lists.Add(_owner,c.Id,old.ItemId,0);
        var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,false);
        for(var index=0;index<5;index++)
        {
            r=Assert.Single(_memory.References(_owner));var next=r.Identity with {ItemId=Guid.NewGuid()};
            Assert.True(_memory.Rebind(r,next));Assert.Equal(next.ItemId,Assert.Single(_lists.Get(_owner,c.Id).Items).ItemId);
            Assert.False(Assert.Single(_memory.References(_owner)).Favorite);
        }
        Assert.Equal(r.ReferenceId,_memory.Remember(_owner,old).ReferenceId);
    }
    [Fact] public void ListOnlySelectionRebindNeverCreatesNativeFavoriteIntent()
    {
        var old=Movie();var c=_lists.Create(_owner,"Only a list");_lists.Add(_owner,c.Id,old.ItemId,0);
        var r=_memory.Remember(_owner,old);
        Assert.True(_memory.Rebind(r,old with {ItemId=Guid.NewGuid()}));
        var after=Assert.Single(_memory.References(_owner));Assert.Null(after.Favorite);Assert.False(after.PendingFavorite);
    }
    [Fact] public void ConcurrentFavoriteChangePreventsStaleRebindWithoutPartialListEdits()
    {
        var old=Movie();var c=_lists.Create(_owner,"Concurrent");_lists.Add(_owner,c.Id,old.ItemId,0);
        var r=_memory.Remember(_owner,old);_memory.RecordFavorite(_owner,r.ReferenceId,true);
        var before=_lists.Get(_owner,c.Id);
        Assert.False(_memory.Rebind(r,old with {ItemId=Guid.NewGuid()}));
        var after=_lists.Get(_owner,c.Id);Assert.Equal(before.Collection.Revision,after.Collection.Revision);
        Assert.Equal(old.ItemId,Assert.Single(after.Items).ItemId);
    }
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(_dir,true);}
}
