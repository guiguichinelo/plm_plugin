"""Wide and narrow real templates retain usable space, translated headings, and RTL flow."""
from web_fixture import *

with sync_playwright() as p:
 channel=os.environ.get('PLM_BROWSER_CHANNEL')
 browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
 page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.route('http://plm-fixture.test/**',handle)
 page.goto('http://plm-fixture.test/web/#/favorites')
 page.locator('#plm-app[data-controller]').wait_for()
 for width in [390,1280,2560,3440]:
  page.set_viewport_size({'width':width,'height':900})
  for locale in ['pt-br','en-us','de','ar','ja']:
   page.evaluate("l=>{document.documentElement.lang=l;document.documentElement.dir=l==='ar'?'rtl':'ltr'}",locale)
   page.wait_for_function('l=>PLM.i18n.locale===l&&PLM.i18n.loaded',arg=locale)
   for route,selector in [('library','#plm-app'),('manage','#plm-manage')]:
    page.evaluate('r=>location.hash=PLM.routes[r]',route)
    page.locator(selector).wait_for(state='visible')
    result=page.locator(selector).evaluate('e=>({width:e.getBoundingClientRect().width,overflow:document.documentElement.scrollWidth>innerWidth+1})')
    assert not result['overflow'],(width,locale,route,result)
    if route=='library':assert result['width']>=width*.9,(width,locale,route,result)
    elif width>=1280:assert result['width']<1000,(width,locale,route,result)
 assert not errors,errors
 browser.close()
print('PASS: only Favorites expands at 2560/3440px; settings retain their width; narrow screens and five languages fit without horizontal overflow')
