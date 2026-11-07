using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Plm.Core;

/// <summary>Owner-scoped persistence. No operation accepts a media filesystem path.</summary>
public sealed class LibraryStore
{
    private readonly string _connectionString;
    public LibraryStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, DefaultTimeout = 10 }.ToString();
        using var db = Open();
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(databasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS schema_version(version INTEGER NOT NULL);
            INSERT INTO schema_version SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_version);
            CREATE TABLE IF NOT EXISTS collections(
              id TEXT PRIMARY KEY, owner TEXT NOT NULL, name TEXT NOT NULL,
              watch_later INTEGER NOT NULL DEFAULT 0, revision INTEGER NOT NULL DEFAULT 0,
              deleted_at TEXT NULL);
            CREATE INDEX IF NOT EXISTS collections_owner ON collections(owner,deleted_at);
            CREATE UNIQUE INDEX IF NOT EXISTS watch_later_owner ON collections(owner) WHERE watch_later=1;
            CREATE TABLE IF NOT EXISTS selections(
              collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
              item_id TEXT NOT NULL, position INTEGER NOT NULL, added_at TEXT NOT NULL, deleted_at TEXT NULL,
              PRIMARY KEY(collection_id,item_id));
            CREATE TABLE IF NOT EXISTS preferences(owner TEXT PRIMARY KEY, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mirrors(
              collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
              media_type TEXT NOT NULL, playlist_id TEXT NOT NULL UNIQUE, revision INTEGER NOT NULL DEFAULT -1,
              PRIMARY KEY(collection_id,media_type));
            CREATE TABLE IF NOT EXISTS jobs(
              collection_id TEXT PRIMARY KEY REFERENCES collections(id) ON DELETE CASCADE,
              attempts INTEGER NOT NULL DEFAULT 0, error TEXT NULL);
            CREATE TABLE IF NOT EXISTS missing_owners(owner TEXT PRIMARY KEY, since TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS selections_page ON selections(collection_id,deleted_at,position,item_id);
            CREATE TABLE IF NOT EXISTS list_artwork(owner TEXT NOT NULL,list_key TEXT NOT NULL,jpeg BLOB NOT NULL,version TEXT NOT NULL,PRIMARY KEY(owner,list_key));
            """;
        command.ExecuteNonQuery();
        using var version = db.CreateCommand(); version.CommandText = "SELECT version FROM schema_version";
        if (Convert.ToInt32(version.ExecuteScalar(), CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("Unsupported PLM database schema; restore a matching backup.");
    }

    private SqliteConnection Open() { var db = new SqliteConnection(_connectionString); db.Open(); return db; }
    private static string Key(Guid value) => value == Guid.Empty ? throw new UnauthorizedAccessException() : value.ToString("N");
    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] parameters)
    {
        var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql;
        foreach (var (name, value) in parameters) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return c;
    }
    private static PersonalCollection ReadCollection(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetBoolean(2), r.GetInt64(3), r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture));
    private static PersonalCollection Require(SqliteConnection db, SqliteTransaction? tx, Guid owner, Guid id, bool deleted = false)
    {
        using var c = Command(db, tx, "SELECT id,name,watch_later,revision,deleted_at FROM collections WHERE id=$id AND owner=$owner" + (deleted ? "" : " AND deleted_at IS NULL"), ("$id", Key(id)), ("$owner", Key(owner)));
        using var r = c.ExecuteReader(); return r.Read() ? ReadCollection(r) : throw new CollectionMissingException();
    }
    public PersonalCollection Create(Guid owner, string name, bool watchLater = false)
    {
        name = name.Trim(); if (name.Length is < 1 or > 120) throw new ArgumentException("Collection name must contain 1 to 120 characters.");
        using var db = Open(); using var tx = db.BeginTransaction();
        var id = Guid.NewGuid();
        using (var c = Command(db, tx, "INSERT OR IGNORE INTO collections(id,owner,name,watch_later) VALUES($id,$owner,$name,$watch)", ("$id", Key(id)), ("$owner", Key(owner)), ("$name", name), ("$watch", watchLater ? 1 : 0))) c.ExecuteNonQuery();
        if (watchLater) { using var c = Command(db, tx, "SELECT id FROM collections WHERE owner=$owner AND watch_later=1", ("$owner", Key(owner))); id = Guid.Parse((string)c.ExecuteScalar()!); }
        var result = Require(db, tx, owner, id, true); tx.Commit(); return result;
    }
    public IReadOnlyList<PersonalCollection> List(Guid owner, bool trash = false)
    {
        using var db = Open(); using var c = Command(db, null, "SELECT id,name,watch_later,revision,deleted_at FROM collections WHERE owner=$owner AND deleted_at IS " + (trash ? "NOT NULL" : "NULL") + " ORDER BY watch_later DESC,name,id", ("$owner", Key(owner)));
        using var r = c.ExecuteReader(); var result = new List<PersonalCollection>(); while (r.Read()) result.Add(ReadCollection(r)); return result;
    }
    public CollectionDetails Get(Guid owner, Guid id, bool trash = false)
    {
        using var db = Open(); var collection = Require(db, null, owner, id, trash); using var c = Command(db, null, "SELECT item_id,position,added_at,deleted_at FROM selections WHERE collection_id=$id" + (trash ? "" : " AND deleted_at IS NULL") + " ORDER BY position,item_id", ("$id", Key(id)));
        using var r = c.ExecuteReader(); var items = new List<Selection>(); while (r.Read()) items.Add(new(Guid.Parse(r.GetString(0)), r.GetInt32(1), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.IsDBNull(3) ? null : DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture))); return new(collection, items);
    }
    private static void Touch(SqliteConnection db, SqliteTransaction tx, Guid owner, Guid id, long revision)
    {
        using var c = Command(db, tx, "UPDATE collections SET revision=revision+1 WHERE id=$id AND owner=$owner AND revision=$revision", ("$id", Key(id)), ("$owner", Key(owner)), ("$revision", revision));
        if (c.ExecuteNonQuery() != 1) throw new RevisionConflictException();
        using var job = Command(db, tx, "INSERT INTO jobs(collection_id) VALUES($id) ON CONFLICT(collection_id) DO UPDATE SET attempts=0,error=NULL", ("$id", Key(id))); job.ExecuteNonQuery();
    }
    public void Add(Guid owner, Guid collectionId, Guid itemId, long revision)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); Require(db, tx, owner, collectionId); Key(itemId); Touch(db, tx, owner, collectionId, revision);
        using var c = Command(db, tx, "INSERT INTO selections(collection_id,item_id,position,added_at) VALUES($id,$item,COALESCE((SELECT MAX(position)+1 FROM selections WHERE collection_id=$id),0),$now) ON CONFLICT(collection_id,item_id) DO UPDATE SET deleted_at=NULL", ("$id", Key(collectionId)), ("$item", Key(itemId)), ("$now", Time(DateTimeOffset.UtcNow))); c.ExecuteNonQuery(); tx.Commit();
    }
    public void Remove(Guid owner, Guid collectionId, Guid itemId, long revision)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); Require(db, tx, owner, collectionId); Touch(db, tx, owner, collectionId, revision);
        using var c = Command(db, tx, "UPDATE selections SET deleted_at=$now WHERE collection_id=$id AND item_id=$item AND deleted_at IS NULL", ("$now", Time(DateTimeOffset.UtcNow)), ("$id", Key(collectionId)), ("$item", Key(itemId))); c.ExecuteNonQuery(); tx.Commit();
    }
    public long ChangeGroup(Guid owner,Guid collection,Guid[] items,long revision,bool restore)
    {
        if(items.Length==0||items.Distinct().Count()!=items.Length)throw new ArgumentException();
        using var db=Open();using var tx=db.BeginTransaction();Require(db,tx,owner,collection);Touch(db,tx,owner,collection,revision);
        foreach(var item in items) { using var cmd=Command(db,tx,"UPDATE selections SET deleted_at=$deleted WHERE collection_id=$id AND item_id=$item",("$deleted",restore?null:Time(DateTimeOffset.UtcNow)),("$id",Key(collection)),("$item",Key(item)));if(cmd.ExecuteNonQuery()!=1)throw new RevisionConflictException(); }
        tx.Commit();return revision+1;
    }
    public void Rename(Guid owner, Guid id, string name, long revision)
    {
        name = name.Trim(); if (name.Length is < 1 or > 120) throw new ArgumentException("Invalid collection name.");
        using var db = Open(); using var tx = db.BeginTransaction(); Require(db, tx, owner, id); Touch(db, tx, owner, id, revision);
        using var c = Command(db, tx, "UPDATE collections SET name=$name WHERE id=$id", ("$name", name), ("$id", Key(id))); c.ExecuteNonQuery(); tx.Commit();
    }
    public void SetDeleted(Guid owner, Guid id, long revision, bool deleted)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); var item = Require(db, tx, owner, id, true);
        if (deleted && item.WatchLater) throw new ArgumentException("The default watch-later collection cannot be deleted.");
        Touch(db, tx, owner, id, revision); using var c = Command(db, tx, "UPDATE collections SET deleted_at=$time WHERE id=$id", ("$time", deleted ? Time(DateTimeOffset.UtcNow) : null), ("$id", Key(id))); c.ExecuteNonQuery(); tx.Commit();
    }
    public void Reorder(Guid owner, Guid id, IReadOnlyList<Guid> order, long revision)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); Require(db, tx, owner, id);
        using var q = Command(db, tx, "SELECT item_id FROM selections WHERE collection_id=$id AND deleted_at IS NULL", ("$id", Key(id))); var existing = new HashSet<Guid>(); using (var r = q.ExecuteReader()) while (r.Read()) existing.Add(Guid.Parse(r.GetString(0)));
        if (order.Count != existing.Count || order.Distinct().Count() != order.Count || !existing.SetEquals(order)) throw new ArgumentException("Order must contain every active selection exactly once.");
        Touch(db, tx, owner, id, revision); for (var i = 0; i < order.Count; i++) { using var c = Command(db, tx, "UPDATE selections SET position=$position WHERE collection_id=$id AND item_id=$item", ("$position", i), ("$id", Key(id)), ("$item", Key(order[i]))); c.ExecuteNonQuery(); }
        tx.Commit();
    }
    public void RestoreSelection(Guid owner, Guid id, Guid item, long revision)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); Require(db, tx, owner, id); Touch(db, tx, owner, id, revision); using var c = Command(db, tx, "UPDATE selections SET deleted_at=NULL WHERE collection_id=$id AND item_id=$item", ("$id", Key(id)), ("$item", Key(item))); c.ExecuteNonQuery(); tx.Commit();
    }
    public string GetPreferences(Guid owner)
    { using var db = Open(); using var c = Command(db, null, "SELECT document FROM preferences WHERE owner=$owner", ("$owner", Key(owner))); return c.ExecuteScalar() as string ?? "{}"; }
    public void SetPreferences(Guid owner, string document)
    { if (document.Length > 16384) throw new ArgumentException("Preferences too large."); using var parsed = System.Text.Json.JsonDocument.Parse(document); using var db = Open(); using var c = Command(db, null, "INSERT INTO preferences(owner,document) VALUES($owner,$doc) ON CONFLICT(owner) DO UPDATE SET document=excluded.document", ("$owner", Key(owner)), ("$doc", document)); c.ExecuteNonQuery(); }
    public void Backup(string destination)
    { using var db = Open(); using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); target.Open(); db.BackupDatabase(target); }
    public MaintenancePreview PreviewCleanup(DateTimeOffset now)
    {
        using var db = Open(); var cut = Time(now.AddDays(-30)); using var a = Command(db, null, "SELECT count(*) FROM collections WHERE deleted_at<$cut", ("$cut", cut)); using var b = Command(db, null, "SELECT count(*) FROM selections WHERE deleted_at<$cut", ("$cut", cut)); return new(Convert.ToInt32(a.ExecuteScalar(), CultureInfo.InvariantCulture), Convert.ToInt32(b.ExecuteScalar(), CultureInfo.InvariantCulture));
    }
    public MaintenancePreview Cleanup(DateTimeOffset now)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); var cut = Time(now.AddDays(-30));
        // Mirror deletion must be completed by the adapter first. Never lose its ownership ledger.
        using var a = Command(db, tx, "DELETE FROM selections WHERE deleted_at<$cut", ("$cut", cut)); var items = a.ExecuteNonQuery();
        using var b = Command(db, tx, "DELETE FROM collections WHERE deleted_at<$cut AND NOT EXISTS(SELECT 1 FROM mirrors WHERE collection_id=collections.id)", ("$cut", cut)); var collections = b.ExecuteNonQuery();
        using(var art=Command(db,tx,"DELETE FROM list_artwork WHERE list_key<>'liked' AND NOT EXISTS(SELECT 1 FROM collections c WHERE c.id=list_artwork.list_key AND c.owner=list_artwork.owner)"))art.ExecuteNonQuery();tx.Commit();return new(collections,items);
    }
    public LibraryExport Export(Guid owner) => new(1, List(owner).Select(c => new ExportCollection(c.Name, c.WatchLater, Get(owner, c.Id).Items.Select(i => i.ItemId).ToArray())).ToArray());
    public IReadOnlyList<PersonalCollection> Import(Guid owner, LibraryExport document)
    {
        if (document.Version != 1 || document.Collections is null || document.Collections.Length > 100) throw new ArgumentException("Invalid export.");
        if (document.Collections.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 120 || c.Items is null || c.Items.Length > 10000 || c.Items.Any(i => i == Guid.Empty))) throw new ArgumentException("Invalid collection.");
        using var db = Open(); using var tx = db.BeginTransaction(); var result = new List<PersonalCollection>();
        foreach (var collection in document.Collections)
        {
            // Imports create separate collections; they never overwrite existing data or trust an exported owner/id.
            var id = Guid.NewGuid(); using (var c = Command(db, tx, "INSERT INTO collections(id,owner,name) VALUES($id,$owner,$name)", ("$id", Key(id)), ("$owner", Key(owner)), ("$name", collection.Name.Trim()))) c.ExecuteNonQuery();
            var index = 0; foreach (var item in collection.Items.Distinct())
            { using var c = Command(db, tx, "INSERT INTO selections(collection_id,item_id,position,added_at) VALUES($id,$item,$position,$now)", ("$id", Key(id)), ("$item", Key(item)), ("$position", index++), ("$now", Time(DateTimeOffset.UtcNow))); c.ExecuteNonQuery(); }
            Touch(db, tx, owner, id, 0); result.Add(Require(db, tx, owner, id));
        }
        tx.Commit(); return result;
    }
    public IReadOnlyList<MirrorSource> Sources()
    {
        using var db = Open(); using var c = Command(db, null, "SELECT id,owner,revision,deleted_at FROM collections ORDER BY id"); using var r = c.ExecuteReader(); var rows = new List<MirrorSource>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetInt64(2), r.IsDBNull(3) ? null : DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture))); return rows;
    }
    public IReadOnlyList<Mirror> Mirrors(Guid? collectionId = null)
    {
        using var db = Open(); using var c = Command(db, null, "SELECT c.id,c.owner,m.media_type,m.playlist_id,m.revision FROM mirrors m JOIN collections c ON c.id=m.collection_id" + (collectionId.HasValue ? " WHERE c.id=$id" : ""), ("$id", collectionId?.ToString("N"))); using var r = c.ExecuteReader(); var rows = new List<Mirror>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), Guid.Parse(r.GetString(3)), r.GetInt64(4))); return rows;
    }
    public void SaveMirror(Guid collectionId, string mediaType, Guid playlistId, long revision)
    {
        using var db = Open(); using var c = Command(db, null, "INSERT INTO mirrors(collection_id,media_type,playlist_id,revision) VALUES($id,$type,$playlist,$revision) ON CONFLICT(collection_id,media_type) DO UPDATE SET playlist_id=excluded.playlist_id,revision=excluded.revision", ("$id", Key(collectionId)), ("$type", mediaType), ("$playlist", Key(playlistId)), ("$revision", revision)); c.ExecuteNonQuery();
    }
    public IReadOnlyList<Mirror> FindMirrors(IEnumerable<Guid> playlistIds)
    {
        using var db = Open(); var result = new List<Mirror>();
        foreach (var batch in playlistIds.Where(id => id != Guid.Empty).Distinct().Chunk(200))
        {
            var parameters = batch.Select((id, index) => ("$p" + index, (object?)Key(id))).ToArray();
            using var c = Command(db, null, "SELECT c.id,c.owner,m.media_type,m.playlist_id,m.revision FROM mirrors m JOIN collections c ON c.id=m.collection_id WHERE m.playlist_id IN (" + string.Join(',', parameters.Select(p => p.Item1)) + ")", parameters);
            using var r = c.ExecuteReader(); while (r.Read()) result.Add(new(Guid.Parse(r.GetString(0)), Guid.Parse(r.GetString(1)), r.GetString(2), Guid.Parse(r.GetString(3)), r.GetInt64(4)));
        }
        return result;
    }
    public void ForgetMirror(Guid playlistId)
    { using var db = Open(); using var c = Command(db, null, "DELETE FROM mirrors WHERE playlist_id=$id", ("$id", Key(playlistId))); c.ExecuteNonQuery(); }
    public DateTimeOffset? ObserveOwner(Guid owner, bool exists, DateTimeOffset now)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var c = Command(db, tx, exists ? "DELETE FROM missing_owners WHERE owner=$owner" : "INSERT OR IGNORE INTO missing_owners(owner,since) VALUES($owner,$now)", ("$owner", Key(owner)), ("$now", Time(now)))) c.ExecuteNonQuery();
        using var q = Command(db, tx, "SELECT since FROM missing_owners WHERE owner=$owner", ("$owner", Key(owner))); var value = q.ExecuteScalar() as string; tx.Commit(); return value is null ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }
    public void PurgeMissingOwner(Guid owner, DateTimeOffset now)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); using var q = Command(db, tx, "SELECT since FROM missing_owners WHERE owner=$owner", ("$owner", Key(owner))); var since = q.ExecuteScalar() as string;
        if (since is null || DateTimeOffset.Parse(since, CultureInfo.InvariantCulture) > now.AddDays(-30)) return;
        using (var c = Command(db, tx, "DELETE FROM collections WHERE owner=$owner AND NOT EXISTS(SELECT 1 FROM mirrors WHERE collection_id=collections.id)", ("$owner", Key(owner)))) c.ExecuteNonQuery();
        using (var c = Command(db, tx, "DELETE FROM preferences WHERE owner=$owner", ("$owner", Key(owner)))) c.ExecuteNonQuery();
        using (var c = Command(db, tx, "DELETE FROM list_artwork WHERE owner=$owner", ("$owner", Key(owner)))) c.ExecuteNonQuery();
        using (var c = Command(db, tx, "DELETE FROM missing_owners WHERE owner=$owner AND NOT EXISTS(SELECT 1 FROM collections WHERE owner=$owner)", ("$owner", Key(owner)))) c.ExecuteNonQuery();tx.Commit();
    }
    public IReadOnlyList<Guid> Owners()
    {
        using var db=Open();using var c=Command(db,null,"SELECT owner FROM collections UNION SELECT owner FROM preferences UNION SELECT owner FROM missing_owners UNION SELECT owner FROM list_artwork");using var r=c.ExecuteReader();var result=new List<Guid>();while(r.Read())result.Add(Guid.Parse(r.GetString(0)));return result;
    }
    public void CompleteJob(Guid id, long revision)
    { using var db = Open(); using var c = Command(db, null, "DELETE FROM jobs WHERE collection_id=$id AND EXISTS(SELECT 1 FROM collections WHERE id=$id AND revision=$revision)", ("$id", Key(id)), ("$revision", revision)); c.ExecuteNonQuery(); }
    public SelectionPage Page(Guid owner,Guid id,int start,int limit,bool trash=false)
    {
        if(start<0||limit is <1 or >200)throw new ArgumentException();
        using var db=Open();using var tx=db.BeginTransaction();var collection=Require(db,tx,owner,id,trash);var where="collection_id=$id"+(trash?"":" AND deleted_at IS NULL");
        using var count=Command(db,tx,"SELECT COUNT(*) FROM selections WHERE "+where,("$id",Key(id)));var total=Convert.ToInt32(count.ExecuteScalar(),CultureInfo.InvariantCulture);
        using var c=Command(db,tx,"SELECT item_id,position,added_at,deleted_at FROM selections WHERE "+where+" ORDER BY position,item_id LIMIT $limit OFFSET $start",("$id",Key(id)),("$limit",limit),("$start",start));
        var items=new List<Selection>();using(var r=c.ExecuteReader())while(r.Read())items.Add(new(Guid.Parse(r.GetString(0)),r.GetInt32(1),DateTimeOffset.Parse(r.GetString(2),CultureInfo.InvariantCulture),r.IsDBNull(3)?null:DateTimeOffset.Parse(r.GetString(3),CultureInfo.InvariantCulture)));tx.Commit();return new(collection,total,items);
    }
    public IReadOnlyList<Guid> SelectionIds(Guid owner,Guid id,bool trash=false)
    {using var db=Open();Require(db,null,owner,id,trash);using var c=Command(db,null,"SELECT item_id FROM selections WHERE collection_id=$id"+(trash?"":" AND deleted_at IS NULL"), ("$id",Key(id)));using var r=c.ExecuteReader();var ids=new List<Guid>();while(r.Read())ids.Add(Guid.Parse(r.GetString(0)));return ids;}
    public IReadOnlyDictionary<Guid,Selection> SelectionDetails(Guid owner,Guid id,IReadOnlyList<Guid> ids)
    {
        using var db=Open();Require(db,null,owner,id,true);var result=new Dictionary<Guid,Selection>();
        foreach(var batch in ids.Chunk(200)){
            var values=batch.Select((item,index)=>("$p"+index,(object?)Key(item))).Append(("$id",(object?)Key(id))).ToArray();
            using var c=Command(db,null,"SELECT item_id,position,added_at,deleted_at FROM selections WHERE collection_id=$id AND item_id IN ("+string.Join(',',values.Take(batch.Length).Select(p=>p.Item1))+")",values);using var r=c.ExecuteReader();while(r.Read()){var key=Guid.Parse(r.GetString(0));result[key]=new(key,r.GetInt32(1),DateTimeOffset.Parse(r.GetString(2),CultureInfo.InvariantCulture),r.IsDBNull(3)?null:DateTimeOffset.Parse(r.GetString(3),CultureInfo.InvariantCulture));}
        }return result;
    }
    private static string ArtworkKey(SqliteConnection db,SqliteTransaction? tx,Guid owner,string key,bool trash=false)
    {Key(owner);if(key=="liked")return key;if(!Guid.TryParse(key,out var id)||id==Guid.Empty)throw new ArgumentException();Require(db,tx,owner,id,trash);return Key(id);}
    public IReadOnlyList<ArtworkIndex> Artworks(Guid owner)
    {using var db=Open();using var c=Command(db,null,"SELECT list_key,version FROM list_artwork WHERE owner=$owner",("$owner",Key(owner)));using var r=c.ExecuteReader();var list=new List<ArtworkIndex>();while(r.Read())list.Add(new(r.GetString(0),r.GetString(1)));return list;}
    public ListArtwork? Artwork(Guid owner,string key)
    {using var db=Open();key=ArtworkKey(db,null,owner,key,true);using var c=Command(db,null,"SELECT jpeg,version FROM list_artwork WHERE owner=$owner AND list_key=$key",("$owner",Key(owner)),("$key",key));using var r=c.ExecuteReader();return r.Read()?new((byte[])r[0],r.GetString(1)):null;}
    public void SaveArtwork(Guid owner,string key,byte[] jpeg)
    {
        JpegThumbnail.Validate(jpeg);using var db=Open();using var tx=db.BeginTransaction();key=ArtworkKey(db,tx,owner,key);
        using var c=Command(db,tx,"INSERT INTO list_artwork(owner,list_key,jpeg,version) VALUES($owner,$key,$jpeg,$version) ON CONFLICT(owner,list_key) DO UPDATE SET jpeg=excluded.jpeg,version=excluded.version",("$owner",Key(owner)),("$key",key),("$jpeg",jpeg),("$version",Guid.NewGuid().ToString("N")));c.ExecuteNonQuery();tx.Commit();
    }
    public void RemoveArtwork(Guid owner,string key)
    {using var db=Open();key=ArtworkKey(db,null,owner,key);using var c=Command(db,null,"DELETE FROM list_artwork WHERE owner=$owner AND list_key=$key",("$owner",Key(owner)),("$key",key));c.ExecuteNonQuery();}
    public IReadOnlyList<Guid> WatchMemberships(Guid owner,IReadOnlyList<Guid> ids)
    {
        if(ids.Count>200)throw new ArgumentException();Key(owner);if(ids.Count==0)return [];
        using var db=Open();var p=ids.Select((id,i)=>("$p"+i,(object?)Key(id))).Append(("$owner",(object?)Key(owner))).ToArray();
        using var c=Command(db,null,"SELECT s.item_id FROM selections s JOIN collections c ON c.id=s.collection_id WHERE c.owner=$owner AND c.watch_later=1 AND s.deleted_at IS NULL AND s.item_id IN ("+string.Join(',',p.Take(ids.Count).Select(v=>v.Item1))+")",p);using var r=c.ExecuteReader();var result=new List<Guid>();while(r.Read())result.Add(Guid.Parse(r.GetString(0)));return result;
    }
    public IReadOnlyList<Guid> SavedMemberships(Guid owner,IReadOnlyList<Guid> ids)
    {
        if(ids.Count>200)throw new ArgumentException();Key(owner);if(ids.Count==0)return [];
        using var db=Open();var p=ids.Select((id,i)=>("$p"+i,(object?)Key(id))).Append(("$owner",(object?)Key(owner))).ToArray();
        using var c=Command(db,null,"SELECT DISTINCT s.item_id FROM selections s JOIN collections c ON c.id=s.collection_id WHERE c.owner=$owner AND c.deleted_at IS NULL AND s.deleted_at IS NULL AND s.item_id IN ("+string.Join(',',p.Take(ids.Count).Select(v=>v.Item1))+")",p);using var r=c.ExecuteReader();var result=new List<Guid>();while(r.Read())result.Add(Guid.Parse(r.GetString(0)));return result;
    }
    public void SetWatchMembership(Guid owner,Guid item,bool saved)
    {
        var collection=Create(owner,"Assistir mais tarde",true);Key(item);using var db=Open();using var tx=db.BeginTransaction();var current=Require(db,tx,owner,collection.Id);
        using var exists=Command(db,tx,"SELECT COUNT(*) FROM selections WHERE collection_id=$id AND item_id=$item AND deleted_at IS NULL",("$id",Key(collection.Id)),("$item",Key(item)));if((Convert.ToInt32(exists.ExecuteScalar(),CultureInfo.InvariantCulture)>0)==saved)return;
        Touch(db,tx,owner,collection.Id,current.Revision);
        using var c=saved?Command(db,tx,"INSERT INTO selections(collection_id,item_id,position,added_at) VALUES($id,$item,COALESCE((SELECT MAX(position)+1 FROM selections WHERE collection_id=$id),0),$now) ON CONFLICT(collection_id,item_id) DO UPDATE SET deleted_at=NULL",("$id",Key(collection.Id)),("$item",Key(item)),("$now",Time(DateTimeOffset.UtcNow))):Command(db,tx,"UPDATE selections SET deleted_at=$now WHERE collection_id=$id AND item_id=$item",("$id",Key(collection.Id)),("$item",Key(item)),("$now",Time(DateTimeOffset.UtcNow)));c.ExecuteNonQuery();tx.Commit();
    }
    public IReadOnlyList<CollectionMembership> Memberships(Guid owner,Guid itemId)
    {
        using var db=Open();using var c=Command(db,null,"SELECT c.id,c.name,c.watch_later,c.revision,c.deleted_at,EXISTS(SELECT 1 FROM selections s WHERE s.collection_id=c.id AND s.item_id=$item AND s.deleted_at IS NULL) FROM collections c WHERE c.owner=$owner AND c.deleted_at IS NULL ORDER BY c.watch_later DESC,c.name,c.id",("$owner",Key(owner)),("$item",Key(itemId)));
        using var r=c.ExecuteReader();var result=new List<CollectionMembership>();while(r.Read())result.Add(new(ReadCollection(r),r.GetBoolean(5)));return result;
    }
}
