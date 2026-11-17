"""Filters are usable before the queue responds; stale responses never replace a newer choice."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
with sync_playwright()as p:
 browser=p.chromium.launch(channel=os.environ.get('WUN_BROWSER_CHANNEL','msedge'),headless=True)
 for admin in [True,False]:
  page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.set_content('<html lang=en><body><section id=wun-app class=wun></section></body></html>');page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
  page.evaluate('''admin=>{location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';window.loads=[];window.ApiClient={getCurrentUserId:()=> 'fixture',getItems:async()=>({Items:[]}),getUrl:p=>p,ajax:async r=>{if(r.url.endsWith('/me'))return {Administrator:admin};if(r.url.includes('/requests?'))return new Promise(resolve=>loads.push({url:r.url,resolve}));return [];}};}''',admin)
  page.add_script_tag(path=str(root/'src/Wun.Web/app.js'));page.evaluate('WunUI.mount(document.querySelector("#wun-app"))');page.locator('.wun-list-filters').wait_for();page.wait_for_function('loads.length===1')
  control=page.get_by_label('Decision',exact=True);assert control.input_value()==('0'if admin else'')
  assert page.locator('.wun-request-card').count()==0
  assert page.evaluate('new URL(loads[0].url,"http://fixture").searchParams.get("decision")')==('0'if admin else None)
  control.select_option('1');page.wait_for_function('loads.length===2')
  page.evaluate('''()=>loads[1].resolve({Items:[{Id:2,Selection:{Kind:'Movie',TmdbId:'42',Title:'Approved fixture'},Decision:1,Revision:1,SupportCount:1}],HasMore:false})''');page.locator('.wun-request-card').wait_for();assert control.input_value()=='1'
  page.evaluate('''()=>loads[0].resolve({Items:[{Id:1,Selection:{Kind:'Movie',TmdbId:'43',Title:'Stale fixture'},Decision:0,Revision:1,SupportCount:1}],HasMore:false})''');page.wait_for_timeout(50);assert 'Stale fixture'not in page.locator('#wun-app').inner_text();assert control.input_value()=='1';assert not errors,errors;page.close()
 browser.close()
print('PASS: immediate filters, pending administrator default, personal-view default preserved and stale response ignored')
