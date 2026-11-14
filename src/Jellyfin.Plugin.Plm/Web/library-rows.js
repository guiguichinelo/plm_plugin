/* Library favorites are content shelves. List management stays in global Favorites. */
(()=>{
 'use strict';const P=window.PLM;if(!P||P.libraryRows)return;
 let backdropOwner='';const backdrops=new Map();
 function rememberBackdrop(id){
  if(backdropOwner!==P.owner()){backdropOwner=P.owner();backdrops.clear();}
  const image=[...document.querySelectorAll('.backdropImage')].reverse().find(e=>getComputedStyle(e).backgroundImage!=='none');if(!image)return;
  const css=getComputedStyle(image).backgroundImage;if(!css.startsWith('url('))return;
  try{backdrops.set(id,{url:JSON.parse(css.slice(4,-1)),at:Date.now()});}catch{}
 }
 async function backdrop(id,items,live,root){
  const actor=P.owner();if(backdropOwner!==actor){backdropOwner=actor;backdrops.clear();}
  const cached=backdrops.get(id);if(cached&&Date.now()-cached.at<300000){if(live())P.setBackdrop(cached.url,root);return;}
  try{
   let media=items.find(i=>i.BackdropImageTags?.length||i.ParentBackdropImageTags?.length);
   if(!media){const result=await P.api().getItems(actor,{ParentId:id,Recursive:true,ImageTypes:'Backdrop',EnableImageTypes:'Backdrop',ImageTypeLimit:1,Limit:1,IncludeItemTypes:P.libraryNavigation?.context()?.type==='tv'?'Series':'Movie',SortBy:'IsFavoriteOrLiked,Random',EnableTotalRecordCount:false});media=result.Items?.find(i=>i.BackdropImageTags?.length);}
   if(!live()||P.owner()!==actor)return;const url=media?P.image(media,true):'';if(url){if(backdrops.size>=12)backdrops.delete(backdrops.keys().next().value);backdrops.set(id,{url,at:Date.now()});}P.setBackdrop(url||null,root);
  }catch{if(live()&&P.owner()===actor)P.setBackdrop(null,root);}
 }
 function pageUrl(id,mode='list'){
  const url=new URL(location.hash.slice(1),location.origin);url.searchParams.set('list',id);url.searchParams.set('mode',mode);return '#'+url.pathname+'?'+url.searchParams;
 }
 async function render({host,filters,start=0,append=false,live,onMore,onClear}){
  const actor=P.owner(),query=new URLSearchParams({libraryId:filters.libraryId,startIndex:String(start),limit:'6',rowLimit:'10',sort:filters.sort==='custom'?'name':filters.sort,descending:String(filters.descending)});
  for(const [key,value]of Object.entries({genres:filters.genres.join('|'),types:filters.types,progress:filters.progress,search:filters.search,year:filters.year,favorite:filters.favorite}))if(value!==''&&value!==undefined)query.set(key,String(value));
  const data=await P.call('library-sections?'+query);if(!live()||P.owner()!==actor)return;
  const sections=data.sections||[],ids=[...new Set(sections.flatMap(s=>s.itemIds||[]))];let items=[];
  if(ids.length){const result=await P.api().getItems(actor,{Ids:ids.join(','),Recursive:true,Fields:'Genres,PrimaryImageAspectRatio',EnableImageTypes:'Primary,Backdrop',ImageTypeLimit:1,Limit:ids.length,EnableTotalRecordCount:false});items=result.Items||[];}
  if(!live()||P.owner()!==actor)return;
  const byId=new Map(items.map(i=>[i.Id.replaceAll('-','').toLowerCase(),i]));
  if(!append)host.replaceChildren();host.querySelector('.plm-more-sections')?.remove();
  for(const section of sections){
   const rowItems=(section.itemIds||[]).map(id=>byId.get(id.replaceAll('-','').toLowerCase())).filter(Boolean);if(!rowItems.length)continue;
   const row=P.el('section','verticalSection plm-library-row');row.dataset.plmRowList=section.id;
   const name=section.liked?P.t('PlmLiked'):section.watchLater?P.t('PlmWatchLater'):section.name;
   const head=P.el('div','sectionTitleContainer sectionTitleContainer-cards plm-library-row-heading'),title=P.el('h2','sectionTitle sectionTitle-cards',name),link=P.el('a','clearLink button-flat sectionTitleTextButton plm-row-title');link.href=pageUrl(section.id);link.setAttribute('aria-label',name+' — '+P.i18n.count(section.totalRecordCount));link.append(title,P.icon('chevron_right'));head.append(link);
   const viewport=P.el('div','plm-row-viewport'),cards=P.container('plm-library-row-cards'),controls=P.el('div','emby-scrollbuttons plm-row-controls');cards.setAttribute('aria-label',name);for(const item of rowItems){const card=P.card(item),box=P.el('div','cardBox cardBox-bottompadded');card.classList.add('overflowPortraitCard','card-hoverable');box.append(...card.childNodes);card.append(box);card.querySelector('.plm-card-title')?.classList.add('cardText','cardTextCentered','cardText-first');card.querySelector('.plm-card-subtitle')?.classList.add('cardText','cardTextCentered');cards.append(card);}
   const direction=getComputedStyle(host).direction==='rtl'?-1:1;
   const previous=P.button('',()=>cards.scrollBy({left:-direction*cards.clientWidth*.8,behavior:'smooth'}),'paper-icon-button-light emby-scrollbuttons-button btnPrev plm-row-arrow');previous.append(P.icon(direction===1?'chevron_left':'chevron_right'));previous.setAttribute('aria-label',P.t('Previous'));
   const next=P.button('',()=>cards.scrollBy({left:direction*cards.clientWidth*.8,behavior:'smooth'}),'paper-icon-button-light emby-scrollbuttons-button btnNext plm-row-arrow');next.append(P.icon(direction===1?'chevron_right':'chevron_left'));next.setAttribute('aria-label',P.t('Next'));
   const sync=()=>{const max=cards.scrollWidth-cards.clientWidth,pos=Math.abs(cards.scrollLeft);previous.disabled=pos<=1;next.disabled=max<=1||pos>=max-1;};cards.addEventListener('scroll',sync,{passive:true});cards.addEventListener('plm:row-resize',sync);previous.onfocus=next.onfocus=sync;controls.append(previous,next);viewport.append(cards);row.append(head,controls,viewport);host.append(row);requestAnimationFrame(sync);
  }
  const filtered=filters.genres.length||filters.types||filters.progress||filters.search||filters.year||filters.favorite!=='';
  if(!host.querySelector('.plm-library-row')&&!data.hasMore){
   const empty=P.el('div','plm-library-empty centerMessage');empty.append(P.el('h2','',P.t(filtered?'PlmNoSavedMatches':'PlmNoLibraryFavorites')),P.el('p','plm-muted',P.t(filtered?'PlmChangeorclearthefilterstocontinue':'PlmLibraryFavoritesHint')));
   if(filtered)empty.append(P.button(P.t('PlmClearfilters'),onClear,'emby-button'));
   else{const c=P.libraryNavigation?.context();if(c){const link=P.el('a','emby-button',P.t('PlmExploreCatalog'));link.href=P.libraryNavigation.route(c,'all');empty.append(link);}}
   host.append(empty);
  }
  await backdrop(filters.libraryId,items,live);
  if(data.hasMore){const more=P.button(P.t('PlmMoreLists'),()=>onMore(start+sections.length),'emby-button plm-more-sections');host.append(more);}
  return data;
 }
 P.libraryRows={render,pageUrl,backdrop,rememberBackdrop};
 window.addEventListener('resize',()=>{for(const cards of document.querySelectorAll('.plm-library-row-cards'))cards.dispatchEvent(new Event('plm:row-resize'));});
})();
