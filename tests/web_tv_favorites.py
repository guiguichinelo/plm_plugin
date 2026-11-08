from pathlib import Path
from playwright.sync_api import sync_playwright
source=(Path(__file__).resolve().parents[1]/'src/Jellyfin.Plugin.Plm/Web/navigation.js').read_text()
with sync_playwright()as p:
 b=p.chromium.launch(channel='msedge',headless=True);page=b.new_page()
 page.set_content('<base href="http://fixture/"><header class="skinHeader"><div class="headerTabs"><button data-index="0" class="emby-tab-button">Home</button><button data-index="1" class="emby-tab-button"><span>Favorites</span></button></div></header>')
 page.evaluate("()=>{location.hash='/home';window.PLM={owner:()=>'',routes:{library:'#/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fcollections.html'}};window.fetch=async()=>({json:async()=>({})});window.nativeClicks=0;document.querySelector('[data-index=\"1\"]').addEventListener('click',()=>nativeClicks++);}")
 page.evaluate(source)
 for keyboard in [False,True]:
  page.evaluate("location.hash='/home'")
  button=page.get_by_role('button',name='Favorites')
  if keyboard:button.focus();button.press('Enter')
  else:button.locator('span').click()
  assert 'PLM%2Fweb%2Fcollections' in page.evaluate('location.hash')
  assert page.evaluate('nativeClicks')==0
 page.evaluate("location.hash='/tv?topParentId=fixture'");page.get_by_role('button',name='Favorites').click();assert page.evaluate('nativeClicks')==1
 assert page.evaluate('location.hash').startswith('#/tv')
 b.close()
print('PASS TV Favorites: pointer, Enter/OK activation and unrelated library tab preserved')
