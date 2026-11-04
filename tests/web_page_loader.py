"""Native-animation race: an incoming hidden template owns its delayed response."""
import os,re
from pathlib import Path
from playwright.sync_api import sync_playwright

root=Path(__file__).resolve().parents[1]
source=(root/'src/Jellyfin.Plugin.Plm/PluginPageTemplate.cs').read_text(encoding='utf-8')
loader=source.split('const string loader = """')[1].split('""";')[0].strip()
with sync_playwright() as p:
    channel=os.environ.get('PLM_BROWSER_CHANNEL')
    browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
    page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.route('http://loader-fixture.test/**',lambda route:route.fulfill(body='<html></html>',content_type='text/html'))
    page.goto('http://loader-fixture.test/')
    page.set_content('''<div class="userPreferencesPage"><div id="outgoing" class="userPluginSettingsContainer" data-plugin-pages-owned="old">Previous page</div></div>
        <div class="userPreferencesPage hide"><div id="incoming" class="userPluginSettingsContainer"></div><script type="text/plain">pluginPagesOwnedLoader</script></div>''')
    page.evaluate("""()=>{location.hash='/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fcollections.html';window.fixtureOwner='one';window.ApiClient={getCurrentUserId:()=>fixtureOwner,getUrl:p=>p,ajax:()=>new Promise(resolve=>window.finishLoad=resolve)};}""")
    # Evaluation without currentScript reproduces the host evaluator.
    page.evaluate(loader)
    page.evaluate("finishLoad('<section id=loaded>New page</section>')")
    page.locator('#incoming #loaded').wait_for()
    assert page.locator('#outgoing').inner_text()=='Previous page'
    page.evaluate("()=>{document.querySelector('#incoming').removeAttribute('data-plugin-pages-owned');document.querySelector('#incoming').replaceChildren();}")
    page.evaluate(loader)
    page.evaluate("fixtureOwner='two';finishLoad('<section id=leak>Private</section>')")
    assert page.locator('#leak').count()==0
    # Syntax survives the exact single-quoted webpack-template representation.
    encoded=loader.replace('\\','\\\\').replace('\r','\\r').replace('\n','\\n').replace("'","\\'")
    decoded=page.evaluate("()=> '"+encoded+"'")
    assert decoded==loader
    page.evaluate('source=>new Function(source)',decoded)
    assert not errors,errors
    browser.close()
print('PASS: outgoing animation, legacy evaluator, owner change, webpack escaping')
