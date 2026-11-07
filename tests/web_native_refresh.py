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
    clock.click()
    page.wait_for_function("fixture.selections[fixture.lists[0].Id]?.length === 1")
    assert page.evaluate('fixture.favorite') is False
    page.evaluate('refreshNativeDetails()')
    assert page.evaluate('nativeRefreshes') == 4
    browser.close()
    print('PASS: repeated native details refresh and independent Watch Later keep working')
