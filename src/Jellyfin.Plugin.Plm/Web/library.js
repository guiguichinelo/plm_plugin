(() => {
 'use strict';const P=window.PLM,root=document.getElementById('plm-app');if(!P?.interactionsReady||!root||root.dataset.controller)return;root.dataset.controller='1';P.styles();P.i18n.apply(root);
 const user=P.owner(),$=id=>root.querySelector('#plm-'+id);let selected='liked',lists=[],art=[],page=0,size=40,total=0,request=0,view='grid',searchTimer;
 let filters={genres:[],types:'',progress:'',search:'',sort:'name',descending:false,year:'',favorite:''};
 const alive=()=>P.surface.isActive(root)&&P.owner()===user;const status=(text,error=false)=>{if(alive()){$('status').textContent=text;$('status').setAttribute('role',error?'alert':'status');}};
 selected=new URL(location.href.split('#')[1]||'/',location.origin).searchParams.get('list')||'liked';if(selected!=='liked')filters.sort='custom';
 const safe=work=>async()=>{try{await work();}catch{status(P.t("PlmCouldnotcompletethisactionRefreshandtryagain"),true);}};
 async function create(){const name=await P.inputDialog(P.t("PlmListname"));if(!name)return;const c=await P.call('collections','POST',{name});selected=c.id;page=0;filters.sort='custom';P.changed();}
 function removed(id){if(selected===id){selected='liked';filters.sort='name';page=0;}}
 function sidebar(){
  $('lists').replaceChildren();const heading=P.el('div','plm-sidebar-head'),plus=P.button('',safe(create),'plm-icon-button');plus.title=P.t("PlmCreatealist");plus.setAttribute('aria-label',P.t("PlmCreatealist"));plus.append(P.icon('add'));heading.append(P.el('p','plm-sidebar-label',P.t("PlmYourlibrary")),plus);$('lists').append(heading);
  const choices=[{id:'liked',name:P.t("PlmLiked")},...lists];
  for(const c of choices){const row=P.el('div','plm-list-row'),b=P.button('',()=>{if(selected!==c.id){selected=c.id;page=0;filters.sort=c.id==='liked'?'name':'custom';sidebar();load();}},'plm-list-button'),glyph=P.icon(c.id==='liked'?'favorite':c.watchLater?'watch_later':'queue');b.setAttribute('aria-current',String(selected===c.id));b.append(glyph,P.el('span','',c.id==='liked'?P.t("PlmLiked"):P.name(c)));
   row.oncontextmenu=e=>P.listMenu(e,c,removed);b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')P.listMenu(e,c,removed);};
   const more=P.button('',e=>P.listMenu(e,c,removed),'plm-icon-button plm-list-more');more.title=P.t("PlmListoptions");more.setAttribute('aria-label',P.t("PlmOptionsfor")+(c.id==='liked'?P.t("PlmLiked"):P.name(c)));more.append(P.icon('more_vert'));row.append(b,more);$('lists').append(row);
   if(art.some(a=>a.key===c.id.replaceAll('-','')))P.bindThumbnail(glyph,c.id);
  }
 }
 function chips(){
  $('active-filters').replaceChildren();for(const genre of filters.genres){const b=P.button('',()=>{filters.genres=filters.genres.filter(g=>g!==genre);page=0;chips();load();},'plm-filter-chip');b.append(P.el('span','',genre),P.icon('close'));b.setAttribute('aria-label',P.t("PlmRemovefilter")+genre);$('active-filters').append(b);}
  const active=filters.types||filters.progress||filters.year||filters.favorite!==''||filters.search||filters.genres.length;$('filters').classList.toggle('plm-filter-active',!!active);$('genres').textContent=filters.genres.length?P.t('Genres')+' ('+P.i18n.number(filters.genres.length)+')':P.t("Genres");
  if(active)$('active-filters').append(P.button(P.t("PlmClearfilters"),()=>{filters={...filters,genres:[],types:'',progress:'',search:'',year:'',favorite:''};$('search').value='';page=0;chips();load();},'plm-button plm-clear-filters'));
 }
 async function cover(c,items,ticket){const cell=$('list-cover');cell.replaceChildren(P.icon(selected==='liked'?'favorite':c?.watchLater?'watch_later':'queue'));const backdrop=items.find(i=>!i.unavailable&&P.image(i,true));P.setBackdrop(backdrop||null,root);
  const chosen=selected;if(art.some(a=>a.key===chosen.replaceAll('-',''))){const url=await P.thumbnail(chosen);if(url&&ticket===request&&alive()){const img=P.el('img');img.src=url;img.alt='';cell.replaceChildren(img);if(!backdrop)P.setBackdrop(url,root);}}
 }
 async function load(){
  const ticket=++request;status(P.t("PlmLoading"));$('media').setAttribute('aria-busy','true');
  try{
   const c=lists.find(l=>l.id===selected);let items=[];
   if(selected!=='liked'&&!c){selected='liked';filters.sort='name';sidebar();return load();}
   const data=await P.libraryProvider.page({owner:user,id:selected,filters:{...filters,genres:[...filters.genres]},page,size});items=data.items;total=data.total;
   if(ticket!==request||!alive())return;
   if(page>0&&page*size>=total){page=Math.max(0,Math.ceil(total/size)-1);return load();}
   $('list-title').textContent=selected==='liked'?P.t("PlmLiked"):P.name(c);$('list-description').textContent=selected==='liked'?P.t("PlmContentsavedtoLiked"):c.watchLater?P.t("PlmMoviesshowsandothercontenttowatchlater"):'';$('count').textContent=P.i18n.count(total);$('media').replaceChildren();
   await cover(c,items,ticket);if(ticket!==request||!alive())return;
   if(!items.length){const active=filters.genres.length||filters.search||filters.progress||filters.types||filters.year||filters.favorite!=='';P.empty($('media'),active?P.t("PlmNocontentmatchesthesefilters"):P.t("PlmThislistisempty"),active?P.t("PlmChangeorclearthefilterstocontinue"):P.t("PlmUsethehearttochooseLikedoralistTheclockaddsdirectlytoWatchLater"));}
   else{const cards=P.container(view==='list'?'plm-list-view':'');for(const item of items){if(item.unavailable){const unavailable=P.el('article','plm-card');const art=P.el('div','plm-art');art.append(P.icon('lock'));unavailable.append(art,P.el('p','plm-card-title',P.t("PlmContentunavailable")),P.el('p','plm-card-subtitle',P.t("PlmYourselectionhasbeenkept")));cards.append(unavailable);}else cards.append(P.card(item));}$('media').append(cards);}
   $('previous').disabled=page===0;$('next').disabled=(page+1)*size>=total;$('page-number').textContent=P.t('PlmPageOf',P.i18n.number(page+1),P.i18n.number(Math.max(1,Math.ceil(total/size))));status('');
  }catch(e){if(ticket===request)status(P.t("PlmCouldnotloadthislistTryrefreshingthepage"),true);}finally{if(ticket===request&&alive())$('media').removeAttribute('aria-busy');}
 }
 $('previous').onclick=()=>{page=Math.max(0,page-1);load();};$('next').onclick=()=>{page++;load();};$('page-size').onchange=()=>{size=Number($('page-size').value);page=0;load();};
 $('search').oninput=()=>{clearTimeout(searchTimer);searchTimer=setTimeout(()=>{filters.search=$('search').value.trim();page=0;chips();load();},350);};
 $('genres').onclick=safe(async()=>{const chosen=await P.genreDialog(filters.genres);if(chosen){filters.genres=chosen;page=0;chips();load();}});
 $('filters').onclick=safe(async()=>{const chosen=await P.filtersDialog(filters,selected==='liked');if(chosen){filters=chosen;page=0;chips();load();}});
 $('sort').onclick=()=>{const d=P.el('dialog','plm-ui plm-dialog'),heading=P.el('h2','',P.t("Sort")),sort=P.el('select','plm-input');sort.setAttribute('aria-label',P.t("PlmSortby"));for(const [id,label]of [['custom',P.t("PlmListorder")],['name',P.t("LabelTitle")],['year',P.t("LabelYear")],['created',P.t("PlmDateaddedtothelibrary")],['premiere',P.t("PlmReleasedate")]])if(id!=='custom'||selected!=='liked')sort.append(new Option(label,id));sort.value=filters.sort;const order=P.el('select','plm-input');order.setAttribute('aria-label',P.t("PlmDirection"));order.append(new Option(P.t("Ascending"),'asc'),new Option(P.t("Descending"),'desc'));order.value=filters.descending?'desc':'asc';const footer=P.el('div','plm-dialog-footer');footer.append(P.button(P.t("ButtonCancel"),()=>d.close()),P.button(P.t('PlmApply'),()=>{filters.sort=sort.value;filters.descending=order.value==='desc';page=0;d.close();load();},'plm-button plm-primary'));d.append(heading,sort,order,footer);d.onclose=()=>d.remove();document.body.append(d);d.showModal();};
 $('view').onclick=()=>{view=view==='grid'?'list':'grid';$('view').replaceChildren(P.icon(view==='grid'?'view_list':'view_module'));$('view').setAttribute('aria-label',view==='grid'?P.t("PlmListview"):P.t("PlmGridview"));$('media').querySelector('.plm-cards')?.classList.toggle('plm-list-view',view==='list');};
 async function init(){try{await P.call('watch-later','POST');[lists,art]=await Promise.all([P.call('collections'),P.artworks()]);if(!alive())return;sidebar();chips();await load();}catch{status(P.t("PlmCouldnotloadyourlists"),true);}}
 const refresh=()=>{if(alive())init();};window.addEventListener('plm:changed',refresh);P.surface.mount(root,{enter:()=>{const id=new URL(location.hash.slice(1),location.origin).searchParams.get('list');if(id&&id!==selected){selected=id;page=0;filters.sort=id==='liked'?'name':'custom';}init();},leave:()=>{request++;clearTimeout(searchTimer);},destroy:()=>window.removeEventListener('plm:changed',refresh)});
})();
