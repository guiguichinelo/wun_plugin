# Maintenance and request lifecycle (beta 3)

Approved requests wait for availability. Completed and Canceled are additional states; prior numeric decision values remain unchanged. A declined or canceled request requires explicit confirmation before reopening the existing record. Its history and unique supporters remain intact. System transitions have no administrator identity.

WUN availability update runs every six hours by default. It checks actual catalog items and their requested scope, including declined and canceled requests. Movie and episode matches require real media paths. Season completion requires the expected episodes; whole ongoing series remain active and can display “Up to date.” Whole series complete only when ended and all expected episodes are owned. Unknown provider coverage never becomes completion. Large series exceeding the existing 40-season safety bound remain unknown for automation.

Optional expiration rules count from the latest approval and independently from the oldest released-but-missing episode while monitoring. Missing or unreliable coverage suspends automatic expiration. Defaults disable both rules. Catalog completion takes priority over expiration; completed requests do not automatically reopen if media disappears later.

WUN cleanup runs daily. All retention defaults are zero (never delete). Configure retention independently for completed requests, canceled requests and read notifications. Request retention starts at the corresponding terminal transition. Read-notification retention starts at the recorded read time. Unknown historical dates remain ineligible. Declined and active requests are preserved. Deleting a terminal request also deletes its linked notifications (including unread ones), history, followers and supporters. Media, Jellyfin users and other plugins are never deleted.

The native configuration page offers preview, confirmed execution and immediate availability update. Preview uses the current form; execution uses saved settings. Saving enabled retention asks for confirmation with the candidate counts. Native Scheduled Tasks owns progress, schedules and execution results. Concurrent WUN tasks share a gate. Deletion uses a single transaction and checks cancellation between records. Automatic database compaction is not performed.

Operational defaults allow requests, all content types and unlimited active requests. Anime classification takes precedence over movie/series rules. Limits count authored active requests, not supports; administrators are exempt. Notification following is a default for new participation, with explicit per-request choices preserved.

## Compatibility and recovery

Database schema 3 preserves original identities and unknown authors/dates, adds read timestamps, system events, follow preferences and maintenance results. Legacy public identifiers migrate to non-reusable SQLite identifiers. Back up plugin configuration, plugin binaries and the complete WUN database directory before upgrading. To revert to a schema-2 binary, stop Jellyfin and restore matching binaries and database together; do not open schema 3 with the old binary.

Metrics and maintenance APIs require Jellyfin administrator privileges. New endpoint: `GET /WUN/v1/admin/maintenance/preview`, with optional retention days for simulation. It never deletes. Manual execution uses Jellyfin Scheduled Tasks. Request creation accepts `ConfirmReopen`; a declined/canceled match otherwise returns `409 reopen_confirmation`. Completed/Canceled values are appended to the existing decision enum. Lists expose `Current` for current episode coverage, without changing per-user catalog visibility.
