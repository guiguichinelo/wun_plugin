import os
"""UI behavior with synthetic, in-memory API fixtures; no real accounts."""
from pathlib import Path
from playwright.sync_api import sync_playwright

script = Path(__file__).resolve().parents[1] / 'src/Wun.Web/app.js'
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(**({'channel':os.environ['WUN_BROWSER_CHANNEL']} if os.environ.get('WUN_BROWSER_CHANNEL') else {}), headless=True)
    for administrator in (False, True):
        page = browser.new_page()
        errors = []
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.set_content('<html lang="pt-BR"><section class="wun" id="wun-app" data-wun-requests></section></html>')
        page.evaluate("location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html'")
        page.evaluate('''admin => {
          window.Image=class {set src(value){queueMicrotask(()=>this.onload?.());}};
          window.calls=[];window.ApiClient={getCurrentUserId:()=> 'fixture-user',getUrl:p=>p,
            ajax:async req=>{calls.push(req);let path=req.url;
              if(path.endsWith('/me'))return {Administrator:admin};
              if(path.includes('/search?'))return [{Kind:'Movie',TmdbId:'42',Title:'<img src=x onerror=alert(1)>',Year:2020}];
              if(path.includes('/history'))return [];
              if(/\/admin\/requests\/1$/.test(path))return {Id:1,Selection:{Kind:'Series',TmdbId:'42',Title:'Fixture series'},Decision:0,Monitor:true,Revision:1};
              if(path.includes('/notifications'))return [];
              if(path.includes('/requests?'))return {Items:[{Id:1,Selection:{Kind:'Series',TmdbId:'42',Title:'Fixture series',Season:null,Episode:null},Decision:'Pending',Availability:'Unknown',Monitor:true,Revision:1}],HasMore:false};
              return {};
            }};
        }''', administrator)
        page.add_script_tag(path=str(script))
        page.evaluate("WunUI.mount(document.getElementById('wun-app'))")
        try:
            page.get_by_role('heading', name='Fixture series').wait_for(timeout=5000)
        except Exception:
            print(page.evaluate('({calls, text:document.body.innerText})'), errors)
            raise
        assert page.get_by_role('button', name='Solicitações gerais', exact=True).count() == int(administrator)
        if administrator:
            page.get_by_role('button', name='Quero também', exact=True).click()
            page.wait_for_function("calls.some(x=>x.type==='PUT'&&x.url.includes('/support?enabled=true'))")
        else:
            page.get_by_role('button', name='Acompanhar', exact=True).click()
            page.wait_for_function("calls.some(x=>x.type==='PUT'&&x.url.includes('/follow?enabled=true'))")
        page.get_by_role('button', name='Pesquisar', exact=True).click()
        page.get_by_label('Título ou identificação').fill('Fixture')
        # Simulate a host keyboard handler that cancels implicit form submission.
        page.evaluate("document.addEventListener('keydown',e=>{if(e.key==='Enter')e.preventDefault();})")
        count = page.evaluate("calls.filter(x=>x.url.includes('/search?')).length")
        page.locator('#wun-app input[type=search]').press('Enter')
        page.get_by_role('button', name='Solicitar', exact=True).wait_for()
        assert page.evaluate("calls.filter(x=>x.url.includes('/search?')).length") == count + 1
        assert page.locator('#wun-app img').count() == 0
        page.get_by_role('button', name='Solicitar', exact=True).click()
        page.locator('dialog').get_by_role('button', name='Salvar').click()
        page.wait_for_function("calls.some(x=>x.type==='POST'&&x.url.endsWith('/requests'))")
        if administrator:
            page.get_by_role('button', name='Solicitações gerais', exact=True).click()
            page.locator('article').get_by_role('button', name='Aprovar', exact=True).click()
            page.get_by_label('Decisão', exact=True).last.select_option('1')
            page.get_by_label('Nota interna').fill('Private fixture note')
            page.locator('dialog').get_by_role('button', name='Aprovar', exact=True).click()
            page.wait_for_function("calls.some(x=>x.type==='PUT'&&x.url.includes('/admin/requests/'))")
            page.evaluate('''()=>{window.fixtureIdentity='fixture-user';ApiClient.getCurrentUserId=()=>fixtureIdentity;const original=ApiClient.ajax;ApiClient.ajax=req=>req.url.includes('/history')?new Promise(resolve=>window.releaseHistory=resolve):original(req);}''')
            page.locator('article').get_by_role('button', name='Histórico').click()
            page.wait_for_function('typeof releaseHistory === "function"')
            page.evaluate("fixtureIdentity='different-user';document.body.append(document.createElement('div'))")
            page.wait_for_function('calls.filter(x=>x.url.endsWith("/me")).length>1')
            page.evaluate("releaseHistory([{Public:{Decision:1,Message:'PRIVATE_STALE_RESULT',Created:'2026-01-01T00:00:00Z'},InternalNote:'PRIVATE_STALE_RESULT'}])")
            page.wait_for_timeout(100)
            assert 'PRIVATE_STALE_RESULT' not in page.locator('#wun-app').inner_text()
        page.locator('#wun-app nav').get_by_role('button', name='Pesquisar', exact=True).click()
        page.evaluate('''()=>{const original=ApiClient.ajax;ApiClient.ajax=req=>{if(req.url.includes('/search?')){calls.push(req);return Promise.resolve([{Kind:'Movie',TmdbId:'78',ImdbId:'tt0083658',Title:'Catalog fixture',Year:1982,Genres:['Drama'],Rating:8.1,BackdropUrl:'https://image.tmdb.org/t/p/w1280/fixture.jpg',Catalog:{Obtained:true,ItemId:'12345678123412341234123456789abc'}}]);}return original(req);};}''')
        page.locator('#wun-app input[type=search]').fill('imdb:tt0083658')
        assert page.locator('[data-wun-multiselect]').get_attribute('data-value') == 'All'
        page.get_by_role('button', name='Tipo', exact=True).click()
        assert page.locator('.wun-type-check span').all_text_contents() == ['Todos', 'Filmes', 'Séries', 'Animes']
        page.get_by_role('button', name='Tipo', exact=True).click()
        toggle = page.get_by_role('button', name='Filtros', exact=True)
        assert toggle.get_attribute('aria-expanded') == 'false'
        toggle.click()
        assert toggle.get_attribute('aria-expanded') == 'true'
        page.get_by_label('Ano inicial', exact=True).fill('1980')
        page.get_by_label('Ano final', exact=True).fill('1990')
        page.get_by_label('Gêneros', exact=True).fill('Drama')
        page.get_by_label('Nota mínima', exact=True).fill('8')
        page.get_by_label('No seu catálogo', exact=True).select_option('yes')
        page.locator('#wun-app form button[type=submit]').click()
        page.locator('#wun-app article a').wait_for()
        assert page.locator('#wun-app article a').get_attribute('href') == 'https://www.imdb.com/title/tt0083658/'
        assert page.locator('#wun-app article a').get_attribute('rel') == 'noopener noreferrer'
        assert page.locator('#wun-app article').get_by_role('button', name='Solicitar', exact=True).count() == 0
        assert page.get_by_role('button', name='Abrir no catálogo').count() == 1
        assert page.locator('.wun-cover-status .material-icons').inner_text() == 'check_circle'
        assert page.locator('.wun-presence-badge').inner_text().endswith('No catálogo')
        assert page.evaluate("calls.some(x=>x.url.includes('yearFrom=1980')&&x.url.includes('genres=Drama')&&x.url.includes('minRating=8')&&x.url.includes('obtained=yes'))")
        assert page.evaluate("calls.some(x=>x.url.includes('/search?')&&x.url.includes('kind=All'))")
        page.get_by_role('button', name='Limpar filtros').click()
        assert page.get_by_label('Ano inicial', exact=True).input_value() == ''
        page.get_by_role('button', name='Tipo', exact=True).click()
        page.get_by_role('checkbox', name='Filmes', exact=True).check()
        page.get_by_role('checkbox', name='Animes', exact=True).check()
        assert page.locator('[data-wun-multiselect]').get_attribute('data-value') == 'Movie,Anime'
        count = page.evaluate("calls.filter(x=>x.url.includes('/search?')).length")
        page.get_by_role('checkbox', name='Séries', exact=True).press('Enter')
        assert page.locator('[data-wun-multiselect]').get_attribute('data-value') == 'Movie,Anime,Series'
        assert page.evaluate("calls.filter(x=>x.url.includes('/search?')).length") == count
        page.get_by_role('checkbox', name='Séries', exact=True).press('Enter')
        page.keyboard.press('Escape')
        page.locator('#wun-app input[type=search]').dispatch_event('keydown', {'key':'Enter','isComposing':True})
        assert page.evaluate("calls.filter(x=>x.url.includes('/search?')).length") == count
        page.locator('#wun-app form button[type=submit]').click()
        page.wait_for_function("calls.some(x=>x.url.includes('kind=Movie%2CAnime'))")
        page.wait_for_selector('#wun-page-backdrop', state='attached')
        page.evaluate("location.hash='/home'")
        page.wait_for_function("!document.body.classList.contains('wun-surface-active')&&!document.getElementById('wun-page-backdrop')")
        page.evaluate("location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html'")
        page.wait_for_selector('#wun-app article')
        assert page.locator('[data-wun-multiselect]').get_attribute('data-value') == 'Movie,Anime'
        assert page.locator('#wun-app input[type=search]').input_value() == 'imdb:tt0083658'
        page.wait_for_selector('#wun-page-backdrop', state='attached')
        assert errors == [], errors
        page.close()
    browser.close()
print('PASS: user/admin flows, privacy lifecycle, advanced filters, IMDb links and obtained titles')
