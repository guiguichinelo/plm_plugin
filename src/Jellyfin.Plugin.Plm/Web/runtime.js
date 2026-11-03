(() => {
 'use strict';if(window.PLM)return;
 const version='ux7',routes={library:'#/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fcollections.html',manage:'#/userpluginsettings.html?pageUrl=%2FPLM%2Fweb%2Fmanage.html'};
 const t=(...args)=>window.PLM.t(...args);
 let cacheOwner='',cache=new Map();
 const owner=()=>window.ApiClient?.getCurrentUserId()||'';
 const api=()=>window.ApiClient;
 const normalize=x=>Array.isArray(x)?x.map(normalize):x&&typeof x==='object'?Object.fromEntries(Object.entries(x).map(([k,v])=>[k[0].toLowerCase()+k.slice(1),normalize(v)])):x;
 const call=(path,method='GET',body)=>{const id=owner();return api().ajax({url:api().getUrl('PLM/v1/me/'+path),type:method,contentType:'application/json',dataType:method==='GET'||method==='POST'?'json':undefined,data:body===undefined?undefined:JSON.stringify(body)}).then(value=>{if(id!==owner())throw Error('session_changed');return normalize(value);});};
 const el=(tag,cls,text)=>{const e=document.createElement(tag);if(cls)e.className=cls;if(text!==undefined)e.textContent=text;return e;};
 const button=(label,work,cls='plm-button')=>{const b=el('button',cls,label);b.type='button';if(work)b.onclick=work;return b;};
 const icon=name=>{const s=el('span','material-icons',name);s.setAttribute('aria-hidden','true');return s;};
 const name=c=>c.watchLater?t("PlmWatchLater"):c.name;
 const changed=()=>{cache.clear();window.dispatchEvent(new CustomEvent('plm:changed',{detail:{owner:owner()}}));};
 function styles(){if(document.getElementById('plm-styles'))return;const link=el('link');link.id='plm-styles';link.rel='stylesheet';link.href=api()?.getUrl('PLM/web/style.css')+'?v='+version;document.head.append(link);}
 async function item(id,refresh=false){
  const user=owner();if(user!==cacheOwner){cacheOwner=user;cache.clear();}
  const found=cache.get(id);if(!refresh&&found&&Date.now()-found.at<60000)return found.value;
  let value;try{value=await api().getItem(user,id);}catch{return null;}
  if(user!==owner())throw Error('session_changed');if(cache.size>=128)cache.delete(cache.keys().next().value);cache.set(id,{at:Date.now(),value});return value;
 }
 async function map(items,work){const result=new Array(items.length);let next=0;await Promise.all(Array.from({length:Math.min(4,items.length)},async()=>{while(next<items.length){const index=next++;result[index]=await work(items[index],index);}}));return result;}
 function image(media,wide=false){
  if(!media)return '';
  let id=media.Id,type='Primary',tag=media.ImageTags?.Primary;
  if(wide&&media.BackdropImageTags?.length){type='Backdrop';tag=media.BackdropImageTags[0];}
  else if(wide&&media.ParentBackdropItemId&&media.ParentBackdropImageTags?.length){id=media.ParentBackdropItemId;type='Backdrop';tag=media.ParentBackdropImageTags[0];}
  else if(!tag&&media.SeriesPrimaryImageTag){id=media.SeriesId;tag=media.SeriesPrimaryImageTag;}
  else if(!tag&&media.ParentThumbImageTag){id=media.ParentThumbItemId;type='Thumb';tag=media.ParentThumbImageTag;}
  if(!id||!tag)return '';
  return api().getImageUrl(id,{type,tag,maxWidth:wide?1000:360,quality:85});
 }
 const detail=id=>'#/details?id='+encodeURIComponent(id)+'&serverId='+encodeURIComponent(api().serverId());
 const canList=media=>['Movie','Series','Season','Episode','Video','MusicVideo','Audio','MusicAlbum','AudioBook','Book','Photo','PhotoAlbum'].includes(media.Type);
 const routeFor=media=>{const query={Genre:'genreId',MusicGenre:'musicGenreId',Studio:'studioId'}[media.Type];return query?'#/list?'+query+'='+encodeURIComponent(media.Id)+'&serverId='+encodeURIComponent(api().serverId()):detail(media.Id);};
 const title=m=>m.Type==='Episode'&&m.SeriesName?m.SeriesName:m.Name;
 const subtitle=m=>m.Type==='Episode'?['S'+(m.ParentIndexNumber??'?')+' · E'+(m.IndexNumber??'?'),m.Name].join(' — '):[m.ProductionYear,(m.Genres||[])[0]].filter(Boolean).join(' · ');
 async function favorite(media,value){
  const user=owner();
  if(typeof api().updateFavoriteStatus==='function')await api().updateFavoriteStatus(owner(),media.Id,value);
  else await api().ajax({url:api().getUrl('UserFavoriteItems/'+media.Id),type:value?'POST':'DELETE'});
  if(owner()!==user)throw Error('session_changed');media.UserData={...media.UserData,IsFavorite:value};
  for(const b of document.querySelectorAll('button[is="emby-ratingbutton"][data-id]'))if(b.dataset.id===media.Id){b.setAttribute('data-isfavorite',String(value));b.classList.toggle('ratingbutton-withrating',value);b.querySelector('.material-icons')?.classList.toggle('ratingbutton-icon-withrating',value);}
  changed();
 }
 function container(cls=''){const c=typeof document.registerElement==='function'?document.createElement('div','emby-itemscontainer'):document.createElement('div',{is:'emby-itemscontainer'});c.setAttribute('is','emby-itemscontainer');c.setAttribute('data-multiselect','false');c.setAttribute('data-contextmenu','false');c.className='itemsContainer plm-cards '+cls;setTimeout(()=>c.classList.toggle('plm-native-ready',typeof c.resume==='function'),150);return c;}
 function card(media,{wide=false}={}){
  const c=el('article','card card-withuserdata plm-card'+(wide?' plm-card-wide':''));Object.assign(c.dataset,{id:media.Id,serverid:api().serverId(),type:media.Type,mediatype:media.MediaType||'',isfolder:String(media.IsFolder||false)});
  const art=el('div','plm-art');const link=el('a','itemAction plm-art-link');link.href=routeFor(media);link.dataset.action='link';link.setAttribute('aria-label',t("PlmOpen")+title(media));
  const url=image(media,wide);if(url){const img=el('img');img.src=url;img.alt='';img.loading='lazy';img.decoding='async';link.append(img);}else link.append(icon(media.Type==='Audio'?'music_note':'movie'));
  art.append(link);
  const actions=el('div','plm-card-actions');const play=button(' ',null,'itemAction plm-icon-button plm-play');play.dataset.action='play';play.title=t("Play");play.setAttribute('aria-label',t("PlmPlay")+title(media));play.replaceChildren(icon('play_arrow'));if(!['Person','Genre','MusicGenre','Studio'].includes(media.Type))actions.append(play);
  const heart=button(' ',async e=>{e.stopPropagation();heart.disabled=true;try{await favorite(media,!media.UserData?.IsFavorite);heart.setAttribute('aria-pressed',String(!!media.UserData?.IsFavorite));heart.classList.toggle('plm-liked',!!media.UserData?.IsFavorite);}finally{heart.disabled=false;}},'plm-icon-button plm-heart'+(media.UserData?.IsFavorite?' plm-liked':''));heart.title=t('PlmLikeAction');heart.setAttribute('aria-label',t("PlmLike")+title(media));heart.setAttribute('aria-pressed',String(!!media.UserData?.IsFavorite));heart.replaceChildren(icon('favorite'));actions.append(heart);
  const save=button(' ',e=>{e.stopPropagation();picker(media);},'plm-icon-button plm-save-button');save.title=t("PlmSavetolists");save.setAttribute('aria-label',t("PlmSave")+title(media)+t("Plmtolists"));save.replaceChildren(icon('playlist_add'));if(canList(media))actions.append(save);art.append(actions);c.append(art);
  const text=el('a','plm-card-title',title(media));text.href=routeFor(media);c.append(text,el('p','plm-card-subtitle',subtitle(media)));
  const data=media.UserData||{};if(data.PlaybackPositionTicks>0&&media.RunTimeTicks>0&&!data.Played){const progress=el('progress','plm-progress');progress.max=media.RunTimeTicks;progress.value=data.PlaybackPositionTicks;progress.setAttribute('aria-label',t("PlmPlaybackprogress"));art.append(progress);}
  return c;
 }
 function empty(host,heading,text,action){const e=el('div','plm-empty');e.append(icon('bookmark_border'),el('h3','',heading),el('p','plm-muted',text));if(action)e.append(action);host.append(e);}
 async function inputDialog(heading,value=''){
  return new Promise(resolve=>{styles();const d=el('dialog','plm-ui plm-dialog');const form=el('form');form.method='dialog';const h=el('h2','',heading),input=el('input','plm-input');input.value=value;input.maxLength=120;input.required=true;input.setAttribute('aria-label',heading);const footer=el('div','plm-dialog-footer'),cancel=button(t("ButtonCancel"),()=>d.close());const submit=el('button','plm-button plm-primary',t("Save"));submit.type='submit';footer.append(cancel,submit);form.append(h,input,footer);form.onsubmit=e=>{e.preventDefault();if(input.value.trim()){resolve(input.value.trim());d.close();}};d.onclose=()=>{resolve(null);d.remove();};d.append(form);document.body.append(d);d.showModal();input.focus();input.select();});
 }
 async function picker(media){
  styles();document.querySelector('.plm-picker')?.close();const user=owner(),d=el('dialog','plm-ui plm-dialog plm-picker');const header=el('div','plm-dialog-heading'),close=button(t("ButtonClose"),()=>d.close(),'plm-button');header.append(el('h2','',t("PlmFavoritesandlists")),close);const status=el('p','plm-status',t("PlmLoadingyourlists")),rows=el('div','plm-picker-rows');d.append(header,el('p','plm-muted',title(media)),rows,status);d.onclose=()=>d.remove();document.body.append(d);d.showModal();
  function row(label,checked,work,kind){const l=el('label','plm-picker-row');const input=el('input');input.type='checkbox';input.checked=checked;input.setAttribute('aria-label',label);l.append(icon(kind),el('span','',label),input);input.onchange=async()=>{const value=input.checked;input.disabled=true;status.textContent=t("PlmSaving");try{await work(value);status.textContent=t("PlmSaved");changed();}catch(e){input.checked=!value;status.textContent=e.status===409?t("PlmThislistchangedinanothersessionCloseandreopenit"):t("PlmCouldnotsaveTryagain");}finally{input.disabled=false;}};rows.append(l);}
  try{
   const memberships=await call('items/'+media.Id+'/memberships');if(user!==owner()||!d.isConnected){d.close();return;}
   row(t("PlmLiked"),!!media.UserData?.IsFavorite,value=>favorite(media,value),'favorite');
   for(const m of memberships){const c=m.collection;row(name(c),m.contains,async value=>{if(value)await call('collections/'+c.id+'/items','POST',{itemId:media.Id,revision:c.revision});else await call('collections/'+c.id+'/items/'+media.Id+'?revision='+c.revision,'DELETE');c.revision++;},c.watchLater?'watch_later':'queue');}
   const create=button(t("PlmNewlist"),async()=>{const label=await inputDialog(t("PlmListname"));if(!label)return;const c=await call('collections','POST',{name:label});await call('collections/'+c.id+'/items','POST',{itemId:media.Id,revision:c.revision});changed();d.close();picker(media);});d.append(create);status.textContent=t("PlmChoosethelistsforthiscontent");
  }catch{status.textContent=t("PlmCouldnotloadyourlists");}
 }
 async function entries(id,{page=0,genre='',progress='',limit=40,trash=false}={}){const query=new URLSearchParams({startIndex:String(page*limit),limit:String(limit),genre,progress,trash:String(trash)});const result=await call('collections/'+id+'?'+query);const values=await map(result.items,async e=>({...e,native:e.media.status==='catalogued'?await item(e.itemId):null}));return {...result,items:values};}
 async function watchCandidates(){
  const c=await call('watch-later','POST');const data=await entries(c.id,{limit:100,progress:'unplayed'});const resolved=await map(data.items,async e=>{
   const media=e.native;if(!media||media.UserData?.Played)return null;
   if(media.Type==='Series'||media.Type==='Season'){
    if(media.Type==='Series'){
     const next=await api().getJSON(api().getUrl('Shows/NextUp',{UserId:owner(),SeriesId:media.Id,Limit:1,EnableResumable:true,EnableRewatching:false}));if(next.Items?.length)return {...next.Items[0],PlmSelectionId:e.itemId};
    }
    const result=await api().getItems(owner(),{ParentId:media.Id,Recursive:true,IncludeItemTypes:'Episode',IsPlayed:false,SortBy:'ParentIndexNumber,IndexNumber',SortOrder:'Ascending',Limit:1});return result.Items?.[0]?{...result.Items[0],PlmSelectionId:e.itemId}:null;
   }
   return ['Movie','Episode','Video','MusicVideo'].includes(media.Type)?media:null;
  });return {id:c.id,items:[...new Map(resolved.filter(Boolean).map(m=>[m.Id,m])).values()]};
 }
 window.PLM={version,routes,owner,api,normalize,call,el,button,icon,name,changed,styles,item,map,image,detail,title,subtitle,favorite,container,card,empty,inputDialog,picker,entries,watchCandidates};
})();
