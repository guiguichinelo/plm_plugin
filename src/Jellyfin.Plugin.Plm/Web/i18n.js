/* The host sets html.lang from the user's interface preference. Never use media language. */
(() => {
 'use strict';const P=window.PLM;if(!P||P.i18n)return;
 let loaded=false,locale='',dictionary={},english={},manifest=null,generation=0;const cache=new Map();
 const normalize=value=>(value||'en-us').replaceAll('_','-').toLowerCase();
 const current=()=>normalize(document.documentElement.lang||document.documentElement.dataset.culture||navigator.language);
 const url=name=>P.api()?.getUrl('PLM/web/locales/'+name+'.json')||new URL('locales/'+name+'.json',document.querySelector('script[data-plm-i18n-script]')?.src||document.baseURI).href;
 async function load(name){if(!cache.has(name))cache.set(name,fetch(url(name)).then(r=>{if(!r.ok)throw Error('locale_unavailable');return r.json();}).catch(error=>{cache.delete(name);throw error;}));return cache.get(name);}
 function text(key,...args){let value=dictionary[key]??english[key]??key;return value.replace(/\{(\d+)\}/g,(match,index)=>args[index]===undefined?match:String(args[index]));}
 function apply(root=document){for(const [attribute,target]of [['text',null],['aria-label','aria-label'],['title','title'],['placeholder','placeholder']]){
  const selector='[data-plm-'+attribute+']',nodes=[...(root.matches?.(selector)?[root]:[]),...root.querySelectorAll(selector)];for(const node of nodes){const value=text(node.getAttribute('data-plm-'+attribute));if(target){if(node.getAttribute(target)!==value)node.setAttribute(target,value);}else if(node.textContent!==value)node.textContent=value;}
 }}
 async function refresh(){
  const request=++generation,next=current();try{manifest=manifest||await load('index');english=await load('en-us');const base=next.split('-')[0],resolved=manifest.locales[next]?next:manifest.locales[base]?base:'en-us';const selected=await load(resolved);if(request!==generation)return;
   const changed=locale!==next;dictionary=selected;locale=next;apply();if(changed){document.querySelectorAll('dialog.plm-ui').forEach(d=>d.close());window.dispatchEvent(new Event('plm:localechange'));window.dispatchEvent(new Event('plm:changed'));}
  }catch{if(request===generation){locale=next;dictionary=english;apply();}}finally{if(request===generation)loaded=true;}
 }
 P.t=text;P.i18n={apply,refresh,get loaded(){return loaded;},get locale(){return locale||current();},get coverage(){return manifest;},number:value=>new Intl.NumberFormat(locale||current()).format(value),date:value=>new Intl.DateTimeFormat(locale||current()).format(new Date(value)),count:value=>text(value===1?'PlmSingleItem':'PlmItemCount',new Intl.NumberFormat(locale||current()).format(value))};
 new MutationObserver(()=>{if(current()!==locale)P.i18n.ready=refresh();}).observe(document.documentElement,{attributes:true,attributeFilter:['lang','data-culture']});
 P.i18n.ready=refresh();
})();
