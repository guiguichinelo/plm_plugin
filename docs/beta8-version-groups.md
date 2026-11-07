# Beta 8: personal lists and merged versions

Jellyfin 12.4.0 beta release. No database migration is required.

Personal lists opt into `groupVersions=true`. The default API view remains unchanged. Grouped entries preserve `ItemId` and add `DisplayItemId` and `SelectionIds`; filtering and pagination operate on displayed groups. The original selections and their ordering remain stored, so splitting native versions reveals the original entries again.

Only explicit Jellyfin version relations are used. The source and destination must be accessible to the requesting user. Missing or cyclic relations do not authorize a fallback to another title. Hidden alternate versions are resolved through the native visibility check; being hidden from a standalone listing is not the same as lacking access.

`POST collections/{id}/version-groups/remove` and `/restore` accept `{Revision, DisplayItemId, SelectionIds}`. The server validates the current selection set, owner and revision. A mismatch returns 409. Changes are transactional and increment the collection revision once. Removing a displayed group only affects its current list. Existing individual-selection endpoints remain available.

Membership queries, the picker and Watch Later understand groups. Adding an already represented version is idempotent and returns the current revision. Membership does not change native favorites or playback data. Deleted selections remain restorable.

No changes to Merge Versions are bundled. Use its scheduled jobs for long operations instead of starting overlapping manual requests.

Validation includes API grouping, legacy responses, group removal/restoration, split-related conflicts, owner isolation, Watch Later, idempotent additions, pagination and raw-order preservation. Live movie and episode merge/split tests were performed on a test server. These checks do not establish sustained load capacity.
