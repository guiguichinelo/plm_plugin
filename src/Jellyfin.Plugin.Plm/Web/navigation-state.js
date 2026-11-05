/* Keep Favorites navigation current when Plugin Pages owns the route. */
(() => {
 'use strict';
 if(document.currentScript?.hasAttribute('data-plm-navigation-state-compat')&&document.querySelector('script[data-plm-navigation-state-script]'))return;
 if(window.plmNavigationStateStarted)return;
 window.plmNavigationStateStarted=true;
 const style=document.createElement('style');
 style.dataset.plmNavigationState='true';
 style.textContent='a[data-plm-navigation-active="true"]:not(.navMenuOption){color:var(--jf-palette-primary-main,#00a4dc)!important}';
 document.head.append(style);
 let pending=false;
 let previousTitle=null,ownedTitle=null;
 const observer=new MutationObserver(schedule);
 const titleObserver=new MutationObserver(schedule);
 function activeRoute(){
  const url=new URL(location.hash.slice(1)||'/',location.origin);
  if(/^\/favorites(?:\.html)?$/.test(url.pathname))return true;
  if(/^\/home(?:\.html)?$/.test(url.pathname))return url.searchParams.get('tab')==='1';
  return /^\/userpluginsettings(?:\.html)?$/.test(url.pathname)&&
   ['/PLM/web/collections.html','/PLM/web/manage.html'].includes(url.searchParams.get('pageUrl'));
 }
 function schedule(){if(!pending){pending=true;setTimeout(sync,0);}}
 function sync(){
  pending=false;observer.disconnect();titleObserver.disconnect();
  const active=activeRoute();
  if(active&&typeof window.PLM?.t==='function'){
   if(previousTitle===null)previousTitle=document.title;
   const url=new URL(location.hash.slice(1)||'/',location.origin);
   ownedTitle=window.PLM.t(url.searchParams.get('pageUrl')==='/PLM/web/manage.html'?'PlmFavoritesSettings':'Favorites');
   if(document.title!==ownedTitle)document.title=ownedTitle;
  }else if(!active){
   if(ownedTitle!==null&&document.title===ownedTitle&&previousTitle!==null)document.title=previousTitle;
   previousTitle=null;ownedTitle=null;
  }
  for(const link of document.querySelectorAll('a[data-plm-browse],a[href="#/favorites"],a[href="#/home?tab=1"]')){
   if(active){
    if(link.getAttribute('aria-current')!=='page')link.setAttribute('aria-current','page');
    if(link.dataset.plmNavigationActive!=='true')link.dataset.plmNavigationActive='true';
   }else{
    if(link.dataset.plmNavigationActive==='true'){
     link.removeAttribute('data-plm-navigation-active');
     if(link.getAttribute('aria-current')==='page')link.removeAttribute('aria-current');
    }
   }
   if(link.classList.contains('navMenuOption'))link.classList.toggle('navMenuOption-selected',active);
   if(link.classList.contains('MuiListItemButton-root'))link.classList.toggle('Mui-selected',active);
  }
  observer.observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['href','class','aria-current']});
  titleObserver.observe(document.head,{childList:true,subtree:true,characterData:true});
 }
 window.addEventListener('hashchange',schedule);
 window.addEventListener('plm:localechange',schedule);
 if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',sync,{once:true});else sync();
})();
