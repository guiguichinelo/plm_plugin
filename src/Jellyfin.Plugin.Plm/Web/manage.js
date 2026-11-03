(() => {
 'use strict';const P=window.PLM,root=document.getElementById('plm-manage');if(!P||!root||root.dataset.controller)return;root.dataset.controller='1';P.styles();P.i18n.apply(root);const user=P.owner(),$=id=>root.querySelector('#plm-'+id);let lists=[],removedPage=0;
 const alive=()=>root.isConnected&&P.owner()===user;const message=text=>{if(alive())$('manage-status').textContent=text;};
 const run=async work=>{try{await work();}catch(e){message(e.status===409?P.t("PlmThislistchangedinanothersessionRefreshandtryagain"):P.t("PlmCouldnotcompletethisactionTryagain"));}};
 function row(label,note,kind){const r=P.el('div','plm-manage-row'),body=P.el('div','plm-manage-label'),actions=P.el('div','plm-manage-actions');body.append(P.el('strong','',label),P.el('small','',note));r.append(P.icon(kind),body,actions);return {r,actions};}
 function openList(c){location.hash=P.routes.library+'&list='+encodeURIComponent(c.id);}
 async function load(){
  await P.call('watch-later','POST');const [active,trash]=await Promise.all([P.call('collections'),P.call('collections?trash=true')]);if(!alive())return;lists=active;$('managed-lists').replaceChildren();$('trash-lists').replaceChildren();
  const native=row(P.t("PlmLiked"),P.t("PlmAddedusingtheJellyfinheart"),'favorite');native.actions.append(P.button(P.t('PlmOpenAction'),()=>location.hash=P.routes.library));$('managed-lists').append(native.r);
  for(const c of lists){const line=row(P.name(c),c.watchLater?P.t("PlmShownonHomeafterNextUp"):P.t("PlmPrivatelistOnlyyoucanseethisselection"),c.watchLater?'watch_later':'queue');line.actions.append(P.button(P.t('PlmOpenAction'),()=>openList(c)));
   if(!c.watchLater){line.actions.append(P.button(P.t("ButtonRename"),()=>run(async()=>{const name=await P.inputDialog(P.t("PlmListname"),c.name);if(name){await P.call('collections/'+c.id,'PUT',{name,revision:c.revision});P.changed();await load();}})),P.button(P.t("Delete"),()=>run(async()=>{if(!window.confirm(P.t("PlmMovethislisttothetrash")))return;await P.call('collections/'+c.id+'?revision='+c.revision,'DELETE');P.changed();await load();message(P.t("PlmListmovedtothetrash"));})));}
   $('managed-lists').append(line.r);
  }
  for(const c of trash){const line=row(c.name,P.t("PlmDeletedon")+P.i18n.date(c.deletedAt),'delete_outline');line.actions.append(P.button(P.t("LabelRestore"),()=>run(async()=>{await P.call('collections/'+c.id+'/restore','POST',{revision:c.revision});P.changed();await load();message(P.t("PlmListrestored"));})));$('trash-lists').append(line.r);}
  if(!trash.length)$('trash-lists').append(P.el('p','plm-muted',P.t("PlmNolistsinthetrash")));
  const previous=$('removed-list').value;$('removed-list').replaceChildren(...lists.map(c=>new Option(P.name(c),c.id)));if(lists.some(c=>c.id===previous))$('removed-list').value=previous;const prefs=await P.call('preferences');if(alive())$('show-home').checked=prefs.homeWatchLater!==false;message('');if(new URL(location.hash.slice(1),location.origin).searchParams.get('trash')==='1'){$('trash-lists').closest('details').open=true;$('trash-lists').closest('details').scrollIntoView({block:'start'});}
 }
 async function removed(){
  const id=$('removed-list').value;if(!id)return;const data=await P.entries(id,{page:removedPage,limit:100,trash:true});if(!alive())return;$('removed-items').replaceChildren();
  for(const entry of data.items.filter(i=>i.deletedAt)){const line=row(entry.native?P.title(entry.native):P.t("PlmContentunavailable"),P.t("PlmRemovedon")+P.i18n.date(entry.deletedAt),'restore');line.actions.append(P.button(P.t("LabelRestore"),()=>run(async()=>{await P.call('collections/'+id+'/items/'+entry.itemId+'/restore','POST',{revision:data.collection.revision});P.changed();await removed();message(P.t("PlmContentrestored"));})));$('removed-items').append(line.r);}
  if(!$('removed-items').childElementCount)$('removed-items').append(P.el('p','plm-muted',P.t("PlmNoremovedselectionsonthispage")));$('removed-next').hidden=(removedPage+1)*100>=data.totalRecordCount;
 }
 $('show-home').onchange=()=>run(async()=>{const prefs=await P.call('preferences');prefs.homeWatchLater=$('show-home').checked;await P.call('preferences','PUT',prefs);P.changed();message(P.t("PlmPreferencesaved"));});
 $('new-list').onclick=()=>run(async()=>{const name=await P.inputDialog(P.t("PlmListname"));if(name){await P.call('collections','POST',{name});P.changed();await load();message(P.t("PlmListcreated"));}});
 $('export-lists').onclick=()=>run(async()=>{const data=await P.call('export'),url=URL.createObjectURL(new Blob([JSON.stringify(data,null,2)],{type:'application/json'})),a=P.el('a');a.href=url;a.download='plm-lists.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
 $('import-lists').onchange=()=>run(async()=>{const file=$('import-lists').files[0];if(!file)return;if(file.size>2097152){message(P.t("PlmChooseafileupto2MB"));return;}await P.call('import','POST',JSON.parse(await file.text()));P.changed();await load();message(P.t("PlmListsimported"));$('import-lists').value='';});
 $('removed-list').onchange=()=>{removedPage=0;run(removed);};$('removed-next').onclick=()=>{removedPage++;run(removed);};$('removed-list').closest('details').ontoggle=()=>{if($('removed-list').closest('details').open)run(removed);};const languageChanged=()=>{if(!root.isConnected){window.removeEventListener('plm:localechange',languageChanged);return;}P.i18n.apply(root);run(load);};window.addEventListener('plm:localechange',languageChanged);run(load);
})();
