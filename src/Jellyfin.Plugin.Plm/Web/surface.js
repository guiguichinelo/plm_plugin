/* Own only PLM's page lifecycle and backdrop, never the host backdrop slots. */
(() => {
 'use strict';const P=window.PLM;if(!P||P.surface)return;
 let mounted=null;
 function route(){const url=new URL(location.hash.slice(1)||'/',location.origin);return /^\/userpluginsettings(?:\.html)?$/.test(url.pathname)&&url.searchParams.get('pageUrl')==='/PLM/web/collections.html';}
 function visible(m){return !!m&&m.owner===P.owner()&&route()&&m.root.isConnected&&!m.root.closest('.hide,[hidden]')&&m.root.getClientRects().length>0;}
 function clear(){document.getElementById('plm-page-backdrop')?.remove();if(document.body.classList.contains('plm-browsing-backdrop'))document.body.classList.remove('plm-browsing-backdrop');}
 function paint(){
  const m=mounted;if(!m?.active||!visible(m)||!m.url){clear();return;}
  let container=document.querySelector('.backdropContainer');if(!container){container=P.el('div','backdropContainer');document.body.prepend(container);}
  let node=document.getElementById('plm-page-backdrop');if(!node){node=P.el('div','plm-page-backdrop');node.id='plm-page-backdrop';node.setAttribute('aria-hidden','true');container.append(node);}
  if(node.dataset.url!==m.url){node.style.backgroundImage='url('+JSON.stringify(m.url)+')';node.dataset.url=m.url;}
  const nativeImage=container.querySelector('.backdropImage'),filter=nativeImage?getComputedStyle(nativeImage).filter:'none';
  if(node.style.filter!==filter)node.style.filter=filter;
  if(!document.body.classList.contains('plm-browsing-backdrop'))document.body.classList.add('plm-browsing-backdrop');
 }
 function sync(){
  const m=mounted;if(!m)return;
  if(!m.root.isConnected){mounted=null;clear();m.callbacks.leave?.();m.callbacks.destroy?.();return;}
  const active=visible(m),key=new URL(location.hash.slice(1)||'/',location.origin).searchParams.get('list')||'';
  if(active!==m.active||active&&m.key!==key){if(m.active)m.callbacks.leave?.();m.active=active;m.key=key;if(active){paint();m.callbacks.enter();}else clear();}else if(active)paint();
 }
 function mount(root,callbacks){if(mounted?.root===root){sync();return;}if(mounted){mounted.callbacks.leave?.();mounted.callbacks.destroy?.();}clear();mounted={root,callbacks,owner:P.owner(),active:false,url:''};sync();}
 P.surface={mount,sync,isActive:root=>mounted?.root===root&&mounted.active&&visible(mounted),route};
 P.setBackdrop=(media,root)=>{if(!mounted||root&&root!==mounted.root||!visible(mounted))return;mounted.url=typeof media==='string'?media:media?P.image(media,true):'';paint();};
 P.clearBackdrop=clear;
 window.addEventListener('hashchange',sync);
 document.addEventListener('pageshow',sync,true);
 document.addEventListener('viewshow',sync,true);
})();
