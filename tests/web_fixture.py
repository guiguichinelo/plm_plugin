"""Synthetic browser workflow: native hearts, independent lists, management and home feed.
pip install playwright; playwright install chromium
PLM_BROWSER_CHANNEL=msedge uses installed Edge. No real accounts or media are used.
"""
import json
import os
import sys
from pathlib import Path
from urllib.parse import urlsplit
from playwright.sync_api import sync_playwright

sys.stdout.reconfigure(encoding='utf-8')
web = Path(__file__).resolve().parents[1] / 'src/Jellyfin.Plugin.Plm/Web'
fixture = r"""
window.fixture={favorite:false,played:false,lists:[{Id:'11111111111111111111111111111111',Name:'Assistir depois',WatchLater:true,Revision:0}],selections:{},counter:2};
const nativeCreate=document.createElement.bind(document);document.registerElement=()=>{};document.createElement=(name,extension)=>{if(extension&&typeof extension!=='string')throw Error('Legacy custom element API expects a name');return nativeCreate(name);};window.restoreFixtureElements=()=>{delete document.registerElement;document.createElement=nativeCreate;};
const movie=()=>({Id:'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',Name:'Synthetic film',Type:'Movie',MediaType:'Video',ProductionYear:2026,Genres:['Drama'],ImageTags:{Primary:'fixture'},UserData:{IsFavorite:fixture.favorite,Played:fixture.played}});
window.ApiClient={getUrl:(path,query)=>'http://plm-fixture.test/'+path+(query?'?'+new URLSearchParams(query):''),getCurrentUserId:()=> 'fixture-user',serverId:()=> 'fixture-server',getImageUrl:()=> 'http://plm-fixture.test/image.svg',
getItem:async()=>movie(),updateFavoriteStatus:async(user,id,value)=>{fixture.favorite=value;return movie().UserData;},getJSON:async()=>({Items:['Drama','Comedy','Action'].map(Name=>({Name})),TotalRecordCount:3}),
getItems:async(user,options={})=>{let items=(!options.IsFavorite||fixture.favorite)&&(!options.IsPlayed||fixture.played)?Array.from({length:fixture.bulk||1},(_,n)=>({...movie(),Id:n?n.toString(16).padStart(32,'0'):movie().Id,Name:'Synthetic film '+n})):[];if(options.SearchTerm)items=items.filter(i=>i.Name.includes(options.SearchTerm));const total=items.length;return {Items:items.slice(options.StartIndex||0,(options.StartIndex||0)+(options.Limit||total)),TotalRecordCount:total};},
ajax:async request=>{const url=new URL(request.url),path=url.pathname,method=request.type,body=request.data?JSON.parse(request.data):null;
if(path==='/PLM/v1/me/thumbnails')return Object.entries(fixture.art||{}).map(([Key,DataUrl])=>({Key,Version:'1'}));
if(path.endsWith('/thumbnail')){const key=path.split('/')[5];fixture.art||={};if(method==='PUT'){fixture.art[key]='data:image/jpeg;base64,'+body.jpeg;return {saved:true};}if(method==='DELETE'){delete fixture.art[key];return null;}return fixture.art[key]?{DataUrl:fixture.art[key],Version:'1'}:null;}
if(path==='/PLM/v1/me/items/memberships')return [...new Set(fixture.lists.filter(c=>!c.DeletedAt).flatMap(c=>(fixture.selections[c.Id]||[]).filter(e=>!e.DeletedAt).map(e=>e.ItemId)))];
if(path==='/PLM/v1/me/watch-later/membership')return (fixture.selections[fixture.lists[0].Id]||[]).filter(e=>!e.DeletedAt).map(e=>e.ItemId);
if(path.includes('/watch-later/items/')){fixture.selections[fixture.lists[0].Id]=body.saved?[{ItemId:path.split('/').pop(),Position:0,Media:{Status:'catalogued',Name:'Synthetic film',Kind:'Movie',Genres:['Drama']}}]:[];return {saved:body.saved};}
if(path==='/PLM/v1/me/watch-later')return structuredClone(fixture.lists[0]);
if(path==='/PLM/v1/me/preferences')return method==='GET'?(fixture.preferences||{}):(fixture.preferences=body,null);
if(path.endsWith('/memberships'))return fixture.lists.filter(c=>!c.DeletedAt).map(c=>({Collection:structuredClone(c),Contains:(fixture.selections[c.Id]||[]).some(e=>!e.DeletedAt)}));
if(path==='/PLM/v1/me/collections'&&method==='GET')return structuredClone(fixture.lists.filter(c=>!!c.DeletedAt===(url.searchParams.get('trash')==='true')));
if(path==='/PLM/v1/me/collections'&&method==='POST'){const c={Id:String(fixture.counter++).padStart(32,'0'),Name:body.name,WatchLater:false,Revision:0};fixture.lists.push(c);return structuredClone(c);}
const match=path.match(/collections\/([a-f0-9]+)/);if(match){const c=fixture.lists.find(x=>x.Id===match[1]);
if(method==='GET'){let entries=fixture.selections[c.Id]||[];if(url.searchParams.get('trash')!=='true')entries=entries.filter(e=>!e.DeletedAt);if(url.searchParams.get('progress')==='unplayed'&&fixture.played)entries=[];const total=entries.length,start=Number(url.searchParams.get('startIndex')||0),limit=Number(url.searchParams.get('limit')||total);return {Collection:structuredClone(c),TotalRecordCount:total,Items:structuredClone(entries.slice(start,start+limit))};}
const revision=body?.revision??Number(url.searchParams.get('revision'));if(revision!==c.Revision)throw {status:409};
if(path.endsWith('/items')&&method==='POST'){fixture.selections[c.Id]=[{ItemId:body.itemId,Position:0,Media:{Status:'catalogued',Name:'Synthetic film',Kind:'Movie',Genres:['Drama']}}];}
else if(path.endsWith('/restore'))c.DeletedAt=null;
else if(method==='DELETE'&&path.includes('/items/'))fixture.selections[c.Id].find(e=>e.ItemId===path.split('/').pop()).DeletedAt='2026-01-01T00:00:00Z';
else if(method==='DELETE')c.DeletedAt='2026-01-01T00:00:00Z';
else if(method==='PUT')c.Name=body.name;c.Revision++;return null;}
throw Error('Unexpected fixture request '+method+' '+path);
}};
window.renderFixture=async()=>{const host=document.querySelector('#fixture-host');if(!host)return;
for(const view of host.children)view.classList.add('hide');
document.querySelectorAll('.backdropImage').forEach(e=>e.style.backgroundImage='none');
if(location.hash==='#/home'){let home=host.querySelector('#fixture-home');if(!home){home=document.createElement('div');home.id='fixture-home';home.innerHTML='<div class="homeSectionsContainer"><div class="verticalSection"><a href="#/list?type=nextup"><h2>A seguir</h2></a></div><div class="verticalSection"><h2>Recentes</h2></div></div>';host.append(home);}home.classList.remove('hide');document.dispatchEvent(new Event('pageshow'));return;}
const name=decodeURIComponent(location.hash).includes('/manage.html')?'manage.html':'collections.html';
let view=[...host.children].find(e=>e.dataset.view===name);if(view){view.classList.remove('hide');document.dispatchEvent(new Event('pageshow'));return;}
view=document.createElement('div');view.className='page';view.dataset.view=name;host.append(view);const response=await fetch('/PLM/web/'+name);const html=await response.text();view.append(document.createRange().createContextualFragment(html));document.dispatchEvent(new Event('pageshow'));};
window.addEventListener('hashchange',renderFixture);
document.addEventListener('DOMContentLoaded',()=>{document.querySelector('#fixture-native-heart').onclick=async e=>{await ApiClient.updateFavoriteStatus('fixture-user',movie().Id,!fixture.favorite);e.currentTarget.setAttribute('data-isfavorite',String(fixture.favorite));};renderFixture();});
"""

def handle(route):
    path = urlsplit(route.request.url).path
    name = path.rsplit('/', 1)[-1]
    if '/PLM/web/locales/' in path and name.endswith('.json') and (web/'locales'/name).is_file():
        route.fulfill(body=(web/'locales'/name).read_text(encoding='utf-8'),content_type='application/json')
        return
    if path.startswith('/PLM/web/') and name in {'runtime.js','i18n.js','interactions.js','surface.js','providers.js','navigation.js','navigation-state.js','library.js','manage.js','style.css','collections.html','manage.html'}:
        mime = 'application/javascript' if name.endswith('.js') else 'text/css' if name.endswith('.css') else 'text/html; charset=utf-8'
        route.fulfill(body=(web/name).read_text(encoding='utf-8'), content_type=mime)
    elif path == '/PLM/web/settings':
        route.fulfill(json={'playlistsDisabled': True})
    elif path == '/image.svg':
        route.fulfill(content_type='image/svg+xml',body='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 450"><rect width="300" height="450" fill="#65518a"/><circle cx="150" cy="180" r="70" fill="#c1a5f4"/><text x="150" y="330" text-anchor="middle" fill="white" font-size="30">PLM fixture</text></svg>')
    else:
        body=f'''<!doctype html><html lang="pt-br"><head><meta charset="utf-8"><script>{fixture}</script><script data-plm-runtime-script src="/PLM/web/runtime.js"></script><script data-plm-i18n-script src="/PLM/web/i18n.js"></script><script src="/PLM/web/interactions.js"></script><script src="/PLM/web/surface.js"></script><script src="/PLM/web/providers.js"></script><script data-plm-navigation-script src="/PLM/web/navigation.js"></script></head><body style="background:#1e202e;color:#eee;font:16px system-ui"><nav class="skinHeader"><a href="#/home?tab=1">Favoritos</a><a href="#/playlists">Playlists</a><button id="fixture-native-heart" is="emby-ratingbutton" data-id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" data-isfavorite="false">favorite</button></nav><div class="backdropContainer"></div><main id="fixture-host"></main></body></html>'''
        body=body.replace('</head>','<style>.hide{display:none!important}.material-icons{display:inline-block;width:24px;overflow:hidden}</style></head>')
        route.fulfill(body=body,content_type='text/html; charset=utf-8')
