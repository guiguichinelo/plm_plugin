"""Regression: Watch Later must not enter Jellyfin's native setItem lifecycle."""
from web_fixture import *

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, **({'channel': os.environ['PLM_BROWSER_CHANNEL']} if os.environ.get('PLM_BROWSER_CHANNEL') else {}))
    page = browser.new_page()
    page.route('http://plm-fixture.test/**', handle)
    page.goto('http://plm-fixture.test/web/#/home')
    page.wait_for_function('window.PLM?.interactionsReady && window.PLM.i18n.loaded')
    page.evaluate('''() => {
        document.querySelectorAll('.plm-native-save').forEach(e => e.remove());
        const heart = document.querySelector('#fixture-native-heart');
        heart.className = 'button-flat btnUserRating detailButton emby-button ratingbutton-withrating plm-saved-heart';
        heart.innerHTML='<span class="material-icons" style="font-size:32px">favorite</span>';
        heart.style.cssText='width:52px;height:52px;padding:5px;box-sizing:border-box';
        heart.setItem = item => { window.nativeRefreshes = (window.nativeRefreshes || 0) + 1; heart.dataset.id = item.Id; };
        window.refreshNativeDetails = () => {
            for (const button of document.querySelectorAll('.btnUserRating,.btnPlaystate')) button.setItem({Id:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'});
        };
    }''')
    page.wait_for_selector('.plm-native-save')
    for _ in range(3):
        page.evaluate('refreshNativeDetails()')
        page.wait_for_timeout(120)
    assert page.evaluate('nativeRefreshes') == 3
    clock = page.locator('.plm-native-save')
    assert 'detailButton' in clock.get_attribute('class')
    assert clock.evaluate("e => !e.matches('.btnUserRating,.btnPlaystate,.ratingbutton-withrating,.plm-saved-heart')")
    assert clock.evaluate('b=>Math.abs(b.getBoundingClientRect().width-document.querySelector("#fixture-native-heart").getBoundingClientRect().width)<1')
    assert clock.locator('.material-icons').evaluate('e=>getComputedStyle(e).fontSize')=='32px'
    page.evaluate('()=>{document.querySelector("#fixture-native-heart").style.width="60px";document.querySelector("#fixture-native-heart span").style.fontSize="36px";dispatchEvent(new Event("resize"));}')
    page.wait_for_function('getComputedStyle(document.querySelector(".plm-native-save span")).fontSize==="36px"')
    clock.click()
    page.wait_for_function("fixture.selections[fixture.lists[0].Id]?.length === 1")
    assert page.evaluate('fixture.favorite') is False
    page.evaluate('refreshNativeDetails()')
    assert page.evaluate('nativeRefreshes') == 4
    browser.close()
    print('PASS: repeated native details refresh and independent Watch Later keep working')
