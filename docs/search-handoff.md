# Native search handoff

On Jellyfin 12.4, the header integration adds “Didn’t find what you were looking for? Request” to the native search empty state and to the end of populated results. Portuguese interfaces receive the Portuguese labels; other locales use the existing English fallback.

The suggestion requires an authenticated user, an available WUN page, a native empty-result message or results container, and a current search field matching the route query. It is removed on input changes, navigation or while no result container is available. A 700 ms delay accommodates the native search debounce. When additional sections appear, the suggestion stays at the end without duplication. It neither changes the native matching algorithm nor asserts that a title is absent: existing matches may differ from the intended title or sequel, and misspellings and native search failures may also lead to the native empty state.

Selecting the link opens the WUN page with an encoded `query` parameter. WUN selects Search, clears previous filters, selects All content types and searches once. The user can edit the text and inspect results before submitting a request. No request is created by the handoff, and no external metadata search runs before the user follows the suggestion. Queries are limited to the existing 150-character input limit. One-character links fill the field without submitting an invalid search.

The integration uses the native search page DOM; no web bundle patch, theme dependency or replacement of native search is required. Other client applications and future web versions with different selectors require separate compatibility validation.

Coverage: `tests/search_handoff_smoke.py` checks the native empty state, stale input, removed results, encoded punctuation/Unicode/HTML-like text, Portuguese/English, user/admin behavior, single automatic search, editable queries, and an additional handoff to an already-open WUN page. Existing header/page/interaction checks cover adjacent navigation and workflow behavior.
