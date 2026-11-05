using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Plm.Core;

/// <summary>Durable user intent, in the same database and backup as the lists.
/// Catalog disappearance never deletes intent. Only explicit user actions do.</summary>
public sealed class CatalogResilienceStore
{
    private readonly string _connection;
    public CatalogResilienceStore(string path)
    {
        _connection = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var c = db.CreateCommand();
        // Additive tables keep existing lists and beta-4 rollback readable.
        c.CommandText = """
            CREATE TABLE IF NOT EXISTS resilience_media(
              owner TEXT NOT NULL, reference_id TEXT NOT NULL, current_item_id TEXT NOT NULL,
              identity_json TEXT NOT NULL, favorite INTEGER NULL, favorite_revision INTEGER NOT NULL DEFAULT 0,
              pending_favorite INTEGER NOT NULL DEFAULT 0, missing INTEGER NOT NULL DEFAULT 0,
              PRIMARY KEY(owner,reference_id), UNIQUE(owner,current_item_id));
            CREATE TABLE IF NOT EXISTS resilience_aliases(
              owner TEXT NOT NULL,item_id TEXT NOT NULL,reference_id TEXT NOT NULL,
              PRIMARY KEY(owner,item_id),
              FOREIGN KEY(owner,reference_id) REFERENCES resilience_media(owner,reference_id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS resilience_log(
              id INTEGER PRIMARY KEY,owner TEXT NOT NULL,reference_id TEXT NOT NULL,
              old_item_id TEXT NOT NULL,new_item_id TEXT NOT NULL,changed_at TEXT NOT NULL);
            """;
        c.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_connection); db.Open(); return db; }
    private static string Key(Guid id) => id == Guid.Empty ? throw new ArgumentException("Empty identity.") : id.ToString("N");
    private static SqliteCommand Cmd(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql;
        foreach (var (k, v) in args) c.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return c;
    }
    private static RememberedMedia Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)),
        Guid.Parse(r.GetString(1)), Guid.Parse(r.GetString(2)),
        JsonSerializer.Deserialize<CatalogIdentity>(r.GetString(3))!,
        r.IsDBNull(4) ? null : r.GetBoolean(4), r.GetInt64(5), r.GetBoolean(6), r.GetBoolean(7));

    public RememberedMedia Remember(Guid owner, CatalogIdentity identity)
    {
        Key(identity.ItemId);
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var c = Cmd(db, tx, """
            SELECT m.reference_id,m.owner,m.current_item_id,m.identity_json,m.favorite,m.favorite_revision,m.pending_favorite,m.missing
            FROM resilience_media m JOIN resilience_aliases a ON a.owner=m.owner AND a.reference_id=m.reference_id
            WHERE a.owner=$owner AND a.item_id=$item
            """, ("$owner", Key(owner)), ("$item", Key(identity.ItemId))))
        {
            using var r = c.ExecuteReader();
            if (r.Read())
            {
                var existing = Read(r); r.Close();
                // Metadata arriving late may enrich a reference. Never erase
                // its known providers or silently accept a conflicting title.
                if (existing.CurrentItemId == identity.ItemId &&
                    (existing.Identity.Providers.Count == 0 || existing.Identity.Matches(identity)))
                {
                    var providers = new Dictionary<string, string>(existing.Identity.Providers, StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in identity.Providers) providers[pair.Key] = pair.Value;
                    var enriched = identity with { Providers = providers };
                    if (JsonSerializer.Serialize(enriched)==JsonSerializer.Serialize(existing.Identity))
                    { tx.Commit(); return existing; }
                    using var update = Cmd(db, tx, "UPDATE resilience_media SET identity_json=$json WHERE owner=$owner AND reference_id=$ref",
                        ("$json", JsonSerializer.Serialize(enriched)), ("$owner", Key(owner)), ("$ref", Key(existing.ReferenceId)));
                    update.ExecuteNonQuery(); tx.Commit(); return existing with { Identity = enriched };
                }
                tx.Commit(); return existing;
            }
        }
        var reference = Guid.NewGuid();
        using (var c = Cmd(db, tx, "INSERT INTO resilience_media(owner,reference_id,current_item_id,identity_json) VALUES($owner,$ref,$item,$json)",
            ("$owner", Key(owner)), ("$ref", Key(reference)), ("$item", Key(identity.ItemId)), ("$json", JsonSerializer.Serialize(identity)))) c.ExecuteNonQuery();
        using (var c = Cmd(db, tx, "INSERT INTO resilience_aliases VALUES($owner,$item,$ref)",
            ("$owner", Key(owner)), ("$item", Key(identity.ItemId)), ("$ref", Key(reference)))) c.ExecuteNonQuery();
        tx.Commit(); return new(reference, owner, identity.ItemId, identity, null, 0, false);
    }
    public IReadOnlyList<RememberedMedia> References(Guid? owner = null)
    {
        using var db = Open(); using var c = Cmd(db, null,
            "SELECT reference_id,owner,current_item_id,identity_json,favorite,favorite_revision,pending_favorite,missing FROM resilience_media" +
            (owner.HasValue ? " WHERE owner=$owner" : "") + " ORDER BY owner,reference_id", ("$owner", owner?.ToString("N")));
        using var r = c.ExecuteReader(); var result = new List<RememberedMedia>();
        while (r.Read()) result.Add(Read(r)); return result;
    }
    public void RecordFavorite(Guid owner, Guid reference, bool favorite, bool seedOnly = false)
    {
        using var db = Open(); using var c = Cmd(db, null, """
            UPDATE resilience_media SET favorite=$value,favorite_revision=favorite_revision+1,pending_favorite=$pending
            WHERE owner=$owner AND reference_id=$ref
            """ + (seedOnly ? " AND favorite IS NULL" : ""),
            ("$value", favorite ? 1 : 0), ("$pending", seedOnly ? 0 : 1), ("$owner", Key(owner)), ("$ref", Key(reference)));
        c.ExecuteNonQuery();
    }
    public bool Rebind(RememberedMedia expected, CatalogIdentity candidate)
    {
        if (!expected.Identity.Matches(candidate) || expected.CurrentItemId == candidate.ItemId) return false;
        using var db = Open(); using var tx = db.BeginTransaction();
        // A separately observed destination can contain newer explicit intent.
        // Never merge it or choose a winner based on playback dates.
        using (var c = Cmd(db, tx, "SELECT COUNT(*) FROM resilience_aliases WHERE owner=$owner AND item_id=$item",
            ("$owner", Key(expected.Owner)), ("$item", Key(candidate.ItemId))))
            if (Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture) != 0) return false;
        // Do not overwrite an active selection OR a user's deletion tombstone.
        using (var c = Cmd(db, tx, """
            SELECT COUNT(*) FROM selections s JOIN collections c ON c.id=s.collection_id
            WHERE c.owner=$owner AND s.item_id=$target AND c.id IN(
              SELECT s2.collection_id FROM selections s2 JOIN resilience_aliases a ON a.item_id=s2.item_id
              WHERE a.owner=$owner AND a.reference_id=$ref)
            """, ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)), ("$target", Key(candidate.ItemId))))
            if (Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture) != 0) return false;
        using (var c = Cmd(db, tx, """
            UPDATE resilience_media SET current_item_id=$target,identity_json=$json,missing=0,pending_favorite=CASE WHEN favorite IS NULL THEN 0 ELSE 1 END
            WHERE owner=$owner AND reference_id=$ref AND current_item_id=$old AND favorite_revision=$revision
            """, ("$target", Key(candidate.ItemId)), ("$json", JsonSerializer.Serialize(candidate)),
            ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)),
            ("$old", Key(expected.CurrentItemId)), ("$revision", expected.FavoriteRevision)))
            if (c.ExecuteNonQuery() != 1) return false;
        var collections = new List<string>();
        using (var c = Cmd(db, tx, """
            SELECT DISTINCT s.collection_id FROM selections s JOIN collections c ON c.id=s.collection_id
            JOIN resilience_aliases a ON a.item_id=s.item_id AND a.owner=c.owner
            WHERE c.owner=$owner AND a.reference_id=$ref
            """, ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId))))
        { using var r = c.ExecuteReader(); while (r.Read()) collections.Add(r.GetString(0)); }
        foreach (var collection in collections)
        {
            using (var c = Cmd(db, tx, """
                UPDATE selections SET item_id=$target WHERE collection_id=$collection AND item_id IN(
                  SELECT item_id FROM resilience_aliases WHERE owner=$owner AND reference_id=$ref)
                """, ("$target", Key(candidate.ItemId)), ("$collection", collection),
                ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)))) c.ExecuteNonQuery();
            using (var c = Cmd(db, tx, "UPDATE collections SET revision=revision+1 WHERE id=$id", ("$id", collection))) c.ExecuteNonQuery();
            using (var c = Cmd(db, tx, "INSERT INTO jobs(collection_id) VALUES($id) ON CONFLICT(collection_id) DO UPDATE SET attempts=0,error=NULL",
                ("$id", collection))) c.ExecuteNonQuery();
        }
        using (var c = Cmd(db, tx, "INSERT INTO resilience_aliases VALUES($owner,$item,$ref)",
            ("$owner", Key(expected.Owner)), ("$item", Key(candidate.ItemId)), ("$ref", Key(expected.ReferenceId)))) c.ExecuteNonQuery();
        using (var c = Cmd(db, tx, "INSERT INTO resilience_log(owner,reference_id,old_item_id,new_item_id,changed_at) VALUES($owner,$ref,$old,$new,$now)",
            ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)),
            ("$old", Key(expected.CurrentItemId)), ("$new", Key(candidate.ItemId)), ("$now", DateTimeOffset.UtcNow.ToString("O")))) c.ExecuteNonQuery();
        tx.Commit(); return true;
    }
    public void AcknowledgeFavorite(RememberedMedia expected)
    {
        using var db = Open(); using var c = Cmd(db, null, """
            UPDATE resilience_media SET pending_favorite=0 WHERE owner=$owner AND reference_id=$ref
            AND current_item_id=$item AND favorite_revision=$revision
            """, ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)),
            ("$item", Key(expected.CurrentItemId)), ("$revision", expected.FavoriteRevision)); c.ExecuteNonQuery();
    }
    public void MarkMissing(Guid owner, Guid item)
    {
        using var db = Open(); using var c = Cmd(db, null,
            "UPDATE resilience_media SET missing=1 WHERE owner=$owner AND current_item_id=$item",
            ("$owner", Key(owner)), ("$item", Key(item))); c.ExecuteNonQuery();
    }
    public void Reappeared(RememberedMedia expected, CatalogIdentity current)
    {
        if (expected.CurrentItemId != current.ItemId || !expected.Identity.Matches(current)) return;
        using var db = Open(); using var c = Cmd(db, null, """
            UPDATE resilience_media SET missing=0,pending_favorite=CASE WHEN favorite IS NULL THEN 0 ELSE 1 END
            WHERE owner=$owner AND reference_id=$ref AND current_item_id=$item AND missing=1
            """, ("$owner", Key(expected.Owner)), ("$ref", Key(expected.ReferenceId)), ("$item", Key(current.ItemId)));
        c.ExecuteNonQuery();
    }
    public void DeleteOwner(Guid owner)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var c = Cmd(db, tx, "DELETE FROM resilience_media WHERE owner=$owner", ("$owner", Key(owner)))) c.ExecuteNonQuery();
        using (var c = Cmd(db, tx, "DELETE FROM resilience_log WHERE owner=$owner", ("$owner", Key(owner)))) c.ExecuteNonQuery();
        tx.Commit();
    }
}
