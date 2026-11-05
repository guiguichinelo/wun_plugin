# Architecture and beta limits

WUN is a Jellyfin 12.4 plugin for Linux x64. Its own SQLite store tracks requests, followers, revisions, public decisions, administrator-only notes and notifications. Authentication and elevation use the host policies; catalog matches are resolved for the current user.

Search and metadata use the installed TMDb provider. Details include movie information, series seasons, episode images and release dates. Completeness compares season/episode identities and requires known provider coverage. Specials are separate. At most 40 seasons are used to confirm overall coverage; another selected season remains viewable. Provider metadata is cached for 15 minutes with bounded concurrency; per-user availability is not shared across accounts.

The web UI uses Plugin Pages and File Transformation 3.0.3.0 Release-12.4.0 builds. An adapter addresses the upstream legacy template race without editing host bundles on disk. It inherits the installed theme and provides Portuguese/English labels, with English fallback for other host locales.

Acquisition remains manual. Availability reconciliation runs every six hours and optional cleanup runs daily through native scheduled tasks. Destructive policies default to disabled. Review reminders, native season-page integration and complete overlapping-request reconciliation are not implemented. See maintenance.md for schema migration, expiry and retention semantics. Future dates are provider estimates. This beta is not a promise of production readiness or compatibility with other platforms.
