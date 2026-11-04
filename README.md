# WUN â€” What U Need?

Catalog requests inside Jellyfin. **0.1.0-beta.1: Jellyfin 12.4.0, Linux x64.**

Search the installed TMDb provider by title, IMDb or TMDb ID. Filter results, inspect movie synopses or series seasons/episodes, view available and missing episodes, and submit requests. Administrators can approve, decline or defer requests, keep private notes and also submit their own requests. Users can follow shared requests and read public decisions and notifications.

Details include title/season covers, episode stills and provider release dates. Complete-series detection distinguishes all released episodes from ended/canceled shows. The header shortcut is a desk bell. Native themes and interface-language preferences are supported; Portuguese and English labels are included, with English fallback elsewhere.

Add [Betterfin for Jellyfin 12](https://raw.githubusercontent.com/guiguichinelo/betterfin/main/12/manifest.json), then install WUN. Install Plugin Pages and File Transformation **3.0.3.0, Release-12.4.0 builds** first. [Download beta 1](https://github.com/guiguichinelo/wun_plugin/releases/tag/v0.1.0-beta.1).

**Acquisition is manual.** Monitoring preferences are stored, but automated monitoring/review reminders and native season-page integration are pending. Other server versions, Windows, ARM, native clients and sustained load are not validated. Back up configuration and plugin data before installing.

See [architecture and limits](docs/IMPLEMENTATION.md), [API](docs/API.md) and [themes/locales](docs/themes-and-locales.md). Build with .NET 10, then run the core executable tests, API tests and Python/Playwright browser checks in tests/. Set WUN_BROWSER_CHANNEL=msedge to use Edge locally; CI uses the Chrome installation in its Ubuntu 24.04 runner. scripts/package.ps1 generates the Linux x64 ZIP and checksum receipt under artifacts/; keep all packaged SQLite dependencies together.

GPL-3.0; see LICENSE and THIRD-PARTY-NOTICES.md.
