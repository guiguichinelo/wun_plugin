import os
"""Jellyfin 12.4 UI locales: Portuguese, English fallback, RTL and mobile controls."""
import json,re
from pathlib import Path
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[1]
locales=json.loads((root/'tests/native-locales.json').read_text())
with sync_playwright() as p:
    browser=p.chromium.launch(**({'channel':os.environ['WUN_BROWSER_CHANNEL']} if os.environ.get('WUN_BROWSER_CHANNEL') else {}),headless=True)
    for locale in locales:
        page=browser.new_page(viewport={'width':390,'height':844})
        errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
        direction='rtl' if re.match(r'^(ar|fa|he|ur)(-|$)',locale) else 'ltr'
        page.set_content(f'<html lang="{locale}" dir="{direction}"><section id="wun-app" class="wun" data-wun-requests></section></html>')
        page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
        page.evaluate("""()=>{
            location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';
            window.ApiClient={getCurrentUserId:()=> 'fixture',getUrl:path=>path,
                getItems:async()=>({Items:[]}),ajax:async x=>x.url.endsWith('/me')?{Administrator:false}:x.url.endsWith('/notifications')?[]:{Items:[],HasMore:false}};
        }""")
        page.add_script_tag(path=str(root/'src/Wun.Web/app.js'))
        page.evaluate('WunUI.mount(document.querySelector("#wun-app"))')
        page.locator('#wun-app nav button').nth(1).click()
        expected='Solicitações' if locale.startswith('pt') else 'Requests'
        assert page.locator('#wun-app h1').inner_text()==expected,locale
        assert page.locator('#wun-app nav button').count()==3
        page.locator('.wun-filters-toggle').click()
        assert page.locator('#wun-advanced-filters').is_visible()
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),locale
        assert not errors,(locale,errors)
        page.close()
    browser.close()
print('PASS: 64 host locales, Portuguese and English fallback, RTL/mobile, regular-user tabs')
