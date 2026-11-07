"""Regression coverage for queue decisions, read groups, search continuity and title ownership."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
 browser=p.chromium.launch(channel=os.environ.get('WUN_BROWSER_CHANNEL','msedge'),headless=True)
 page=browser.new_page(viewport={'width':390,'height':844});errors=[]
 page.on('pageerror',lambda e:errors.append(str(e)))
 page.set_content('<html lang="en"><head><title>Home</title></head><body><section id="wun-app" class="wun"></section></body></html>')
 page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
 page.evaluate('''()=>{
  location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';window.calls=[];window.state=0;window.seen=false;
  const card=()=>({Id:1,Selection:{Kind:'Movie',TmdbId:'42',Title:'Fixture'},Decision:state,Revision:state+1,SupportCount:1,Supporting:true,Following:true});
  ApiClient={getCurrentUserId:()=> 'fixture',getItems:async()=>({Items:[]}),getUrl:p=>p,ajax:async r=>{
   calls.push(r);const u=new URL(r.url,'http://fixture/');
   if(u.pathname.endsWith('/me'))return {Administrator:true};
   if(u.pathname.endsWith('/search'))return [{Kind:'Movie',TmdbId:'43',Title:'New fixture'}];
   if(u.pathname.endsWith('/requests')&&r.type==='POST')return {...card(),Id:2,Selection:{Kind:'Movie',TmdbId:'43'},Supporting:true};
   if(u.pathname.endsWith('/notifications/7/read')){seen=true;return {Read:true};}
   if(u.pathname.endsWith('/notifications'))return (u.searchParams.get('read')==='true')===seen?[{Id:7,RequestId:1,Read:seen,Event:'request',Request:card()}]:[];
   if(u.pathname.endsWith('/admin/requests/1')){if(r.type==='PUT'){state=JSON.parse(r.data).decision;return {Saved:true};}return card();}
   if(u.pathname.endsWith('/requests'))return {Items:u.searchParams.has('decision')&&Number(u.searchParams.get('decision'))!==state?[]:[card()],HasMore:false};
   return {};
  }}; }''')
 page.add_script_tag(path=str(root/'src/Wun.Web/app.js'));page.evaluate('WunUI.mount(document.querySelector("#wun-app"))')
 page.wait_for_function('document.title==="Requests"');page.locator('.wun-request-card').wait_for()
 assert page.get_by_label('Decision',exact=True).input_value()==''
 page.get_by_label('Decision',exact=True).select_option('0')
 page.get_by_role('button',name='Approve',exact=True).click();page.locator('dialog').get_by_role('button',name='Approve',exact=True).click()
 page.wait_for_function('window.state===1&&document.querySelectorAll(".wun-request-card").length===0')
 page.get_by_label('Decision',exact=True).select_option('1');page.locator('.wun-request-card').wait_for()
 assert page.get_by_role('button',name='Approve',exact=True).count()==0
 assert page.get_by_role('button',name='Change decision',exact=True).is_visible()
 # A delayed refresh retains current controls/cards until its replacement is ready.
 page.evaluate('''()=>{window.originalAjax=ApiClient.ajax;ApiClient.ajax=r=>{
   if(r.type==='GET'&&r.url.includes('/requests?')&&!window.delayedOnce){window.delayedOnce=true;return new Promise(resolve=>window.releaseRefresh=()=>originalAjax(r).then(resolve));}
   return originalAjax(r);
 };window.originalFilter=document.querySelector('.wun-list-filters select');}''')
 page.get_by_role('button',name='Refresh',exact=True).click()
 page.wait_for_function('!!window.releaseRefresh')
 assert page.locator('.wun-request-card').count()==1
 assert page.evaluate('window.originalFilter===document.querySelector(".wun-list-filters select")')
 page.evaluate('releaseRefresh()')
 page.wait_for_function('window.originalFilter!==document.querySelector(".wun-list-filters select")')
 page.evaluate('()=>{ApiClient.ajax=window.originalAjax;}')
 page.get_by_role('button',name='Refresh',exact=True).click();page.locator('.wun-request-card').wait_for()
 assert page.get_by_label('Decision',exact=True).input_value()=='1'
 page.get_by_role('button',name='Notifications',exact=True).click();page.locator('.wun-notice').wait_for()
 page.get_by_role('button',name='Mark as read',exact=True).click();page.wait_for_function('window.seen&&document.querySelectorAll(".wun-notice").length===0')
 page.get_by_role('button',name='Read',exact=True).click();page.locator('.wun-notice').wait_for()
 assert page.get_by_role('button',name='Mark as read',exact=True).count()==0
 page.locator('#wun-app nav').get_by_role('button',name='Search',exact=True).click()
 page.get_by_role('button',name='Type',exact=True).click();page.locator('.wun-type-check span').filter(has_text='Movies').click()
 assert page.locator('[data-wun-multiselect]').get_attribute('data-value')=='Movie',page.locator('[data-wun-multiselect]').get_attribute('data-value')
 page.keyboard.press('Escape');page.locator('input[type=search]').fill('Fixture');page.locator('form button[type=submit]').click()
 page.locator('article').get_by_role('button',name='Request',exact=True).click();page.locator('dialog').get_by_role('button',name='Save',exact=True).click()
 page.wait_for_function('calls.some(r=>r.type==="POST"&&r.url.endsWith("/requests"))')
 page.get_by_role('button',name='Supported',exact=True).wait_for()
 assert page.locator('input[type=search]').input_value()=='Fixture'
 assert page.locator('[data-wun-multiselect]').get_attribute('data-value')=='Movie'
 assert page.locator('#wun-app nav [aria-current=page]').inner_text()=='Search'
 page.evaluate("location.hash='/home';document.title='Home'")
 page.wait_for_function('document.title==="Home"&&!document.body.classList.contains("wun-surface-active")')
 assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
 assert not errors,errors
 browser.close()
print('PASS: title lifecycle, all-status default and explicit pending filter, decision actions, refresh filters, read groups, full-row type click and search continuity')
