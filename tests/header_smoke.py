import os
"""Independent browser check: python tests/header_smoke.py (Playwright + Edge)."""
from pathlib import Path
from playwright.sync_api import sync_playwright

script = Path(__file__).resolve().parents[1] / 'src/Wun.Web/header.js'
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(**({'channel':os.environ['WUN_BROWSER_CHANNEL']} if os.environ.get('WUN_BROWSER_CHANNEL') else {}), headless=True)
    for ready, modern in ((False,False),(True,False),(False,True),(True,True)):
        page = browser.new_page()
        page.goto('about:blank')
        page.set_content('<html lang="pt-BR"><div class="skinHeader"><div class="headerRight"><button class="headerSyncButton">Sync</button><button class="headerCastButton">Cast</button></div></div></html>')
        if modern:
            page.evaluate('''()=>{document.body.innerHTML='<header><div class="headerRight"><button class="MuiIconButton-root" aria-controls="app-sync-play-menu">Sync</button><button aria-controls="app-remote-play-menu">Cast</button></div></header>';}''')
        # about:blank cannot resolve URLs; use a real base URL without network access.
        page.evaluate("document.head.innerHTML='<base href=\"http://localhost/web/index.html\">'")
        page.evaluate('''ready => { window.fetch = async () => ({ok:ready,text:async()=>'<section data-wun-requests></section>'}); }''', ready)
        # Resolve the development URL without depending on a running Jellyfin server.
        source = script.read_text(encoding='utf-8').replace("location.href.split('#')[0]", "'http://localhost/web/index.html'")
        page.add_script_tag(content=source)
        button = page.locator('[data-wun-header]')
        button.wait_for()
        page.wait_for_function('(ready)=>document.querySelector("[data-wun-header]").disabled===!ready', arg=ready)
        assert button.count() == 1
        assert button.locator('svg[data-wun-icon="desk-bell"] path').count() == 1
        assert page.locator('.headerRight > :first-child').get_attribute('data-wun-header') == 'true'
        assert button.get_attribute('title') is None
        assert ('desenvolvimento' in button.get_attribute('aria-label')) == (not ready)
        page.add_script_tag(content=source)
        assert button.count() == 1
        page.evaluate("document.documentElement.lang='en-US'")
        page.wait_for_function('document.querySelector("[data-wun-header]").getAttribute("aria-label").startsWith("Requests")')
        if ready:
            button.hover();page.get_by_role('tooltip').wait_for()
            assert page.get_by_role('tooltip').inner_text()=='Requests'
            assert button.get_attribute('aria-describedby')=='wun-header-tooltip'
            page.keyboard.press('Escape');page.get_by_role('tooltip').wait_for(state='detached')
        page.evaluate('''modern => {document.querySelector('.headerRight').outerHTML=modern?'<div class="headerRight"><button class="MuiIconButton-root" aria-controls="app-sync-play-menu">Sync</button></div>':'<div class="headerRight"><button class="headerSyncButton">Sync</button></div>';}''', modern)
        button.wait_for()
        assert button.count() == 1
        if ready:
            button.click()
            assert 'pageUrl=%2FWUN%2Fweb%2Frequests.html' in page.url
        page.close()
    browser.close()
print('PASS: disabled/ready states, order, idempotence, language, header replacement and navigation')
