/* Favorites remain native Jellyfin data; personal lists use the private PLM API. */
(() => {
 'use strict';const P=window.PLM;if(!P)return;
 P.libraryProvider={
  async page({owner,id,filters,page,size}){
   if(id!=='liked'){const data=await P.entries(id,{...filters,page,limit:size});return {total:data.totalRecordCount,items:data.items.map(e=>e.native||{unavailable:true,ItemId:e.itemId})};}
   const options={Recursive:true,IsFavorite:true,IncludeItemTypes:filters.types||'Movie,Series,Season,Episode,Video,MusicVideo,Audio,MusicAlbum,AudioBook,Book,Photo,PhotoAlbum,BoxSet,Person,MusicArtist,Studio,Genre,MusicGenre,TvChannel,Program,Recording',Fields:'Genres,PrimaryImageAspectRatio',StartIndex:page*size,Limit:size,Genres:filters.genres.length?filters.genres.join('|'):undefined,SearchTerm:filters.search||undefined,Years:filters.year||undefined,SortBy:({name:'SortName',year:'ProductionYear',created:'DateCreated',premiere:'PremiereDate'})[filters.sort]||'SortName',SortOrder:filters.descending?'Descending':'Ascending',IsPlayed:filters.progress==='played'?true:filters.progress==='unplayed'?false:undefined,IsResumable:filters.progress==='resume'?true:undefined,EnableTotalRecordCount:true};
   const result=await P.api().getItems(owner,options);if(P.owner()!==owner)throw Error('session_changed');return {total:result.TotalRecordCount||0,items:result.Items||[]};
  }
 };
})();
