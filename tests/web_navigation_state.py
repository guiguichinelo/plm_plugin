"""Favorites active state survives redirects, list links, host rerenders and theme changes."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

source=(Path(__file__).resolve().parents[1]/'src/Jellyfin.Plugin.Plm/Web/navigation-state.js').read_text(encoding='utf8')
with sync_playwright() as p:
 channel=os.environ.get('PLM_BROWSER_CHANNEL')
 browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
 page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.route('http://plm-navigation.test/**',lambda r:r.fulfill(body='<html></html>',content_type='text/html'))
 page.goto('http://plm-navigation.test/')
 page.set_content('''<style>:root{--jf-palette-primary-main:#00a4dc}.navMenuOption-selected{background:var(--jf-palette-primary-main)}</style>
 <a id="favorites" class="MuiButton-root" data-plm-browse href="#/favorites">Favorites</a>
 <a id="drawer" class="navMenuOption" data-plm-browse href="#/favorites">Favorites</a>
 <a id="other" class="Mui-selected" aria-current="page" href="#/tv">Shows</a>''')
 page.evaluate("()=>{document.title='Home';window.fixtureLocale='en';window.PLM={t:key=>key==='PlmFavoritesSettings'?(fixtureLocale==='pt'?'Configurações dos Favoritos':'Favorites settings'):(fixtureLocale==='pt'?'Favoritos':'Favorites')};}")
 page.evaluate(source)
 for route in ['/favorites','/home?tab=1','/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fcollections.html','/userpluginsettings?pageUrl=%2FPLM%2Fweb%2Fcollections.html&list=watch-later','/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fmanage.html']:
  page.evaluate('r=>location.hash=r',route)
  page.wait_for_function("document.querySelector('#favorites').getAttribute('aria-current')==='page'")
  page.wait_for_function("value=>document.title===value",arg='Favorites settings' if 'manage.html' in route else 'Favorites')
  assert page.locator('#drawer').evaluate("e=>e.classList.contains('navMenuOption-selected')")
 for color,expected in [('#00a4dc','rgb(0, 164, 220)'),('#6279cd','rgb(98, 121, 205)'),('#006c90','rgb(0, 108, 144)')]:
  page.evaluate("c=>document.documentElement.style.setProperty('--jf-palette-primary-main',c)",color)
  assert page.locator('#favorites').evaluate("e=>getComputedStyle(e).color")==expected
 page.evaluate("()=>{document.querySelector('#favorites').outerHTML='<a id=favorites class=MuiButton-root data-plm-browse href=\"#/favorites\">Favorites</a>'}")
 page.wait_for_function("document.querySelector('#favorites').getAttribute('aria-current')==='page'")
 page.evaluate("document.querySelector('title').firstChild.data='Home'")
 page.wait_for_function("document.title==='Favorites settings'")
 page.evaluate("()=>{fixtureLocale='pt';window.dispatchEvent(new Event('plm:localechange'));}")
 page.wait_for_function("document.title==='Configurações dos Favoritos'")
 page.evaluate("()=>{document.title='Shows';location.hash='/tv';}")
 page.wait_for_function("!document.querySelector('#favorites').hasAttribute('aria-current')")
 assert page.title()=='Shows','Leaving PLM must preserve the destination title'
 for route in ['/home','/tv?topParentId=fixture','/userpluginsettings.html?pageUrl=%2FWUN%2Fweb%2Frequests.html']:
  page.evaluate('r=>location.hash=r',route)
  page.wait_for_function("!document.querySelector('#favorites').hasAttribute('aria-current')")
  assert not page.locator('#drawer').evaluate("e=>e.classList.contains('navMenuOption-selected')")
  assert page.locator('#other').get_attribute('aria-current')=='page'
 page.evaluate(source)
 assert page.locator('style[data-plm-navigation-state]').count()==1
 assert not errors,errors
 browser.close()
print('Favorites navigation: routes, rerenders, themes, cleanup and singleton passed')
