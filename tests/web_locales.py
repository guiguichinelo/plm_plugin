"""All target-release interface locales, preference changes, fallback and RTL layout."""
from web_fixture import *
import re

manifest=json.loads((web/'locales/index.json').read_text(encoding='utf-8'))
english=json.loads((web/'locales/en-us.json').read_text(encoding='utf-8'))
definitions=json.loads((web.parents[2]/'translations/messages.json').read_text(encoding='utf-8'))
assert set(english)==set(definitions)
# All declared string keys must exist, including templates and admin controls.
for source in [*web.glob('*.js'),*web.glob('*.html')]:
    content=source.read_text(encoding='utf-8')
    keys=set(re.findall(r'(?:P\.t|\bt)\([\"\x27]([A-Za-z0-9]+)[\"\x27]',content))|set(re.findall(r'data-plm-(?:text|aria-label|title|placeholder)="([A-Za-z0-9]+)"',content))
    assert keys<=set(english),(source.name,keys-set(english))

with sync_playwright() as p:
    channel=os.environ.get('PLM_BROWSER_CHANNEL')
    browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
    page=browser.new_page(viewport={'width':390,'height':844},locale='pt-BR');errors=[]
    page.add_init_script("document.addEventListener('DOMContentLoaded',()=>{const shell=document.querySelector('.skinHeader');shell?.classList.remove('skinHeader');setTimeout(()=>{window.routeBeforeShell=location.hash;},200);setTimeout(()=>shell?.classList.add('skinHeader'),450);},{once:true});")
    page.on('pageerror',lambda error:errors.append(str(error)))
    page.route('http://plm-fixture.test/**',handle)
    page.goto('http://plm-fixture.test/web/#/favorites')
    page.locator('#plm-app[data-controller]').wait_for()
    page.wait_for_function("location.hash===PLM.routes.library")
    assert page.evaluate('window.routeBeforeShell')=='#/favorites'
    page.evaluate('()=>location.hash=PLM.routes.manage')
    page.locator('#plm-manage[data-controller]').wait_for()
    assert page.locator('#plm-manage .plm-eyebrow').count()==0
    page.evaluate('()=>location.hash=PLM.routes.library')
    page.evaluate("fixture.lists.push({Id:'22222222222222222222222222222222',Name:'Lista do usuário',WatchLater:false,Revision:0});PLM.changed()")
    for locale in [*manifest['locales'],'fr-BE','zz-ZZ','pt_BR']:
        normalized=locale.replace('_','-').lower();resolved=normalized if normalized in manifest['locales'] else normalized.split('-')[0] if normalized.split('-')[0] in manifest['locales'] else 'en-us'
        values=json.loads((web/'locales'/f'{resolved}.json').read_text(encoding='utf-8'))
        page.evaluate("locale=>{document.documentElement.dir=/^(ar|fa|he|ur)(-|$)/.test(locale)?'rtl':'ltr';document.documentElement.lang=locale;}",locale)
        page.wait_for_function('locale=>PLM.i18n.locale===locale&&PLM.i18n.loaded',arg=normalized)
        page.wait_for_function('value=>document.querySelector("#plm-search").placeholder===value',arg=values.get('PlmSearch',english['PlmSearch']))
        page.wait_for_function('value=>document.querySelector("#plm-app h1").textContent===value',arg=values.get('Favorites',english['Favorites']))
        page.wait_for_function('value=>document.querySelector("#plm-manage h1").textContent===value',arg=values.get('PlmFavoritesSettings',english['PlmFavoritesSettings']))
        page.wait_for_function("document.querySelector('#plm-lists').textContent.includes('Lista do usuário')")
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),locale
        assert page.locator('#plm-search').get_attribute('class')=='emby-input searchfields-txtSearch'
        assert page.locator('#plm-page-size option').all_text_contents()==['20','40','80']
        assert page.evaluate("()=>Object.keys(PLM.i18n.coverage.locales).length")==64
    page.evaluate("document.documentElement.lang='en-us'");page.wait_for_function("PLM.i18n.locale==='en-us'")
    page.get_by_role('button',name='Filters',exact=True).click();page.locator('dialog').wait_for()
    page.evaluate("document.documentElement.lang='pt-br'");page.wait_for_function("PLM.i18n.locale==='pt-br'")
    assert page.locator('dialog[open]').count()==0
    assert not errors,errors
    Path('artifacts').mkdir(exist_ok=True)
    Path('artifacts/localization-coverage.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    browser.close();print('PASS: all 64 native interface locales, regional/base/English fallback, user content preserved, live language changes, native controls and RTL/mobile overflow.')
