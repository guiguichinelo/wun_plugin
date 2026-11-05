# WUN beta 3 API

All `/WUN/v1` routes require native Jellyfin authentication. User identifiers in
request payloads are ignored; identity comes from the authenticated claim.
Administrative routes additionally require the native `RequiresElevation` policy.
Responses disable shared caching. JSON property casing follows the host settings.

| Method | Route | Behavior |
|---|---|---|
| GET | `/WUN/v1/me` | Current administrator capability |
| GET | `/WUN/v1/search?query=...&kind=Movie` | Up to 12 permitted results; All (default), Movie or Series; title, numeric TMDb ID, tmdb:ID, tt ID, imdb:tt ID or IMDb title URL |
| GET | `/WUN/v1/requests` | start, limit (1..100), optional decision/sort; always the current user's authored/supported requests; items and hasMore |
| POST | `/WUN/v1/requests` | kind, tmdbId, optional season/episode; title resolved server-side |
| PUT | `/WUN/v1/requests/{id}/follow?enabled=true` | Change the authenticated user's subscription |
| GET | `/WUN/v1/requests/{id}/history` | Public replies without actor or private notes |
| PUT | `/WUN/v1/admin/requests/{id}` | revision, decision, message, internalNote, reviewAt, monitor |
| GET | `/WUN/v1/admin/requests/{id}/history` | Administrative history including private fields |
| GET | `/WUN/v1/notifications` | Paginated enriched notices belonging to the authenticated user |
| POST | `/WUN/v1/notifications/read` | Mark only this user's notices read |

Decision values: Pending=0, Approved=1, Deferred=2, Rejected=3, Completed=4, Canceled=5. Availability values:
Unknown=0, Absent=1, Partial=2, Available=3. Availability is derived on demand from the visible catalog. Series/season presence
is partial until complete episode coverage is verified. Decisions never imply that a file exists.

Invalid inputs return 400, anonymous requests 401, unauthorized administration 403,
inaccessible/missing public request identifiers 404 and stale decision revisions 409.
Mutation responses use explicit JSON to support the host API client's JSON parser.

`/WUN/web/requests.html`, app.js, style.css and header.js are public static resources
containing no account data. The page carries `data-wun-requests` as an availability
marker for the header shortcut; it is not an authorization mechanism.

## Search filters and matches

Optional query parameters: yearFrom/yearTo (1800..2200, ascending range), genres
(up to six comma-separated genre fragments, 200 characters), minRating (0..10),
and obtained (any/yes/no). All active filters combine; genre fragments use OR.
Unknown years or ratings do not pass a numeric filter. Filters operate on the
provider search candidates, before limiting the matching result array to 12.

Search cards additionally include ImdbId, Genres, Rating and Catalog. Catalog
contains Obtained, optional visible ItemId and optional EpisodeCount. EpisodeCount
is indexed nonvirtual episodes visible to this user, not total expected episodes.
No hidden library item IDs or counts are returned. Request-list cards also include
current-user Catalog and ImdbId projections without changing stored request history.

Creation returns 409 with Error=already_obtained for an indexed movie or exact
episode. A series with any episodes may still request missing seasons or episodes.
No background scan, remote filesystem stat or media download is triggered.

## Combined search and controls

Omitting kind now searches both Movie and Series. Each provider query is bounded
independently, then up to 12 results are interleaved to represent both kinds when
available. Equal TMDb numeric IDs in different kinds are kept distinct. Explicit
Movie/Series selection still searches only that type; request creation accepts
only the actual Movie or Series result kind, never All.

The web UI defaults to combined search. Advanced filters are controlled by an
accessible icon button (aria-expanded/aria-controls), with a separate clear action.

## Anime selector

Search kind also accepts Anime. The UI choices are All, Movie, Series and Anime.
Anime searches both native types and filters before the per-type result limit;
request Kind remains Movie or Series. Explicit anime genre or animation with
an anime keyword/Japanese production identifies candidates from native metadata.
Missing or inaccurate provider metadata can affect classification. Movie/Series
selectors remain format filters, so anime movies and series can also appear there.

## Multiple selections and artwork

The search kind parameter also accepts comma-separated values, e.g. Movie,Anime.
Selections combine as a union: Movie,Anime searches all movies plus anime series;
Series,Anime searches all series plus anime movies. Empty selection and All mean
both formats. The planner issues at most one query per native format.

Search cards may include BackdropUrl, selected only from remote images explicitly
marked Backdrop by the native provider. URLs are restricted to HTTPS image.tmdb.org
and resized to w1280. No poster is substituted as a backdrop. Missing artwork does
not imply missing catalog content.

## Beta 3 workflow and administration

- GET /WUN/v1/admin/requests: elevated general queue, optional start/limit/decision/sort.
- GET /WUN/v1/requests/{id}: visible public projection, no requester identities or private notes.
- GET /WUN/v1/admin/requests/{id}: elevated requester/supporter context and last manual decision.
- PUT /WUN/v1/requests/{id}/support?enabled=true: unique support; revoked requests require confirmed reopening through creation.
- POST /WUN/v1/requests accepts ConfirmReopen; otherwise a declined/canceled match returns 409 reopen_confirmation.
- POST /WUN/v1/notifications/{id}/read: own notification only.
- GET /WUN/v1/admin/metrics and GET /WUN/v1/admin/maintenance/preview: elevated metrics and non-mutating retention preview.

Native plugin configuration page: configurationpage?name=wun. Scheduled tasks WUN.Availability and WUN.Cleanup own execution and scheduling. See maintenance.md for defaults and schema-3 migration.
