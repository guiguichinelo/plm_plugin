(() => {
 'use strict';if(window.plmNavigationStarted)return;window.plmNavigationStarted=true;
 let disabled=false,pending=false,home={owner:'',dirty:true,enabled:null,data:null,loading:false};
 const style=document.createElement('style');style.textContent='[data-plugin-pages][href*="PLM/web/"]{display:none!important}body.plm-no-playlists a[href*="#/playlists"]{display:none!important}';document.head.append(style);
 const observer=new MutationObserver(schedule);
 function schedule(){if(!pending){pending=true;setTimeout(update,100);}}
 const isHome=()=>/^#\/home(?:\?|$)/.test(location.hash)&&!/[?&]tab=1(?:&|$)/.test(location.hash);
 const legacyFavorites=()=>{const [path,query='']=location.hash.slice(1).split('?');return /^\/favorites(?:\.html)?$/.test(path)||/^\/home(?:\.html)?$/.test(path)&&new URLSearchParams(query).get('tab')==='1';};
 function textLink(original,label,href,attribute){const link=original.cloneNode(true);link.removeAttribute('id');link.setAttribute(attribute,'true');link.href=href;link.classList.remove('lnkDisplayPreferences','lnkHomePreferences');const text=link.querySelector('.sectionName,.listItemBodyText,.MuiListItemText-primary');if(text)text.textContent=label;else{link.replaceChildren();link.append(window.PLM.icon('favorite'),document.createTextNode(' '+label));}const icon=link.querySelector('.material-icons');if(icon){icon.classList.remove('tv','home','settings','video_library');icon.textContent='favorite';}return link;}
 function configureMenus(P){
  document.querySelectorAll('[data-plm-shortcut],[data-plm-drawer]').forEach(e=>e.remove());
  for(const link of document.querySelectorAll('a[href="#/home?tab=1"],a[href="#/favorites"]')){link.href=P.routes.library;link.setAttribute('data-plm-browse','true');}
  const first=document.querySelector('.mainDrawer-scrollContainer a[href="#/home"]');if(first&&!document.querySelector('[data-plm-browse-entry]')){const link=textLink(first,P.t("Favorites"),P.routes.library,'data-plm-browse-entry');link.setAttribute('data-plm-browse','true');first.after(link);}
  const display=document.querySelector('.lnkDisplayPreferences');if(display&&!display.parentElement.querySelector('[data-plm-preferences]'))display.after(textLink(display,P.t("Favorites"),P.routes.manage,'data-plm-preferences'));
  for(const settings of document.querySelectorAll('[role="menu"] a[href="#/mypreferencesmenu"]'))if(!settings.parentElement.querySelector('[data-plm-account-favorites]'))settings.after(textLink(settings,P.t("Favorites"),P.routes.manage,'data-plm-account-favorites'));
 }
 const forwardedHearts=new WeakSet();const listKinds=['Movie','Series','Season','Episode','Video','MusicVideo','Audio','MusicAlbum','AudioBook','Book','Photo','PhotoAlbum'];
 function heartType(h){return h.dataset.itemtype||h.dataset.plmKind||h.closest('[data-type]')?.dataset.type||h.parentElement?.querySelector('button[is="emby-playstatebutton"][data-type]')?.dataset.type;}
 function attachPickers(P){
  for(const heart of document.querySelectorAll('button[is="emby-ratingbutton"][data-id]')){
   if(heart.closest('.plm-ui'))continue;const id=heart.dataset.id,type=heartType(heart);if(id&&!type){P.item(id).then(m=>{if(m&&heart.isConnected){heart.dataset.plmKind=m.Type;schedule();}}).catch(()=>{});continue;}
   if(!id||type&&!['Movie','Series','Season','Episode','Video','MusicVideo','Audio','MusicAlbum','AudioBook','Book','Photo','PhotoAlbum'].includes(type))continue;
   if(heart.nextElementSibling?.dataset.plmFor===id)continue;if(heart.nextElementSibling?.hasAttribute('data-plm-for'))heart.nextElementSibling.remove();
   // Copy appearance, never native lifecycle selectors: Jellyfin calls setItem()
   // on .btnUserRating/.btnPlaystate during an in-place details refresh.
   if(!P.bindClock)continue;const save=P.button('',null,heart.className+' plm-native-save');save.classList.remove('ratingbutton-withrating','btnUserRating','btnPlaystate','plm-saved-heart');save.dataset.plmFor=id;save.append(P.icon('watch_later'));P.bindClock(save,id);heart.after(save);heart.title=P.t("PlmFavoritesandlists");
  }
 }
 function anchor(){const all=[...document.querySelectorAll('.homeSectionsContainer>.verticalSection')].filter(e=>!e.hasAttribute('data-plm-home'));const next=document.querySelector('.homeSectionsContainer a[href*="type=nextup"]')?.closest('.verticalSection');return next||all.find(e=>/continuar assistindo|continue watching/i.test(e.querySelector('h2')?.textContent||''))||all[0];}
 function exclusions(){const ids=new Set();for(const section of document.querySelectorAll('.homeSectionsContainer>.verticalSection'))if(section.querySelector('a[href*="type=nextup"]')||/^(continuar assistindo|continue watching)$/i.test(section.querySelector('h2')?.textContent?.trim()||''))section.querySelectorAll('.card[data-id]').forEach(e=>ids.add(e.dataset.id));return ids;}
 function renderHome(P,row){
  if(!row.isConnected||!home.data||P.owner()!==home.owner||!isHome())return;const excluded=exclusions(),items=home.data.items.filter(i=>!excluded.has(i.Id)).slice(0,24),signature=items.map(i=>i.Id).join('|');if(row.dataset.signature===signature)return;row.dataset.signature=signature;row.replaceChildren();
  const heading=P.el('div','plm-home-heading'),link=P.el('a','',P.t("PlmViewlist"));link.href=P.routes.library+'&list='+encodeURIComponent(home.data.id);heading.append(P.el('h2','',P.t("PlmWatchLater")),link);row.append(heading);
  if(items.length){const strip=P.container('plm-home-strip');for(const item of items)strip.append(P.card(item,{wide:true}));row.append(strip);}
  else{const box=P.el('div','plm-home-empty'),text=P.el('div');text.append(P.el('p','',P.t("PlmNopendingcontentinthissection")),P.el('p','plm-muted',home.data.items.length?P.t("PlmContentinprogressisalreadyinthesectionsabove"):P.t("PlmUsetheclocktoaddcontenttoWatchLater")));box.append(P.icon('watch_later'),text);row.append(box);}
 }
 function ensureHome(P){
  if(!isHome())return;const previous=anchor();if(!previous||home.enabled===false&&!home.dirty)return;
  let row=document.querySelector('[data-plm-home]');if(!row){row=P.el('section','verticalSection plm-ui plm-home padded-left padded-right');row.dataset.plmHome='true';row.setAttribute('aria-label',P.t("PlmWatchLater"));previous.after(row);}if(previous.nextElementSibling!==row)previous.after(row);
  if(home.data&&!home.dirty)renderHome(P,row);if(home.loading||!home.dirty)return;const user=P.owner(),state=home;state.loading=true;state.dirty=false;
  Promise.resolve().then(async()=>{const prefs=await P.call('preferences');const enabled=prefs.homeWatchLater!==false;if(!enabled)return {enabled,data:null};return {enabled,data:await P.watchCandidates()};}).then(result=>{
   if(P.owner()!==user||home!==state)return;state.enabled=result.enabled;state.data=result.data;state.loading=false;if(!result.enabled){document.querySelector('[data-plm-home]')?.remove();return;}const current=document.querySelector('[data-plm-home]');if(current){delete current.dataset.signature;renderHome(P,current);}
  }).catch(()=>{state.loading=false;if(row.isConnected&&!row.childElementCount)row.remove();});
 }
 function update(){
  pending=false;observer.disconnect();const P=window.PLM;
  if(P&&!P.i18n?.loaded){P.i18n?.ready.then(schedule);return;}
  if(P&&P.owner()){
   if(legacyFavorites()){
    // Legacy pages import the host tab module, which captures .skinHeader at load.
    // Wait for the native shell before moving a React Favorites entry to that page.
    if(document.querySelector('.skinHeader'))location.replace(P.routes.library);else schedule();
    observer.observe(document.body,{childList:true,subtree:true});return;
   }
   P.styles();if(home.owner!==P.owner()){document.querySelector('[data-plm-home]')?.remove();home={owner:P.owner(),dirty:true,enabled:null,data:null,loading:false};}
   configureMenus(P);for(const link of document.querySelectorAll('[data-plm-browse],[data-plm-preferences],[data-plm-account-favorites]')){const node=link.querySelector('.sectionName,.listItemBodyText,.MuiListItemText-primary');if(node&&node.textContent!==P.t('Favorites'))node.textContent=P.t('Favorites');}attachPickers(P);for(const c of document.querySelectorAll('.plm-cards'))if(typeof c.resume==='function'&&!c.classList.contains('plm-native-ready'))c.classList.add('plm-native-ready');P.surface?.sync();ensureHome(P);
  }else{document.querySelector('[data-plm-home]')?.remove();P?.clearBackdrop?.();}
  if(disabled){document.body.classList.add('plm-no-playlists');for(const e of document.querySelectorAll('button,[role="menuitem"],a[data-id],a[data-action]'))if([P.t('AddToPlaylist'),P.t('NewPlaylist')].includes(e.textContent.trim())||/^(Adicionar (à|a) playlist|Add to playlist|Criar playlist|Nova playlist)$/i.test(e.textContent.trim())||/addtoplaylist/i.test(e.dataset.id||e.dataset.action||''))e.style.display='none';if(P&&location.hash.startsWith('#/playlists'))location.hash=P.routes.library;}
  observer.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['class','data-id','data-isfavorite','href']});
 }
 document.addEventListener('click',e=>{const P=window.PLM;if(!P)return;const browse=e.target.closest('[data-plm-browse]');if(browse){e.preventDefault();e.stopImmediatePropagation();location.hash=P.routes.library;return;}const manage=e.target.closest('[data-plm-account-favorites],[data-plm-preferences]');if(manage){e.preventDefault();e.stopImmediatePropagation();const popup=manage.closest('[role=presentation]');popup?.querySelector('.MuiBackdrop-root')?.click();location.hash=P.routes.manage;return;}const heart=e.target.closest('button[is="emby-ratingbutton"][data-id]');if(heart&&P.interactionsReady){if(forwardedHearts.has(heart)){forwardedHearts.delete(heart);return;}const type=heartType(heart);if(!type||listKinds.includes(type)){e.preventDefault();e.stopImmediatePropagation();if(type)P.picker({Id:heart.dataset.id});else P.item(heart.dataset.id,true).then(media=>{if(!heart.isConnected)return;if(media&&listKinds.includes(media.Type))P.picker(media);else{forwardedHearts.add(heart);heart.click();}}).catch(()=>{});return;}}if(heart)setTimeout(()=>P.changed(),1000);},true);
 function init(){const script=document.querySelector('script[data-plm-navigation-script]');fetch(new URL('settings',script?.src||document.baseURI)).then(r=>r.json()).then(s=>{disabled=s.playlistsDisabled===true||s.PlaylistsDisabled===true;schedule();}).catch(()=>{});schedule();window.addEventListener('hashchange',()=>{home.dirty=true;schedule();});window.addEventListener('plm:changed',()=>{home.dirty=true;home.enabled=null;schedule();});window.addEventListener('plm:localechange',()=>{document.querySelectorAll('[data-plm-home]').forEach(e=>delete e.dataset.signature);home.dirty=true;schedule();});setInterval(()=>{if(isHome()&&!document.hidden){home.dirty=true;schedule();}},60000);}
 if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);else init();
})();
