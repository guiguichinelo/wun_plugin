import os
"""Details for complete/canceled series, ongoing series with gaps, and films."""
from pathlib import Path
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[1]
with sync_playwright() as p:
 b=p.chromium.launch(**({'channel':os.environ['WUN_BROWSER_CHANNEL']} if os.environ.get('WUN_BROWSER_CHANNEL') else {}),headless=True)
 for mode in ['complete','ongoing','movie']:
  page=b.new_page(viewport={'width':390,'height':844});errors=[]
  page.route('https://image.tmdb.org/**',lambda route:route.fulfill(content_type='image/svg+xml',body='<svg xmlns="http://www.w3.org/2000/svg" width="300" height="169"><rect width="300" height="169" fill="#6279cd"/></svg>'))
  page.on('pageerror',lambda e:errors.append(str(e)))
  page.set_content('<html lang="en-us"><section id="wun-app" class="wun" data-wun-requests></section></html>')
  page.add_style_tag(path=str(root/'src/Wun.Web/style.css'))
  page.evaluate("""mode=>{
   location.hash='/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html';
   const kind=mode==='movie'?'Movie':'Series';window.calls=[];
   window.ApiClient={getCurrentUserId:()=> 'fixture',getUrl:p=>p,getItems:async()=>({Items:[]}),ajax:async r=>{
    calls.push(r);if(r.url.endsWith('/me'))return {Administrator:false};
    if(r.url.includes('/search?'))return [{Kind:kind,TmdbId:'42',Title:'Fixture title'}];
    if(r.url.includes('/details?'))return {Kind:kind,Overview:'A synopsis.',Year:2020,RuntimeMinutes:90,ProviderKnown:true,Complete:mode==='complete',CompleteReleased:mode==='complete',Ended:mode==='complete',Owned:mode==='complete'?2:1,Released:2,Expected:3,SelectedSeason:1,Seasons:[{Number:1,Expected:3}],Episodes:[{Number:1,Title:'One',Overview:'Episode synopsis',Available:true,Released:true,AirDate:'2020-01-01',ItemId:'abc'},{Number:2,Title:'Two',Available:mode==='complete',Released:true,AirDate:'2020-01-02'},{Number:3,Title:'Next',Available:false,Released:false,AirDate:'2099-01-01'}]};
    return {Items:[],HasMore:false};
   }};
  }""",mode)
  page.add_script_tag(path=str(root/'src/Wun.Web/app.js'));page.evaluate('WunUI.mount(document.querySelector("#wun-app"))')
  page.evaluate("()=>{const original=ApiClient.ajax;ApiClient.ajax=async request=>{const data=await original(request);if(request.url.includes('/details?')){data.ImageUrl='https://image.tmdb.org/t/p/w342/fixture.jpg';data.Seasons[0].ImageUrl=data.ImageUrl;data.Seasons.push({Number:2,Expected:3,ImageUrl:data.ImageUrl});data.SelectedSeason=Number(new URL(request.url,'http://fixture').searchParams.get('season')||1);data.Episodes[0].ImageUrl='https://image.tmdb.org/t/p/w300/fixture.jpg';data.Episodes[1].ImageUrl='https://untrusted.example/fixture.jpg';}return data;};}")
  page.locator('#wun-app nav button').nth(2).click();page.locator('input[type=search]').fill('fixture');page.locator('input[type=search]').press('Enter')
  page.get_by_role('button',name='Details',exact=True).click();modal=page.locator('.wun-details');modal.get_by_text('A synopsis.',exact=True).wait_for()
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
  assert modal.locator('.wun-title-cover img').count()==1
  if mode=='movie':assert modal.get_by_text('2020 · 90 min',exact=True).is_visible()
  else:
   assert modal.locator('.wun-episode').count()==3
   assert modal.locator('.wun-episode-art img').count()==1
   assert modal.locator('.wun-season-cover img').count()==2
   modal.locator('.wun-season-option').nth(1).click()
   page.wait_for_function('document.querySelectorAll(".wun-season-option")[1].getAttribute("aria-pressed")==="true"')
   modal.locator('.wun-season-option').nth(0).click()
   page.wait_for_function('document.querySelectorAll(".wun-season-option")[0].getAttribute("aria-pressed")==="true"')
   top=modal.locator('.wun-details-heading').bounding_box()['y']
   modal.locator('.wun-details-content').evaluate('e=>e.scrollTop=200')
   assert modal.locator('.wun-details-heading').bounding_box()['y']==top
   page.wait_for_function('document.querySelector(".wun-episode-art img").naturalWidth===300')
   assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
   assert modal.get_by_role('button',name='Request monitoring for new episodes').count()==int(mode=='ongoing')
   if mode=='complete':assert modal.get_by_text('Complete in library',exact=True).is_visible()
   else:
    modal.locator('.wun-episode').nth(1).get_by_role('button',name='Request',exact=True).click()
    request=page.locator('dialog').last
    assert request.locator('input').nth(0).input_value()=='1'
    assert request.locator('input').nth(1).input_value()=='2'
  assert not errors,errors
  page.close()
 b.close()
print('PASS: series coverage, monitoring, future dates, scoped episode request, film synopsis and mobile')
