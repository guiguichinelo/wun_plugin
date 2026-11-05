"""Rich queue/notification UX and private administrative editing with synthetic fixtures."""
import os,json
from pathlib import Path
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
 b=p.chromium.launch(channel=os.environ.get('WUN_BROWSER_CHANNEL','msedge'),headless=True)
 for admin in [False,True]:
  page=b.new_page(viewport={'width':390,'height':844});errors=[]
  page.on('pageerror',lambda e:errors.append(str(e)))
  page.route('https://image.tmdb.org/**',lambda r:r.fulfill(content_type='image/svg+xml',body='<svg xmlns="http://www.w3.org/2000/svg" width="76" height="114"><rect width="76" height="114" fill="#6279cd"/></svg>'))
  page.set_content('<html lang="en"><section class="wun" id="wun-app"></section></html>');page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
  page.evaluate('''admin=>{
   location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';
   const person={Id:'00000000-0000-0000-0000-000000000001',Name:'Fixture person',HasImage:false};
   const card={Id:1,Selection:{Kind:'Movie',TmdbId:'42',Title:'Fixture title'},Year:2020,ImageUrl:'https://image.tmdb.org/t/p/w185/fixture.jpg',Decision:0,Availability:1,Revision:1,SupportCount:2,Supporting:false,Following:false,Created:'2026-01-01T12:00:00Z',Requester:person,RequesterKnown:true,Supporters:[person],LastDecision:{Public:{Message:'Existing reply'},InternalNote:'Private fixture note',ReviewAt:'2026-02-01T12:00:00Z'}};
   window.calls=[];let seen=false;
   window.ApiClient={getCurrentUserId:()=> 'fixture',getUrl:p=>p,getItems:async()=>({Items:[]}),ajax:async r=>{
    calls.push(r);const url=r.url;
    if(url.endsWith('/me'))return {Administrator:admin};
    if(url.includes('/notifications/7/read')){seen=true;return {Read:true};}
    if(url.includes('/notifications?'))return [{Id:7,RequestId:1,Event:'Approved',Read:seen,Created:'2026-01-02T12:00:00Z',Message:'Ready for review',Request:card,Actor:admin?person:null}];
    if(url.includes('/support?')){card.Supporting=url.includes('enabled=true');card.SupportCount=card.Supporting?3:2;return {Supporting:card.Supporting,SupportCount:card.SupportCount};}
    if(url.includes('/requests?'))return {Items:[card],HasMore:false};
    if(/requests\/1$/.test(url)){if(r.type==='PUT'){card.Decision=JSON.parse(r.data).decision;return {Saved:true};}return card;}
    return [];
   }};
  }''',admin)
  page.add_script_tag(path=str(root/'src/Wun.Web/app.js'));page.evaluate('WunUI.mount(document.querySelector("#wun-app"))')
  page.locator('.wun-request-card').wait_for();assert page.locator('.wun-request-cover img').count()==1
  if admin:
   page.get_by_role('button',name='Approve',exact=True).click()
   page.locator('dialog').wait_for()
   assert page.get_by_label('Reply visible to followers').input_value()=='Existing reply'
   assert page.get_by_label('Internal note').input_value()=='Private fixture note'
   assert page.get_by_label('Review date (optional)').input_value()=='2026-02-01'
   assert page.get_by_label('Decision',exact=True).last.input_value()=='1'
   page.locator('dialog').get_by_role('button',name='Cancel').click()
   page.get_by_role('button',name='Decline',exact=True).click()
   page.locator('dialog').get_by_role('button',name='Decline',exact=True).click()
   page.wait_for_function("calls.some(r=>r.type==='PUT'&&r.url.includes('/admin/requests/1')&&JSON.parse(r.data).decision===3)")
  else:
   assert page.get_by_role('button',name='Approve',exact=True).count()==0
   assert page.get_by_text('Fixture person',exact=True).count()==0
  page.get_by_role('button',name='Want this too',exact=True).click()
  page.get_by_role('button',name='Supported',exact=True).wait_for()
  assert page.get_by_role('button',name='Supported',exact=True).get_attribute('aria-pressed')=='true'
  page.get_by_role('button',name='Notifications',exact=True).click()
  notice=page.locator('.wun-notice');notice.wait_for()
  assert notice.get_by_text('Fixture title',exact=True).is_visible()
  assert notice.get_by_text('Ready for review',exact=True).is_visible()
  notice.get_by_role('button',name='View request',exact=True).click();page.locator('.wun-request-dialog').wait_for()
  page.locator('.wun-request-dialog').get_by_role('button',name='Cancel').click()
  notice.get_by_role('button',name='Mark as read',exact=True).click()
  page.wait_for_function("calls.some(r=>r.url.includes('/notifications/7/read'))")
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
  assert not errors,errors
  page.close()
 b.close()
print('PASS: personal/administrative views, support, rich notices, preserved editor values, explicit decisions and mobile layout')
