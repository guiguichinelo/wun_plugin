# Beta 6: catalog reconciliation and episode requests

Targets Jellyfin 12.4.0, Linux x64. No database migration is required.

## Automatic availability

A hosted worker observes movie/series/season/episode additions and updates, successful library scans, and administrator decisions. It also reconciles once after startup. Events are coalesced for 20 seconds, with a two-minute upper bound on waiting for a quiet period. Reconciliation waits while the library scan is running. The existing scheduled availability task remains a fallback.

Automatic runs share the maintenance runner's exclusion gate, retain pending changes after failure, and do not run expiration or cleanup policies. A catalog event received during reconciliation is not lost. Available active requests can complete; rejected, canceled and already completed requests are preserved. Optimistic revisions and transactional transitions prevent duplicate completion events and notifications. Missing content does not automatically reopen a completed request.

An episode request is complete when that episode is present, independently of whether the whole series has ended. Whole-series and season completeness retain their coverage rules. Unknown remote coverage is not treated as complete.

## Presentation and API

`CatalogPresence` adds optional `EpisodeItemId`; `ItemId` continues to identify the series. Episode lookups recognize multi-episode files, exclude virtual entries, and deduplicate coverage by season/episode. The web client opens the requested episode when an episode ID is available.

Requests on the displayed page are grouped by series/season, with independent decisions, interests and history for each request. Summaries count the requests displayed, not all episodes in the series. Available episodes use a confirmation indicator; the generic series-level "up to date" label is omitted for episode requests. Singular/plural wording is corrected. The default list filter shows all statuses.

Visible personal/administrative lists refresh every 30 seconds, retaining filters and pagination. Refresh pauses while a dialog or input/select interaction is active, the page is hidden, or a request is already loading. Search and notification pages are not polled by this mechanism.

## Validation

- 66 API tests and 29 core checks pass.
- Browser regressions cover request/administrator flows, episode groups, exact episode links, polling, focused controls, mobile layout, light/dark backgrounds and 64 host locales with the existing Portuguese/English fallback.
- Live candidate validation covered startup completion, approval of already-present content, episode arrival after refreshing one series, preservation of canceled/rejected states, exact episode navigation and no repeated completion notifications after restart.

Tests do not establish sustained-load capacity, compatibility with every Jellyfin release, or every combination of user/library restrictions. Publication is tracked in the release notes and package receipt.
