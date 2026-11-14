"""Native empty search -> WUN, without requests or metadata calls before opt-in."""
import os
from pathlib import Path
from urllib.parse import urlencode
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[1]
with sync_playwright()as p:
 browser=p.chromium.launch(**({'channel':os.environ['WUN_BROWSER_CHANNEL']}if os.environ.get('WUN_BROWSER_CHANNEL')else{}),headless=True)
 for language,administrator in [('pt-BR',False),('en',True)]:
  page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
  page.route('http://wun.test/**',lambda r:r.fulfill(content_type='text/html',body='<section data-wun-requests></section>'))
  page.goto('http://wun.test/web/')
  page.set_content('<html><head></head><body><div id="searchPage"><input id="searchTextInput"><div class="noItemsMessage centerMessage">No matches</div></div><section id="wun-app" hidden></section></body></html>')
  page.evaluate('''admin=>{document.documentElement.lang=admin?'en':'pt-BR';window.calls=[];window.who='user';window.ApiClient={getCurrentUserId:()=>who,getItems:async()=>({Items:[]}),getUrl:p=>p,ajax:async r=>{calls.push(r);if(r.url.endsWith('/me'))return {Administrator:admin};if(r.url.includes('/search?'))return [];return {Items:[],HasMore:false};}};}''',administrator)
  page.add_script_tag(path=str(root/'src/Wun.Web/header.js'))
  page.add_script_tag(path=str(root/'src/Wun.Web/app.js'))
  query='Coração & <img src=x onerror=alert(1)> + 夜 / tt123'
  page.evaluate('q=>{location.hash="/search?"+new URLSearchParams({query:q});document.querySelector("input").value=q;}',query)
  link=page.locator('[data-wun-search-suggestion] a');link.wait_for()
  assert link.inner_text()==('Solicitar'if language=='pt-BR'else'Request')
  assert page.evaluate('calls.length')==0
  assert page.locator('[data-wun-search-suggestion] img').count()==0
  # No stale prompt while typing/loading; keep it at the end of populated results.
  page.locator('#searchTextInput').fill('Other title');page.wait_for_timeout(800);assert link.count()==0
  page.evaluate('q=>{document.querySelector("input").value=q;document.querySelector(".noItemsMessage").remove();}',query)
  page.wait_for_timeout(100);assert link.count()==0
  page.evaluate('()=>{const d=document.createElement("div");d.className="noItemsMessage";d.textContent="No results";document.querySelector("#searchPage").append(d);}')
  link.wait_for();assert page.locator('[data-wun-search-suggestion]').count()==1
  page.evaluate('()=>{document.querySelector(".noItemsMessage").remove();const d=document.createElement("div");d.className="searchResults";d.innerHTML="<section>Matching title</section>";document.querySelector("#searchPage").append(d);}')
  page.locator('.searchResults [data-wun-search-suggestion]').wait_for()
  assert page.locator('.searchResults').evaluate('(d)=>d.lastElementChild.matches("[data-wun-search-suggestion]")')
  page.evaluate('()=>{const d=document.createElement("section");d.textContent="Another matching title";document.querySelector(".searchResults").append(d);}')
  page.wait_for_function('document.querySelector(".searchResults").lastElementChild.matches("[data-wun-search-suggestion]")')
  assert page.locator('[data-wun-search-suggestion]').count()==1
  link.click()
  page.evaluate('()=>{document.querySelector("#searchPage").hidden=true;const root=document.querySelector("#wun-app");root.hidden=false;WunUI.mount(root);}')
  page.wait_for_function('calls.some(r=>r.url.includes("/search?"))')
  assert page.locator('#wun-app input[type=search]').input_value()==query
  request=page.evaluate('calls.find(r=>r.url.includes("/search?")).url')
  from urllib.parse import urlsplit,parse_qs
  params=parse_qs(urlsplit(request).query);assert params['query']==[query]and params['kind']==['All']
  page.wait_for_timeout(900);assert page.evaluate('calls.filter(r=>r.url.includes("/search?")).length')==1
  assert not page.evaluate('calls.some(r=>r.type==="POST")')
  page.locator('#wun-app input[type=search]').fill('Corrected title');page.locator('#wun-app input[type=search]').press('Enter')
  page.wait_for_function('calls.filter(r=>r.url.includes("/search?")).length===2')
  # A direct link for a different query updates an already-mounted WUN page once.
  page.evaluate('()=>{location.hash="/userpluginsettings.html?"+new URLSearchParams({pageUrl:"/WUN/web/requests.html",query:"Another title"});}')
  page.wait_for_function('calls.some(r=>r.url.includes("query=Another+title"))')
  assert page.locator('#wun-app input[type=search]').input_value()=='Another title',page.evaluate('calls.filter(r=>r.url.includes("/search?")).map(r=>r.url)')
  assert not errors,errors
  page.close()
 browser.close()
print('PASS: empty and populated results, footer order, input race, escaping, locales, opt-in, single search, admin/user')
