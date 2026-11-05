"""Native dashboard configuration: localization, save/refresh, safe text, mobile and errors."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

html=(Path(__file__).resolve().parents[1]/'src/Wun.Web/config.html').read_text(encoding='utf-8')
with sync_playwright() as p:
 browser=p.chromium.launch(channel=os.environ.get('WUN_BROWSER_CHANNEL','msedge'),headless=True)
 for lang in ['pt-BR','en-US','de','ar']:
  page=browser.new_page(viewport={'width':390,'height':844});errors=[]
  page.on('pageerror',lambda error:errors.append(str(error)))
  page.add_init_script('')
  page.set_content('<html lang="'+lang+'"></html>')
  page.evaluate('''()=>{
   window.saved=null;window.fail=false;window.calls=0;
   window.ApiClient={getUrl:p=>p,getPluginConfiguration:async()=>({PageSize:30,AdministratorOrder:'popular',OtherSetting:true}),updatePluginConfiguration:async(id,c)=>{if(fail)throw Error('fixture');saved=c;},ajax:async r=>{if(r.url.includes('/maintenance/preview'))return {Candidates:{Requests:[],Notifications:[]},LastCleanup:null};if(r.url==='ScheduledTasks')return [];calls++;if(fail)throw Error('fixture');return {Total:2,Pending:1,Approved:0,Deferred:1,Rejected:0,Movies:1,Series:1,Supports:3,InterestedUsers:2,Monitored:1,ReviewsDue:1,UnknownRequesters:0,Completed:0,Canceled:0,GeneratedAt:'2026-01-01T12:00:00Z',MostWanted:[{Title:'<img src=x onerror=alert(1)>',Kind:1,Season:2,Episode:3,Supports:2}]};}};
  }''')
  # Keep the synthetic host language while injecting the real page fragment.
  page.evaluate('(html)=>{const parsed=new DOMParser().parseFromString(html,"text/html");document.body.innerHTML=parsed.body.innerHTML;for(const s of document.body.querySelectorAll("script")){const script=document.createElement("script");script.textContent=s.textContent;s.replaceWith(script);}}',html)
  page.evaluate('document.querySelector("#wunConfigurationPage").dispatchEvent(new Event("pageshow"))')
  page.wait_for_function('document.querySelectorAll(".wun-metric").length===14')
  assert page.locator('[data-ranking] img').count()==0
  assert page.locator('[data-ranking]').inner_text().startswith('<img src=x')
  page.get_by_role('tab',name='Pedidos' if lang=='pt-BR' else 'Requests',exact=True).click()
  page.locator('#wun-config-panel-1 details summary').click()
  assert page.locator('[data-text=save]').inner_text()==('Salvar' if lang=='pt-BR' else 'Save')
  page.locator('#wunPageSize').fill('50');page.locator('#wunAdminOrder').select_option('newest')
  page.locator('button[type=submit]').click()
  page.wait_for_function('saved?.PageSize===50&&saved.AdministratorOrder==="newest"&&saved.OtherSetting')
  page.get_by_role('tab',name='Vis\u00e3o geral' if lang=='pt-BR' else 'Overview',exact=True).click()
  page.locator('[data-refresh]').click();page.wait_for_function('calls===2')
  page.get_by_role('tab',name='Pedidos' if lang=='pt-BR' else 'Requests',exact=True).click()
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
  page.get_by_role('tab',name='Limpeza e rotinas' if lang=='pt-BR' else 'Cleanup and tasks',exact=True).click()
  assert page.locator('#wunCompletedDays').is_disabled()
  page.locator('#wunEnableCompletedDays').check();page.locator('#wunCompletedDays').fill('14')
  page.get_by_role('tab',name='Pedidos' if lang=='pt-BR' else 'Requests',exact=True).click()
  page.get_by_role('tab',name='Limpeza e rotinas' if lang=='pt-BR' else 'Cleanup and tasks',exact=True).click()
  assert page.locator('#wunCompletedDays').input_value()=='14'
  page.once('dialog',lambda dialog:dialog.accept());page.locator('button[type=submit]').click()
  page.wait_for_function('saved?.CompletedDays===14&&saved.AcceptRequests===true&&saved.ActiveLimit===0')
  page.locator('#wunEnableCompletedDays').uncheck()
  page.evaluate('window.fail=true');page.locator('button[type=submit]').click()
  page.wait_for_function('document.querySelector("[data-config-status]").textContent.includes(document.documentElement.lang==="pt-BR"?"Não foi possível salvar":"Could not save")')
  assert page.locator('#wunPageSize').input_value()=='50'
  page.get_by_role('tab',name='Vis\u00e3o geral' if lang=='pt-BR' else 'Overview',exact=True).click()
  page.locator('[data-refresh]').click();page.wait_for_function('document.querySelectorAll(".wun-metric").length===0')
  assert not errors,errors
  page.close()
 browser.close()
print('Configuration smoke passed: PT/EN/fallback, metrics, save, errors, safe text and mobile.')
