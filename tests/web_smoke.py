"""Synthetic browser workflow for private lists and native favorites."""
from web_fixture import *

with sync_playwright() as p:
    channel=os.environ.get('PLM_BROWSER_CHANNEL')
    browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
    page=browser.new_page(viewport={'width':1200,'height':900})
    errors=[]
    page.on('pageerror',lambda error:errors.append(str(error)))
    page.route('http://plm-fixture.test/**',handle)
    page.goto('http://plm-fixture.test/web/#/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fcollections.html')
    page.get_by_text('Esta lista está vazia',exact=True).wait_for()
    assert page.get_by_text('SEU ESPAÇO',exact=True).count()==0
    page.locator('#fixture-native-heart').click()
    picker=page.locator('.plm-picker');picker.get_by_role('checkbox',name='Curtidos',exact=True).check()
    page.wait_for_function('fixture.favorite');picker.get_by_role('button',name='Fechar',exact=True).click()
    page.locator('#plm-media .plm-card').wait_for()
    page.locator('#plm-media .plm-watch-button').click()
    page.wait_for_function("fixture.selections[fixture.lists[0].Id]?.length===1")
    assert page.evaluate('fixture.favorite') is True
    page.locator('#plm-media .plm-heart').click()
    picker=page.locator('.plm-picker');assert picker.get_by_role('checkbox',name='Curtidos',exact=True).is_checked()
    assert picker.get_by_role('checkbox',name='Assistir mais tarde',exact=True).is_checked()
    picker.get_by_role('button',name='Fechar',exact=True).click()
    page.get_by_role('button',name='Criar nova lista',exact=True).click()
    name='<img src=x onerror="window.pwned=1">'
    page.get_by_role('textbox',name='Nome da lista',exact=True).fill(name)
    page.get_by_role('button',name='Salvar',exact=True).click()
    row=page.locator('.plm-list-row').filter(has_text=name);row.wait_for()
    assert page.evaluate('window.pwned') is None
    row.click(button='right');page.get_by_role('menuitem',name='Renomear',exact=True).click()
    page.get_by_role('textbox',name='Nome da lista',exact=True).fill('Cinema')
    page.get_by_role('button',name='Salvar',exact=True).click()
    row=page.locator('.plm-list-row').filter(has_text='Cinema');row.wait_for()
    row.click(button='right')
    with page.expect_file_chooser() as chooser:page.get_by_role('menuitem',name='Definir thumb',exact=True).click()
    import base64
    png=page.evaluate("()=>{const c=document.createElement('canvas');c.width=32;c.height=32;const x=c.getContext('2d');x.fillStyle='#6279cd';x.fillRect(0,0,32,32);return c.toDataURL('image/png').split(',')[1];}")
    chooser.value.set_files({'name':'thumb.png','mimeType':'image/png','buffer':base64.b64decode(png)})
    page.wait_for_function('Object.keys(fixture.art||{}).length===1')
    page.locator('#plm-list-cover img').wait_for()
    row.click(button='right');page.once('dialog',lambda dialog:dialog.accept());page.get_by_role('menuitem',name='Excluir lista',exact=True).click()
    page.locator('#plm-list-title').get_by_text('Curtidos',exact=True).wait_for()
    page.get_by_role('button',name='Gêneros',exact=True).click()
    search=page.get_by_role('searchbox',name='Pesquisar gêneros');search.fill('Dra')
    page.get_by_role('checkbox',name='Drama',exact=True).check();search.fill('Com')
    page.get_by_role('checkbox',name='Comedy',exact=True).check()
    page.get_by_role('button',name='Aplicar',exact=True).click();assert page.locator('.plm-filter-chip').count()==2
    page.get_by_role('button',name='Redefinir filtros',exact=True).click()
    page.get_by_role('button',name='Filtros',exact=True).click();page.get_by_role('button',name='Aplicar',exact=True).click()
    page.get_by_role('button',name='Ordenar',exact=True).click();page.get_by_role('button',name='Aplicar',exact=True).click()
    page.get_by_role('link',name='Lixeira',exact=True).click();page.locator('#plm-manage').wait_for()
    page.get_by_role('button',name='Restaurar',exact=True).click();page.locator('#plm-managed-lists').get_by_text('Cinema',exact=True).wait_for()
    page.evaluate("location.hash='#/home'");page.locator('[data-plm-home] .plm-card').wait_for()
    assert page.locator('[data-plm-home]').evaluate("e=>e.previousElementSibling.querySelector('h2').textContent")=='A seguir'
    page.evaluate('fixture.played=true;PLM.changed()');page.locator('[data-plm-home] .plm-card').wait_for(state='detached')
    assert page.evaluate("fixture.selections[fixture.lists[0].Id].length")==1
    page.evaluate("location.hash='#/home?tab=1'");page.wait_for_function('location.hash===PLM.routes.library');page.locator('#plm-media .plm-card').wait_for()
    page.wait_for_function("document.querySelector('#plm-page-backdrop')?.style.backgroundImage.includes('image.svg')")
    page.evaluate('window.retainedRoot=document.querySelector("#plm-app")')
    for _ in range(3):
        page.evaluate("location.hash='#/home'");page.wait_for_function("!document.querySelector('#plm-page-backdrop')")
        page.evaluate("location.hash=PLM.routes.library")
        page.wait_for_function("document.querySelector('#plm-page-backdrop')?.style.backgroundImage.includes('image.svg')")
        assert page.evaluate('document.querySelector("#plm-app")===retainedRoot')
        page.evaluate("document.querySelectorAll('.backdropImage').forEach(e=>e.style.backgroundImage='none')")
        assert page.locator('#plm-page-backdrop').evaluate("e=>e.style.backgroundImage!=='none'")
    page.evaluate("()=>{window.originalItems=ApiClient.getItems;ApiClient.getItems=async(...args)=>{if(args[1]?.IsFavorite){await new Promise(resolve=>window.finishDelayedFavorites=resolve);}return originalItems(...args);};PLM.changed();}")
    page.wait_for_function('typeof window.finishDelayedFavorites===\"function\"')
    page.evaluate("location.hash='#/home'");page.wait_for_function("!document.querySelector('#plm-page-backdrop')")
    page.evaluate('()=>{ApiClient.getItems=originalItems;finishDelayedFavorites();}')
    page.wait_for_timeout(100)
    assert page.locator('#plm-page-backdrop').count()==0
    page.evaluate('location.hash=PLM.routes.library')
    page.wait_for_function("document.querySelector('#plm-page-backdrop')?.style.backgroundImage.includes('image.svg')")
    page.evaluate('fixture.bulk=123;PLM.changed()')
    page.wait_for_function("document.querySelector('#plm-count').textContent==='123 itens'&&!document.querySelector('#plm-media').hasAttribute('aria-busy')")
    assert page.locator('#plm-media .plm-card').count()==40
    page.locator('#plm-next').click();page.wait_for_function("document.querySelector('#plm-page-number').textContent==='2 de 4'")
    page.locator('#plm-page-size').select_option('80');page.wait_for_function("document.querySelector('#plm-page-number').textContent==='1 de 2'")
    assert page.locator('#plm-media .plm-card').count()==80
    page.locator('#plm-search').fill('film 122');page.wait_for_function("document.querySelector('#plm-count').textContent==='1 conteúdo'")
    assert page.locator('#plm-media .plm-card').count()==1
    page.set_viewport_size({'width':390,'height':844})
    assert page.evaluate('document.documentElement.scrollWidth<=window.innerWidth+1')
    assert not errors,errors
    Path('artifacts').mkdir(exist_ok=True);page.screenshot(path='artifacts/plm-web-mobile-smoke.png',full_page=True)
    browser.close();print('PASS: heart picker, quick clock, sidebar create/rename/thumbnail/delete, searchable multiple genres, filters, trash restore, home placement, retention and mobile layout.')
