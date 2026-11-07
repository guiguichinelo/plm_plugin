"""Unavailable titles remain removable, private to a list and correctly paginated."""
from web_fixture import *

with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,**({'channel':os.environ['PLM_BROWSER_CHANNEL']} if os.environ.get('PLM_BROWSER_CHANNEL') else {}))
    page=browser.new_page(viewport={'width':1280,'height':900})
    page.route('http://plm-fixture.test/**',handle)
    page.goto('http://plm-fixture.test/web/#/home')
    page.wait_for_function('window.PLM?.interactionsReady && window.PLM.i18n.loaded')
    page.evaluate('''()=>{
      const entries=Array.from({length:41},(_,i)=>({ItemId:(i+1).toString(16).padStart(32,'0'),Position:i,Media:{Status:'unavailable',Name:'Remembered title '+(i+1),Kind:'Movie'}}));
      fixture.lists.push({Id:'22222222222222222222222222222222',Name:'Saved',Revision:0},{Id:'33333333333333333333333333333333',Name:'Other',Revision:0});
      fixture.selections[fixture.lists[1].Id]=entries;
      fixture.selections[fixture.lists[2].Id]=[structuredClone(entries[40])];
      location.hash=PLM.routes.library+'&list='+fixture.lists[1].Id;
    }''')
    page.locator('.plm-unavailable-card').first.wait_for()
    assert page.locator('.plm-unavailable-card').count()==40
    page.locator('#plm-next').click()
    page.wait_for_function("document.querySelectorAll('.plm-unavailable-card').length===1")
    card=page.locator('.plm-unavailable-card')
    assert card.locator('.plm-card-title').inner_text()=='Remembered title 41'
    remove=card.locator('.plm-remove-unavailable')
    assert remove.get_attribute('aria-label').endswith('Remembered title 41')
    assert remove.evaluate('e=>getComputedStyle(e.parentElement).opacity')=='1'
    # A stale revision must leave the selection and usable removal control intact.
    page.evaluate('''()=>{const original=ApiClient.ajax;let conflict=true;ApiClient.ajax=async r=>{if(conflict&&r.type==='DELETE'&&r.url.includes('/items/')){conflict=false;throw {status:409};}return original(r);};}''')
    remove.click()
    page.wait_for_function("document.querySelector('#plm-status').getAttribute('role')==='alert'")
    assert not remove.is_disabled()
    assert page.evaluate("!fixture.selections[fixture.lists[1].Id][40].DeletedAt")
    # Locale comes from the host's user interface preference.
    page.evaluate("document.documentElement.lang='en-us'")
    page.wait_for_function("PLM.i18n.locale==='en-us'")
    page.wait_for_function("document.querySelector('.plm-remove-unavailable').title==='Remove from list'")
    page.locator('.plm-remove-unavailable').click()
    page.wait_for_function("!!fixture.selections[fixture.lists[1].Id][40].DeletedAt")
    page.wait_for_function("document.querySelectorAll('.plm-unavailable-card').length===40")
    assert page.locator('#plm-previous').is_disabled()
    assert page.evaluate("!fixture.selections[fixture.lists[2].Id][0].DeletedAt")
    assert page.evaluate('fixture.favorite') is False
    browser.close()
    print('PASS: remove unavailable selection, conflict recovery, last-page clamp, locale and independent lists')
