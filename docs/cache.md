# Read caching

WUN caches expensive observations, not workflow ownership or authorization decisions.

| Data | Retention / limit | Isolation |
| --- | --- | --- |
| Remote search results | 15 minutes / 256 entries | Query, type, year, language, country |
| Remote metadata | 15 minutes / 1,000 entries | Type, provider ID, language, country; fresh native visibility check |
| Artwork URLs | 30 minutes / 1,000 entries | Type, provider ID, image type, language, country |
| Remote title/season schedule | 15 minutes / 200 entries | Type, title, season, language, country |
| Local presence | 10 seconds / 1,024 entries | User, current policy fingerprint, library generation, selection |
| Interactive coverage | 10 seconds / 512 entries | User, current policy fingerprint, library generation, selection, monitoring |

Caches are process-local, bounded and disposable. They do not persist credentials or extend the public API. Native provider caches may also contribute to warm performance. Shared loads use a cancellation deadline of 30 seconds, independently of individual HTTP client cancellation. Provider/season concurrency limits are preserved. Null metadata and exceptions are not stored as successful entries; legitimate empty search arrays retain the search TTL.

Local observations invalidate when Jellyfin emits item-added, updated or removed events. User policy is read again before constructing a local cache key; a missing user fails authorization. An old load completing after a library generation change cannot populate the current generation. The 10-second TTL bounds freshness if a source changes without an event. Current policy includes library and parental restrictions, so never substitute a process-wide result for the user-scoped lookup.

Background availability reconciliation bypasses the interactive coverage cache. Request decisions, support/follow state, read state, history and administrator-only people/notes remain direct store projections. Controllers only memoize presentations within one response. Public/admin HTTP responses continue to use no-store; do not add a shared reverse-proxy cache for them.

Cache tests use controlled loads/time and fake native library/user services. HTTP regression tests verify that repeated notifications share presentation work only within the response and reflect later decisions immediately. UI regression tests hold a refresh response open and verify that controls/cards remain until its replacement arrives. Live measurements should distinguish cold provider access, warm provider data, warm local observations and expiry. Do not infer streaming capacity from a navigation benchmark.
