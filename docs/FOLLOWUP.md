# Integration follow-up

## Empty identifiers in third-party routes — fixed in beta 6

The global mirror protection filter treated `Guid.Empty` as a persisted playlist identifier. Third-party integrations can legitimately use it as a clear-selection sentinel, causing a store-key validation exception before their controller ran.

The filter now removes the empty identifier, and mirror lookup defensively ignores empty identifiers while still protecting valid mirror IDs. The HTTP regression covers a clear-selection route and mixed empty/real identifiers in bulk mutations: another owner's mirror remains inaccessible and an owner's native mutation remains blocked. No authentication or ownership validation was relaxed.

The release includes an HTTP regression for the sentinel and mixed bulk identifiers; see the beta 6 release notes for scope.

## Global native mutation gate — narrowed for user-data writes in beta 6

Earlier versions held one shared semaphore across native write actions containing identifier references, including the downstream controller call even when lookup found no mirror. A deterministic HTTP barrier test demonstrated that this serialized independent progress writes.

Beta 6 exempts only the Jellyfin 12.4 UserData/UpdateItemUserData action from that gate. Its mirror ownership checks still execute. Native playlist mutations and all other write routes retain the gate, including its creation/recovery race protection. The concurrency regression also verifies cross-owner denial and owner-only progress on a real mirror fixture.

Follow up with controlled load measurements, and inspect other potentially long-running routes such as PlaybackInfo before considering further changes. The deterministic regression is not proof that the gate caused any specific live timeout. Do not attribute a change between plugin versions solely to a database provider.
