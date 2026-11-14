"""Native view navigation and PLM library scope with per-user view memory."""
from web_fixture import *

library='cccccccccccccccccccccccccccccccc'
other='dddddddddddddddddddddddddddddddd'
with sync_playwright()as p:
 browser=p.chromium.launch(headless=True,**({'channel':os.environ['PLM_BROWSER_CHANNEL']}if os.environ.get('PLM_BROWSER_CHANNEL')else{}))
 page=browser.new_page(viewport={'width':1200,'height':900});errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.route('http://plm-fixture.test/**',handle)
 page.goto('http://plm-fixture.test/web/#/home');page.wait_for_function('window.PLM?.interactionsReady&&PLM.i18n.loaded')
 page.evaluate('''()=>{
  const toolbar=document.createElement('div');toolbar.className='MuiToolbar-root';toolbar.innerHTML='<button aria-controls="library-view-menu">Movies</button><button id="native-sort">Sort</button>';document.body.prepend(toolbar);
  window.requests=[];window.nativeQueries=[];window.actor='fixture-user';ApiClient.getCurrentUserId=()=>actor;
  fixture.backdrop=true;
  const ajax=ApiClient.ajax;ApiClient.ajax=r=>{requests.push(r.url);return ajax(r);};const get=ApiClient.getItems;ApiClient.getItems=(u,o)=>{nativeQueries.push(o);return get(u,o);};
  const item=ApiClient.getItem;ApiClient.getItem=(u,id)=>id==='cccccccccccccccccccccccccccccccc'||id==='dddddddddddddddddddddddddddddddd'?Promise.resolve({Id:id,Name:id[0]==='c'?'Movies':'Anime movies',Type:'CollectionFolder',CollectionType:'movies'}):item(u,id);
  fixture.lists.push({Id:'22222222222222222222222222222222',Name:'Mixed list',WatchLater:false,Revision:0});
  fixture.selections['22222222222222222222222222222222']=[{ItemId:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',Position:0,Media:{Status:'catalogued',Name:'Synthetic film',Kind:'Movie',Genres:['Drama']}}];
 }''')
 page.add_script_tag(path=str(web/'library-navigation.js'));page.add_script_tag(path=str(web/'navigation-state.js'))
 page.evaluate('id=>location.hash="/movies?"+new URLSearchParams({topParentId:id,collectionType:"movies",tab:"0"})',library)
 nav=page.locator('.MuiToolbar-root [data-plm-library-nav]');nav.wait_for()
 assert nav.locator('[data-plm-library-view]').count()==4
 assert page.locator('#native-sort').is_visible()
 assert not page.locator('button[aria-controls=library-view-menu]').is_visible()
 nav.locator('[data-plm-library-view=suggestions]').click();page.wait_for_function('location.hash.includes("tab=1")')
 page.wait_for_function('document.querySelector(".MuiToolbar-root [data-plm-library-view=suggestions]")?.getAttribute("aria-current")==="page"')
 nav.locator('[data-plm-library-view=genres]').click();page.wait_for_function('location.hash.includes("tab=4")')
 page.wait_for_function('document.querySelector(".MuiToolbar-root [data-plm-library-view=genres]")?.getAttribute("aria-current")==="page"')
 assert nav.locator('.plm-library-heading').inner_text()=='Movies'
 page.go_back();page.locator('.MuiToolbar-root [data-plm-library-view=suggestions][aria-current=page]').wait_for()
 page.go_forward();page.locator('.MuiToolbar-root [data-plm-library-view=genres][aria-current=page]').wait_for()
 nav.locator('.plm-library-more summary').click();nav.locator('.plm-library-more-menu a[href*="tab=5"]').click()
 page.wait_for_function('document.querySelector(".MuiToolbar-root .plm-library-more-menu a[aria-current=page]")?.getAttribute("href").includes("tab=5")')
 assert nav.locator('.plm-library-extra-active').count()==1
 nav.locator('[data-plm-library-view=favorites]').click()
 page.locator('#plm-app[data-controller] [data-plm-library-nav]').wait_for()
 page.locator('.plm-library-row').wait_for()
 assert not page.locator('.plm-sidebar').is_visible()
 assert not page.locator('#plm-search').is_visible()
 assert page.locator('.plm-row-explore').count()==0
 page.locator('#plm-page-backdrop').wait_for(state='attached')
 assert page.locator('.plm-library-row .sectionTitle').inner_text()=='Mixed list'
 assert not page.evaluate('fixture.favorite')
 page.evaluate('fixture.favorite=true;PLM.changed()')
 page.wait_for_function('document.querySelector(".plm-library-row")?.dataset.plmRowList==="liked"')
 page.locator('.plm-library-row[data-plm-row-list=liked] .plm-row-title').click()
 page.wait_for_function('nativeQueries.some(q=>q.IsFavorite&&q.ParentId==="cccccccccccccccccccccccccccccccc")')
 page.locator('#plm-scope-back').click()
 page.locator('.plm-library-row[data-plm-row-list="22222222222222222222222222222222"] .plm-row-title').click()
 page.wait_for_function('requests.some(r=>r.includes("/collections/22222222222222222222222222222222?")&&r.includes("libraryId=cccccccccccccccccccccccccccccccc"))')
 assert page.locator('#plm-list-title').inner_text()=='Mixed list'
 assert page.locator('#plm-search').is_visible()
 page.locator('#plm-scope-back').click()
 page.evaluate('fixture.favorite=false;fixture.selections={};PLM.changed()')
 page.locator('.plm-library-empty').wait_for()
 assert page.locator('.plm-library-row').count()==0
 assert not page.locator('#plm-pager').is_visible()
 assert not page.locator('#plm-search').is_visible()
 assert page.locator('.plm-row-explore').count()==0
 # A library's default entry remembers Favorites for this user only.
 page.evaluate('id=>location.hash="/movies?"+new URLSearchParams({topParentId:id,collectionType:"movies"})',library)
 page.wait_for_function('location.hash.includes("libraryId=cccccccccccccccccccccccccccccccc")')
 page.evaluate('id=>location.hash="/movies?"+new URLSearchParams({topParentId:id,collectionType:"movies"})',other)
 page.wait_for_function('location.hash.startsWith("#/movies")');page.wait_for_timeout(150)
 assert 'libraryId='not in page.evaluate('location.hash')
 # Changing users does not reuse the previous user's saved view.
 page.evaluate('id=>{actor="second-user";location.hash="/movies?"+new URLSearchParams({topParentId:id,collectionType:"movies"});PLM.libraryNavigation.sync();}',library)
 page.wait_for_timeout(150);assert 'libraryId='not in page.evaluate('location.hash')
 for width in [390,320]:
  page.set_viewport_size({'width':width,'height':900});page.locator('.plm-library-more summary').click();page.wait_for_timeout(50)
  assert page.locator('.plm-library-more-menu').evaluate('(e)=>{const b=e.getBoundingClientRect();return b.left>=0&&b.right<=innerWidth;}')
  page.keyboard.press('Escape');assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
 page.evaluate('location.hash="/home"');page.wait_for_selector('[data-plm-library-nav]',state='detached')
 assert page.locator('button[aria-controls=library-view-menu]').is_visible()
 assert not errors,errors
 browser.close()
print('PASS: native movie views, scoped saved/liked/lists, no implicit like, per-library/user memory and toolbar cleanup')
