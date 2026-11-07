"""Episode requests retain independent actions inside season groups."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
 b=p.chromium.launch(channel=os.environ.get('WUN_BROWSER_CHANNEL','msedge'),headless=True)
 for lang in ['en','pt-BR']:
  for color in ['#151515','#ffffff']:
   page=b.new_page(viewport={'width':390,'height':844});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
   page.set_content('<html lang="'+lang+'"><style>body{background:'+color+';color:'+('#fff' if color=='#151515' else '#111')+'}</style><section id="wun-app" class="wun"></section></html>');page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
   page.evaluate("""()=>{location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';window.Image=class{set src(v){queueMicrotask(()=>this.onload?.())}};window.calls=[];window.ApiClient={getCurrentUserId:()=> 'fixture',getUrl:p=>p,getItems:async()=>({Items:[]}),ajax:async r=>{calls.push(r);if(r.url.endsWith('/me'))return {Administrator:true};if(r.url.includes('/requests?'))return {Items:[4,5,6].map(n=>({Id:n,Selection:{Kind:'Series',TmdbId:'42',Title:'Example series',Season:2,Episode:n},Decision:(window.fixtureDecision||'Approved'),Availability:'Available',Current:true,Catalog:{Obtained:true,ItemId:'a'.repeat(32),EpisodeItemId:String(n).repeat(32),EpisodeCount:1},Revision:1})),HasMore:false};return [];}};}""")
   page.evaluate("window.setInterval=(cb,ms)=>{if(ms===30000)window.fixturePoll=cb;return 1;}")
   page.add_script_tag(path=str(root/'src/Wun.Web/app.js'));page.evaluate("WunUI.mount(document.getElementById('wun-app'))")
   page.wait_for_selector('.wun-request-group');assert page.locator('.wun-request-group').count()==1;assert page.locator('[data-request-id]').count()==3
   text=page.locator('#wun-app').inner_text();assert '1 episodes' not in text and '1 epis' not in text;assert 'Em dia' not in text and 'Up to date' not in text
   assert page.locator('.wun-presence-partial').count()==0
   assert page.locator('.wun-request-card').first.evaluate('(r)=>r.querySelector(".wun-request-body").getBoundingClientRect().width>=r.getBoundingClientRect().width*.8')
   page.locator('.wun-list-filters select').first.focus();before=page.evaluate('calls.length');page.evaluate("window.fixtureDecision='Completed';window.fixturePoll();");assert page.evaluate('calls.length')==before
   page.evaluate('document.activeElement.blur();window.fixturePoll()');page.wait_for_function("[...document.querySelectorAll('.wun-decision-badge')].length===3&&[...document.querySelectorAll('.wun-decision-badge')].every(n=>/Completed|Finalizado/.test(n.textContent))")
   assert ' ? ' not in text
   assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
   label='Abrir epis'+chr(243)+'dio' if lang=='pt-BR' else 'Open episode';page.get_by_role('button',name=label,exact=True).first.click();assert ('id='+('4'*32)) in page.evaluate('location.hash')
   assert not page.evaluate("calls.some(c=>c.type&&c.type!=='GET')");assert not errors,errors;page.close()
 b.close()
print('PASS: grouped episodes, exact links, independent rows, availability, PT/EN, light/dark and narrow layout')
