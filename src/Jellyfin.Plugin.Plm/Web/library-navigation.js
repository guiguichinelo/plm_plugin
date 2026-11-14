/* Jellyfin 12.4 movie/series views, plus the same PLM lists scoped to a library. */
(()=>{
 'use strict';const P=window.PLM;if(!P||P.libraryNavigation)return;
 const names=new Map();let owner='',loading=new Set();
 const valid=id=>/^(?:[a-f0-9]{32}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})$/i.test(id||'');
 function context(){
  const url=new URL(location.hash.slice(1)||'/',location.origin),native=/^\/(movies|tv)(?:\.html)?$/.exec(url.pathname);
  const plm=/^\/userpluginsettings(?:\.html)?$/.test(url.pathname)&&url.searchParams.get('pageUrl')==='/PLM/web/collections.html';
  const id=url.searchParams.get(native?'topParentId':'libraryId');if(!valid(id)||!native&&!plm)return null;
  const type=native?.[1]||(url.searchParams.get('libraryType')==='tvshows'?'tv':'movies');
  return {url,id,type,native:!!native,tab:url.searchParams.get('tab')};
 }
 function key(c){return 'plm-library-view:'+P.api().serverId()+':'+P.owner()+':'+c.id.replaceAll('-','').toLowerCase();}
 function remember(c,view){try{localStorage.setItem(key(c),view);}catch{/* Private browsing may deny storage. */}}
 function recalled(c){try{return localStorage.getItem(key(c));}catch{return null;}}
 function route(c,view){
  if(view==='favorites')return P.routes.library+'&'+new URLSearchParams({libraryId:c.id,libraryType:c.type==='tv'?'tvshows':'movies',list:'saved'});
  const tab=view==='all'?0:view==='suggestions'?1:view==='genres'?(c.type==='tv'?3:4):Number(view);
  const url=c.native?new URL(c.url):new URL('/'+c.type,location.origin);url.searchParams.set('topParentId',c.id);url.searchParams.set('collectionType',c.type==='tv'?'tvshows':'movies');url.searchParams.set('tab',String(tab));return '#'+url.pathname+'?'+url.searchParams;
 }
 function nativeHost(){return [...document.querySelectorAll('button[aria-controls="library-view-menu"]')].map(b=>b.closest('.MuiToolbar-root')).reverse().find(h=>h&&h.getClientRects().length&&!h.closest('.hide,[hidden]'));}
 function current(c){
  if(!c.native)return 'favorites';
  if(c.tab===null){const text=nativeHost()?.querySelector('button[aria-controls="library-view-menu"]')?.textContent?.trim();for(const [view,label]of [['suggestions','Suggestions'],['genres','Genres'],...(c.type==='movies'?[['favorites','Favorites']]:[])])if(text===P.t(label))return view;return 'all';}
  const tab=Number(c.tab);return tab===0?'all':tab===1?'suggestions':tab===(c.type==='tv'?3:4)?'genres':String(tab);
 }
 function reset(){document.querySelectorAll('[data-plm-library-nav]').forEach(n=>n.remove());document.querySelectorAll('.plm-library-toolbar').forEach(n=>n.classList.remove('plm-library-toolbar'));}
 function mark(c){
  for(const link of document.querySelectorAll('a[href]')){
   let matching=false;try{const url=new URL(link.hash.slice(1)||'/',location.origin);matching=!!c&&!c.native&&!link.closest('[data-plm-library-nav]')&&url.searchParams.get('topParentId')===c.id;}catch{}
   if(matching){link.dataset.plmLibraryActive='true';link.setAttribute('aria-current','page');}
   else if(link.dataset.plmLibraryActive==='true'){delete link.dataset.plmLibraryActive;link.removeAttribute('aria-current');}
  }
 }
 function sync(){
  if(!P.i18n?.loaded)return;
  const user=P.owner();if(owner!==user){owner=user;names.clear();loading=new Set();reset();}
  const c=user?context():null;if(!c){reset();mark(null);return;}
  if(c.native&&c.tab===null){const saved=recalled(c);if(['all','suggestions','genres','favorites'].includes(saved)){location.replace(route(c,saved));return;}}
  const view=current(c);
  if(c.native&&c.type==='movies'&&(c.tab==='2'||view==='favorites')){remember(c,'favorites');location.replace(route(c,'favorites'));return;}
  if(!c.native||c.tab!==null)remember(c,view);mark(c);
  let host=c.native?nativeHost():[...document.querySelectorAll('[id="plm-app"]')].reverse().find(r=>r.getClientRects().length&&!r.closest('.hide,[hidden]'));
  if(!host||!host.getClientRects().length)return;
  document.querySelectorAll('[data-plm-library-nav]').forEach(n=>{if(n.parentElement!==host)n.remove();});
  if(c.native)host.classList.add('plm-library-toolbar');
  const signature=[user,c.id,c.type,view,P.i18n.locale,names.get(c.id)||''].join('|');let nav=host.querySelector(':scope > [data-plm-library-nav]');
  if(nav?.dataset.signature===signature)return;
  nav?.remove();nav=P.el('div','plm-library-nav focuscontainer-x');nav.dataset.plmLibraryNav='true';nav.dataset.signature=signature;
  const heading=P.el('h1','plm-library-heading',names.get(c.id)||P.t('PlmLibrary'));
  const tabs=P.el('nav','plm-library-tabs');tabs.setAttribute('aria-label',P.t('PlmLibraryViews'));
  for(const [value,label]of [['all','PlmAll'],['suggestions','Suggestions'],['genres','Genres'],['favorites','Favorites']]){
   const link=P.el('a','emby-button plm-library-tab',P.t(label));link.href=route(c,value);link.dataset.plmLibraryView=value;
   if(value===view)link.setAttribute('aria-current','page');link.addEventListener('click',()=>{if(value==='favorites')P.libraryRows?.rememberBackdrop(c.id);remember(c,value);});tabs.append(link);
  }
  const extras=c.type==='tv'?[['2','TabUpcoming'],['4','TabNetworks'],['5','Episodes'],['6','Collections']]:[['3','Collections'],['5','Studios']];
  const more=P.el('details','plm-library-more'),summary=P.el('summary','emby-button plm-library-tab',P.t('More'));more.append(summary);
  const menu=P.el('div','plm-library-more-menu');for(const [index,label]of extras){const link=P.el('a','emby-button',P.t(label));link.href=route(c,index);if(index===view){link.setAttribute('aria-current','page');summary.textContent=P.t(label);summary.classList.add('plm-library-extra-active');}menu.append(link);}more.append(menu);tabs.append(more);
  more.addEventListener('toggle',()=>{if(!more.open)return;menu.style.transform='none';const box=menu.getBoundingClientRect(),shift=box.left<8?8-box.left:box.right>innerWidth-8?innerWidth-8-box.right:0;if(shift)menu.style.transform='translateX('+shift+'px)';});
  nav.append(heading,tabs);host.prepend(nav);
  if(!names.has(c.id)&&!loading.has(c.id)){
   loading.add(c.id);const actor=user;P.item(c.id).then(item=>{loading.delete(c.id);if(P.owner()!==actor||!item)return;names.set(c.id,item.Name);sync();}).catch(()=>loading.delete(c.id));
  }
 }
 P.libraryNavigation={sync,context,route,name:id=>names.get(id)||''};
 window.addEventListener('hashchange',sync);window.addEventListener('plm:localechange',()=>{reset();sync();});
 document.addEventListener('click',event=>{for(const details of document.querySelectorAll('.plm-library-more[open]'))if(!details.contains(event.target))details.open=false;});
 document.addEventListener('keydown',event=>{if(event.key==='Escape')for(const details of document.querySelectorAll('.plm-library-more[open]')){details.open=false;details.querySelector('summary').focus();}});
})();
