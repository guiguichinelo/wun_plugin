# Workflow candidate — not yet published

Beta 3 uses assembly version 0.1.0.3 and WUN database schema 3. Published beta 2 packages remain unchanged.

## Personal requests and administrative review

Regular accounts see their own requests and requests they support. The server enforces this filter even if the caller passes mine=false. The administrative queue and user identity projections have separate endpoints protected by Jellyfin's elevation policy.

Creating a request records the authenticated author and creation time. A duplicate selection joins the existing request, adds one unique support per account, and does not replace the original author. Selection identity includes media kind, provider ID, season and episode; distinct episode scopes remain distinct requests.

Support and notification following are independent. Withdrawing support never deletes the shared request, and stopping notifications does not remove support. Administrators can order the queue by unique support count, newest or oldest; support does not automatically approve a request.

Review cards include type, year, scope, cover when available, support count and status. Administrative cards show the known author and interested accounts, including native profile images. Approval, rejection and follow-up have explicit actions with confirmation. The editor retrieves the current revision and retains prior public replies, internal notes and review dates.

Notifications include title, scope, artwork, public message, read state, event time when known and a link to the request. Individual reads remain scoped to the authenticated recipient. Internal notes and other account identities are not included in regular-user projections.

## Schema-1 migration and rollback

Back up both plugin binaries and the WUN data directory before upgrading. Schema 1 did not record original requester or creation time. Migration preserves those fields as unknown; current followers are imported once as existing interest, never as evidence of authorship. Existing requests, history, notifications and read states are retained. A withdrawn support is not recreated on restart.

An older plugin does not support schema 3. To roll back, restore a matching backup of both plugin binaries and WUN data with the server stopped. Replacing only the DLL is insufficient.

## Known limits

The queue still depends on visible metadata from the installed provider. Provider outages and visibility filtering can affect page contents. A series' aggregate availability in a request row may be less precise than its detailed episode coverage. Notification push delivery and an unread badge are not included in this change. Languages other than Portuguese use the existing English fallback.

Tests use synthetic identities and requests. Real server data, portraits and infrastructure details are not included in this repository.
# Native administration

WUN exposes one native Jellyfin configuration page (`wun`) through `IHasWebPages`. The dashboard sidebar entry and the installed plugin's configuration action both use this registration. The administrative page does not require Plugin Pages; the user-facing Requests page retains its existing dependencies.

The page includes request totals by decision and media type, unique supports, distinct interested accounts, monitored requests, overdue reviews and the five most supported requests. Review metrics use only the latest decision, and only deferred requests can have overdue reviews. Metrics read a consistent local database snapshot and do not call metadata providers or estimate catalog completeness. Legacy requests without recorded authors are counted separately.

Native plugin configuration persists page size (10–100; default 30) and initial administrator ordering (most wanted, newest, oldest). The Requests client obtains these defaults through its authenticated profile endpoint. Metrics and native plugin configuration remain restricted by Jellyfin's administrator policy. Lifecycle and maintenance extend this candidate to schema 3; see [maintenance](maintenance.md).

