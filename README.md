# WUN - What U Need?

Catalog requests inside Jellyfin. **0.1.0-beta.5: Jellyfin 12.4.0, Linux x64.**

Search the installed TMDb provider by title, IMDb or TMDb ID. Filter results, inspect movie synopses or series seasons/episodes, view available and missing episodes, and submit requests. Administrators can approve, decline or defer requests, keep private notes and also submit their own requests. Users can follow shared requests and read public decisions and notifications.

Details include title/season covers, episode stills and provider release dates. Complete-series detection distinguishes all released episodes from ended/canceled shows. The header shortcut is a desk bell. Native themes and interface-language preferences are supported; Portuguese and English labels are included, with English fallback elsewhere.

Add [Betterfin for Jellyfin 12](https://raw.githubusercontent.com/guiguichinelo/betterfin/main/12/manifest.json), then install WUN. Install Plugin Pages and File Transformation **3.0.3.0, Release-12.4.0 builds** first. [Download beta 5](https://github.com/guiguichinelo/wun_plugin/releases/tag/v0.1.0-beta.5).

**Acquisition is manual.** Beta 3 adds private personal queues, unique supports, rich notifications, a general administrator queue, native configuration/metrics, availability reconciliation and optional retention/expiration. All deletion and expiration policies are disabled by default. The local WUN database migrates to schema 3: back up both binaries and data, and restore them together if reverting. Review reminders and native season-page integration remain pending. Other server versions, Windows, ARM, native clients and sustained load are not validated. Back up configuration and plugin data before installing.

See [architecture and limits](docs/IMPLEMENTATION.md), [API](docs/API.md) and [themes/locales](docs/themes-and-locales.md). Build with .NET 10, then run the core executable tests, API tests and Python/Playwright browser checks in tests/. Set WUN_BROWSER_CHANNEL=msedge to use Edge locally; CI uses the Chrome installation in its Ubuntu 24.04 runner. scripts/package.ps1 generates the Linux x64 ZIP and checksum receipt under artifacts/; keep all packaged SQLite dependencies together.

GPL-3.0; see LICENSE and THIRD-PARTY-NOTICES.md.

See [maintenance and recovery](docs/maintenance.md) and [beta 3 changes](docs/releases/0.1.0-beta.3.md).

Beta 4 improves request-page titles, notification read groups, full-row type selection, repeated requests without leaving search, state-aware decision actions and refresh controls that retain filters. See [beta 4 changes](docs/releases/0.1.0-beta.4.md).

Beta 5 adds bounded provider caches, shared loads and short user/policy-scoped catalog observations while preserving live workflow state. Refresh keeps controls and cards visible. See [beta 5 changes](docs/releases/0.1.0-beta.5.md) and [cache behavior](docs/cache.md).
