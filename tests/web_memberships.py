"""Saving to multiple personal lists also likes; clicking a liked heart edits all lists."""
from web_fixture import *

with sync_playwright() as p:
    channel=os.environ.get('PLM_BROWSER_CHANNEL')
    browser=p.chromium.launch(headless=True,**({'channel':channel} if channel else {}))
    page=browser.new_page()
    page.route('http://plm-fixture.test/**',handle)
    page.goto('http://plm-fixture.test/web/#/home')
    page.wait_for_function('window.PLM?.interactionsReady && window.PLM.i18n.loaded')
    page.evaluate("fixture.lists.push({Id:'22222222222222222222222222222222',Name:'Cinema',Revision:0},{Id:'33333333333333333333333333333333',Name:'Family',Revision:0})")
    page.locator('#fixture-native-heart').click()
    picker=page.locator('.plm-picker')
    picker.get_by_role('checkbox',name='Cinema',exact=True).check()
    page.wait_for_function("!!fixture.selections['22222222222222222222222222222222']?.length")
    assert page.evaluate('fixture.favorite') is False
    assert not picker.get_by_role('checkbox',name='Curtidos',exact=True).is_checked()
    picker.get_by_role('checkbox',name='Family',exact=True).check()
    page.wait_for_function("fixture.selections['33333333333333333333333333333333']?.length===1")
    picker.get_by_role('button',name='Fechar',exact=True).click()
    page.locator('#fixture-native-heart').click()
    for label in ['Cinema','Family']:
        assert picker.get_by_role('checkbox',name=label,exact=True).is_checked()
    picker.get_by_role('checkbox',name='Cinema',exact=True).uncheck()
    page.wait_for_function("!!fixture.selections['22222222222222222222222222222222'][0].DeletedAt")
    assert picker.get_by_role('checkbox',name='Family',exact=True).is_checked()
    assert page.evaluate('fixture.favorite') is False
    picker.get_by_role('button',name='Fechar',exact=True).click()
    page.locator('#fixture-native-heart').click()
    assert not picker.get_by_role('checkbox',name='Cinema',exact=True).is_checked()
    assert picker.get_by_role('checkbox',name='Family',exact=True).is_checked()
    assert not picker.get_by_role('checkbox',name='Curtidos',exact=True).is_checked()
    page.wait_for_function("document.querySelector('#fixture-native-heart').classList.contains('plm-saved-heart')")
    assert page.locator('#fixture-native-heart').get_attribute('data-isfavorite')=='false'
    picker.get_by_role('checkbox',name='Family',exact=True).uncheck()
    page.wait_for_function("!document.querySelector('#fixture-native-heart').classList.contains('plm-saved-heart')")
    picker.get_by_role('checkbox',name='Assistir mais tarde',exact=True).check()
    page.wait_for_function("document.querySelector('#fixture-native-heart').classList.contains('plm-saved-heart')")
    assert not picker.get_by_role('checkbox',name='Curtidos',exact=True).is_checked()
    picker.get_by_role('checkbox',name='Curtidos',exact=True).check()
    page.wait_for_function('fixture.favorite')
    picker.get_by_role('checkbox',name='Assistir mais tarde',exact=True).uncheck()
    assert page.evaluate('fixture.favorite') is True
    browser.close()
    print('PASS: native liked heart, two memberships, reopen and independent removal')
