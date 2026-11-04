# Native themes and interface languages

WUN uses generic catalog labels and does not require a server brand or custom stylesheet. Controls and dialogs inherit the host palette. Light and Apple TV use light surfaces and readable catalog indicators; dark themes retain the translucent backdrop presentation.

Interface labels follow the user's Jellyfin `html.lang` preference. Portuguese and English are provided; other interface languages fall back to English. Metadata titles may follow the server/provider metadata language independently. Locale tests exercise the 64 active Jellyfin 12.4 UI locales, RTL direction and mobile controls; they do not claim complete translations.

`tests/page_smoke.py` covers user/admin UI behavior, search by Enter, advanced filters, memberships, obtained titles and stale responses. `tests/header_smoke.py` covers the quick-access button. `tests/locales_smoke.py` covers interface fallback. `tests/page_loader_smoke.py` covers the legacy template loader's page ownership, outgoing animations, account changes and webpack escaping. Browser tests can use installed Edge.

Plugin Pages 3.x initializes templates through a globally visible page selector. During native animation that can target an outgoing page. WUN registers an idempotent File Transformation adapter after the Plugin Pages startup task, covering its HTML and virtual template chunks. It assigns each template its own container and rejects responses after removal or account change. This integration also works when PLM is absent and does not patch host bundles on disk. Unknown upstream template formats require renewed compatibility testing.
