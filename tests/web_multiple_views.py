"""Jellyfin caches multiple plugin settings pages, including duplicate template IDs."""
from web_fixture import *
with sync_playwright()as p:
 b=p.chromium.launch(headless=True);page=b.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.route('http://plm-fixture.test/**',handle)
 page.goto('http://plm-fixture.test/web/#/home');page.wait_for_function('PLM.i18n.loaded')
 page.evaluate('''()=>{
 window.removeEventListener('hashchange',renderFixture);
 window.renderFixture=async()=>{const host=document.querySelector('#fixture-host');for(const v of host.children)v.classList.add('hide');let view=[...host.children].find(v=>v.dataset.route===location.hash);if(!view){view=document.createElement('div');view.className='page';view.dataset.route=location.hash;host.append(view);view.append(document.createRange().createContextualFragment(await(await fetch('/PLM/web/collections.html')).text()));}view.classList.remove('hide');document.dispatchEvent(new Event('pageshow'));};
 window.addEventListener('hashchange',renderFixture);
 }''')
 def route(scope):page.evaluate('scope=>location.hash=PLM.routes.library+"&libraryId="+scope+"&list=saved"',scope)
 route('cccccccccccccccccccccccccccccccc');page.locator('.plm-library-empty:visible').wait_for()
 page.evaluate("fixture.favorite=true;PLM.changed()")
 page.locator('.plm-library-row:visible').wait_for()
 route('dddddddddddddddddddddddddddddddd');page.locator('.page:not(.hide) .plm-library-row').wait_for()
 assert page.locator('[id=plm-app]').count()==2
 page.locator('.page:not(.hide) .plm-row-title').click();page.locator('.page:not(.hide) #plm-media .plm-card').wait_for()
 page.locator('.page:not(.hide) #plm-scope-back').click();page.locator('.page:not(.hide) .plm-library-row').wait_for()
 route('cccccccccccccccccccccccccccccccc');page.locator('.page:not(.hide) .plm-library-row').wait_for()
 page.evaluate('fixture.favorite=false;PLM.changed()');page.locator('.page:not(.hide) .plm-library-empty').wait_for()
 assert not errors,errors
 b.close()
print('PASS: multiple cached plugin pages initialize independently and resume safely')
