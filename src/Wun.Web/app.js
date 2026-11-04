(() => {
    'use strict';
    if (window.WunUI) return;
    const words = {
        en: { availableHere:'In your library', episodesHere:'episodes in your library', both:'All', anime:'Anime', movieSingle:'Movie', seriesSingle:'Series', filters:'Filters', clearFilters:'Clear filters', searchHint:'Search a title or paste an IMDb identification.', advanced:'Advanced search', yearFrom:'Year from', yearTo:'Year to', genres:'Genres', minRating:'Minimum rating', obtained:'In your catalog', any:'Any', catalogYes:'Already obtained', catalogNo:'Not in your catalog', openCatalog:'Open in Jellyfin', seriesPresent:'In catalog — episodes available', requestEpisodes:'Request episodes', refine:'Filters apply to the provider results for this search. Up to 12 matches.', openImdb:'Open on IMDb', title:'Requests', all:'Requests', mine:'Following', search:'Search', admin:'Manage', notices:'Notifications', query:'Title or identification', kind:'Type', movie:'Movies', series:'Series', pending:'Awaiting approval', approved:'Approved', deferred:'Follow-up', rejected:'Declined', unknown:'Availability not checked', absent:'Absent from catalog', partial:'Partially available', available:'Available', request:'Request', follow:'Follow', unfollow:'Stop following', history:'History', next:'Next', previous:'Previous', empty:'No results.', loading:'Loading…', error:'Could not complete this operation. Please try again.', conflict:'Another administrator changed this request. Reload before saving.', save:'Save', cancel:'Cancel', season:'Season (optional)', episode:'Episode (optional)', message:'Reply visible to followers', note:'Internal note', review:'Review date (optional)', decision:'Decision', monitor:'Continue monitoring', yes:'Yes', no:'No', sent:'Request saved.', saved:'Saved.', read:'Mark all as read', details:'Details', every:'All states', page:'Page', continuous:'Continuous series monitoring', requested:'New request', development:'Public beta: catalog matches are checked when browsing. The release calendar is not available yet.' },
        pt: { availableHere:'No catálogo', episodesHere:'episódios no catálogo', both:'Todos', anime:'Anime', movieSingle:'Filme', seriesSingle:'Série', filters:'Filtros', clearFilters:'Limpar filtros', searchHint:'Pesquise um título ou cole uma identificação do IMDb.', advanced:'Pesquisa avançada', yearFrom:'Ano inicial', yearTo:'Ano final', genres:'Gêneros', minRating:'Nota mínima', obtained:'No seu catálogo', any:'Todos', catalogYes:'Já obtido', catalogNo:'Não está no seu catálogo', openCatalog:'Abrir no catálogo', seriesPresent:'No catálogo — episódios disponíveis', requestEpisodes:'Solicitar episódios', refine:'Os filtros refinam os resultados do provedor para esta busca. Até 12 correspondências.', openImdb:'Abrir no IMDb', title:'Solicitações', all:'Solicitações', mine:'Acompanhando', search:'Pesquisar', admin:'Gerenciar', notices:'Notificações', query:'Título ou identificação', kind:'Tipo', movie:'Filmes', series:'Séries', pending:'Aguardando aprovação', approved:'Aprovado', deferred:'Em acompanhamento', rejected:'Recusado', unknown:'Disponibilidade não verificada', absent:'Ausente do catálogo', partial:'Parcialmente disponível', available:'Disponível', request:'Solicitar', follow:'Acompanhar', unfollow:'Deixar de acompanhar', history:'Histórico', next:'Próxima', previous:'Anterior', empty:'Nenhum resultado.', loading:'Carregando…', error:'Não foi possível concluir esta operação. Tente novamente.', conflict:'Outro administrador alterou esta solicitação. Atualize antes de salvar.', save:'Salvar', cancel:'Cancelar', season:'Temporada (opcional)', episode:'Episódio (opcional)', message:'Resposta visível aos interessados', note:'Nota interna', review:'Data de revisão (opcional)', decision:'Decisão', monitor:'Continuar acompanhando', yes:'Sim', no:'Não', sent:'Solicitação salva.', saved:'Salvo.', read:'Marcar todas como lidas', details:'Detalhes', every:'Todas as situações', page:'Página', continuous:'Acompanhamento contínuo da série', requested:'Nova solicitação', development:'Beta: a presença no catálogo é verificada ao consultar. O calendário de lançamentos ainda não está disponível.' }
    };
    let root, owner, active=false, administrator=false, tab='all', start=0, filter='', ticket=0;
    let searchState={query:'',kind:'All',yearFrom:'',yearTo:'',genres:'',minRating:'',obtained:'any',cards:[]};
    let backdropUrl='',backdropPending='',backdropTimer;
    const decisions=['pending','approved','deferred','rejected'];
    const availability=['unknown','absent','partial','available'];
    const enumIndex=(value,values)=>typeof value==='number'?value:values.indexOf(String(value).toLowerCase());
    const t = key => (words[/^pt\b/i.test(document.documentElement.lang)?'pt':'en'][key] || key);
    const normalize = value => Array.isArray(value)?value.map(normalize):value&&typeof value==='object'?Object.fromEntries(Object.entries(value).map(([key,v])=>[key[0].toLowerCase()+key.slice(1),normalize(v)])):value;
    function el(tag, attrs={}, children=[]) { const node=document.createElement(tag); for(const [key,value] of Object.entries(attrs)) { if(key==='class')node.className=value; else node.setAttribute(key,value); } for(const child of children)node.append(typeof child==='string'?document.createTextNode(child):child); return node; }
    function button(label, action) {
        const b=el('button',{type:'button',class:'emby-button button-flat'},[label]);
        const icon=label===t('openCatalog')?'open_in_new':label===t('request')?'add':label===t('requestEpisodes')?'playlist_add':label===t('history')?'history':label===t('follow')?'notifications_none':null;
        if(icon)b.prepend(el('span',{class:'material-icons','aria-hidden':'true'},[icon]));
        b.addEventListener('click',action);return b;
    }
    let fieldId=0;
    function field(label, input) {const id='wun-field-'+(++fieldId);(input.matches('[data-wun-multiselect]')?input.querySelector('button'):input).id=id;return el('div',{class:'wun-field'},[el('label',{for:id},[label]),input]);}
    function select(options,value='') {const node=el('select',{class:'emby-select emby-select-withcolor'});for(const [v,label]of options)node.append(el('option',{value:v},[label]));node.value=value;return node;}
    function multiselect(options, value='All') {
        const selected=new Set(value.split(',')),wrap=el('div',{class:'wun-multiselect','data-wun-multiselect':''});
        const text=el('span',{class:'wun-multiselect-value'}),trigger=button('',()=>{popup.hidden=!popup.hidden;trigger.setAttribute('aria-expanded',String(!popup.hidden));});
        trigger.className='emby-input wun-multiselect-trigger';trigger.setAttribute('aria-label',t('kind'));trigger.setAttribute('aria-expanded','false');
        trigger.append(text,el('span',{class:'material-icons','aria-hidden':'true'},['expand_more']));
        const popup=el('div',{class:'wun-multiselect-options',role:'group','aria-label':t('kind'),hidden:''});
        const checks=[];
        function sync(){if(!selected.size)selected.add('All');const labels=options.filter(([key])=>selected.has(key)).map(([,label])=>label);text.textContent=labels.length>1?labels[0]+' +'+(labels.length-1):labels[0];trigger.title=labels.join(', ');wrap.dataset.value=[...selected].join(',');for(const [key,input]of checks)input.checked=selected.has(key);}
        for(const [key,label]of options){const input=el('input',{type:'checkbox',value:key});checks.push([key,input]);input.addEventListener('change',()=>{if(key==='All'){selected.clear();selected.add('All');}else{selected.delete('All');if(input.checked)selected.add(key);else selected.delete(key);}sync();wrap.dispatchEvent(new Event('change',{bubbles:true}));});popup.append(el('label',{class:'wun-type-check'},[input,el('span',{},[label])]));}
        wrap.append(trigger,popup);Object.defineProperty(wrap,'value',{get:()=>[...selected].join(',')});
        wrap.addEventListener('keydown',e=>{
            if(e.key==='Escape'){popup.hidden=true;trigger.setAttribute('aria-expanded','false');trigger.focus();}
            if(e.key==='Enter'&&!e.isComposing&&(e.target===trigger||e.target.matches('input[type=checkbox]'))){e.preventDefault();e.stopPropagation();if(!e.repeat)e.target.click();}
        });
        wrap.addEventListener('focusout',e=>{if(!wrap.contains(e.relatedTarget)){popup.hidden=true;trigger.setAttribute('aria-expanded','false');}});
        sync();return wrap;
    }
    document.addEventListener('pointerdown',event=>root?.querySelectorAll('[data-wun-multiselect]').forEach(wrap=>{if(!wrap.contains(event.target)){wrap.querySelector('.wun-multiselect-options').hidden=true;wrap.querySelector('button').setAttribute('aria-expanded','false');}}));
    function routeActive(){try{const url=new URL(location.hash.slice(1)||'/',location.origin==='null'?'http://localhost':location.origin);return /^\/userpluginsettings(?:\.html)?$/.test(url.pathname)&&url.searchParams.get('pageUrl')==='/WUN/web/requests.html';}catch{return false;}}
    function surfaceActive(){return active&&routeActive()&&root?.isConnected&&!!root.getClientRects().length&&owner===ApiClient.getCurrentUserId();}
    function clearSurface(){clearTimeout(backdropTimer);backdropPending='';document.getElementById('wun-page-backdrop')?.remove();if(document.body.classList.contains('wun-surface-active'))document.body.classList.remove('wun-surface-active');}
    function paintSurface(){
        if(!surfaceActive()){clearSurface();return;}
        if(!document.body.classList.contains('wun-surface-active'))document.body.classList.add('wun-surface-active');
        if(!backdropUrl)return;
        let container=document.querySelector('.backdropContainer');if(!container){container=el('div',{class:'backdropContainer'});document.body.prepend(container);}
        let layer=document.getElementById('wun-page-backdrop');if(!layer){layer=el('div',{id:'wun-page-backdrop',class:'wun-page-backdrop','aria-hidden':'true'});container.append(layer);}
        const native=container.querySelector('.backdropImage');
        const nativeFilter=native?getComputedStyle(native).filter:'none';
        const filter=nativeFilter==='none'?'blur(4px) saturate(1.2) contrast(1.2) brightness(.4)':nativeFilter;
        if(layer.style.filter!==filter)layer.style.filter=filter;
        if(layer.dataset.url!==backdropUrl){layer.style.backgroundImage='url('+JSON.stringify(backdropUrl)+')';layer.dataset.url=backdropUrl;}
        document.getElementById('wun-transition-backdrop')?.remove();
    }
    function setBackdrop(url){
        if(!surfaceActive()||!url)return;
        let parsed;try{parsed=new URL(url,location.href);}catch{return;}
        if(parsed.origin!==location.origin&&!(parsed.protocol==='https:'&&parsed.hostname==='image.tmdb.org'))return;
        if(backdropUrl===parsed.href){paintSurface();return;}
        const actor=owner;backdropPending=parsed.href;const image=new Image();
        image.onload=()=>{if(!surfaceActive()||owner!==actor||backdropPending!==parsed.href)return;backdropUrl=parsed.href;paintSurface();};
        image.onerror=()=>{};image.src=parsed.href;
    }
    async function initialBackdrop(){
        const previous=window.WunHeader?.surface;
        if(!backdropUrl&&previous?.owner===owner){
            try{const url=new URL(previous.url,location.href);if(url.origin===location.origin){backdropUrl=url.href;paintSurface();return;}}catch{}
        }
        if(backdropUrl){paintSurface();return;}if(typeof ApiClient.getItems!=='function')return;
        const actor=owner;
        try{const data=await ApiClient.getItems(owner,{Recursive:true,IncludeItemTypes:'Movie,Series',Limit:10,SortBy:'Random',ImageTypeLimit:1});
            if(!surfaceActive()||owner!==actor||backdropPending||backdropUrl)return;
            const item=(data.Items||data.items||[]).find(x=>x.BackdropImageTags?.length);
            if(item)setBackdrop(ApiClient.getImageUrl(item.Id,{type:'Backdrop',index:0,maxWidth:1920,quality:80,tag:item.BackdropImageTags[0]}));
        }catch{/* Keep the readable dark surface when no image is available. */}
    }
    window.addEventListener('hashchange',()=>activate());
    async function api(path,method='GET',data) {const result=await ApiClient.ajax({type:method,url:ApiClient.getUrl('WUN/v1/'+path),data:data===undefined?undefined:JSON.stringify(data),contentType:'application/json',dataType:'json'});return normalize(result);}
    function live(id=ticket) {return active&&routeActive()&&root?.isConnected&&owner===ApiClient.getCurrentUserId()&&id===ticket;}
    function status(text) {if(root?.isConnected)root.querySelector('.wun-status').textContent=text;}
    async function run(work) {const actor=owner;try{await work();}catch(error){if(live()&&actor===owner)status(error?.status===409&&tab==='search'?t('catalogYes'):error?.status===409?t('conflict'):t('error'));}}
    function shell() {
        root.replaceChildren(el('h1',{},[t('title')]));
        const nav=el('nav',{'aria-label':t('title')});
        for(const value of ['all','mine','search',...(administrator?['admin']:[]),'notices']) {const b=button(t(value),()=>{tab=value;start=0;render();});b.className='emby-button wun-tab';if(value===tab)b.setAttribute('aria-current','page');nav.append(b);}
        root.append(nav,el('div',{class:'wun-content'},[el('p',{class:'wun-status',role:'status'})]));
        return root.querySelector('.wun-content');
    }
    async function render() {
        if(!live())return;
        const id=++ticket, content=shell();status(t('loading'));
        if(tab==='search') {renderSearch(content);status('');return;}
        await run(async()=>{
            if(tab==='notices') {
                const notices=await api('notifications');if(!live(id))return;
                content.append(button(t('read'),()=>run(async()=>{await api('notifications/read','POST',{});await render();})));
                for(const notice of notices) content.append(el('p',{},[(notice.event==='request'?t('requested'):t(notice.event.toLowerCase()))+' #'+notice.requestId+(notice.read?'':' •')]));
                status(notices.length?'':t('empty'));return;
            }
            const query=new URLSearchParams({start,limit:30,mine:tab==='mine'});if(filter!=='')query.set('decision',filter);
            const result=await api('requests?'+query);if(!live(id))return;
            const states=select([['',t('every')],...decisions.map((x,i)=>[String(i),t(x)])],filter);
            states.addEventListener('change',()=>{filter=states.value;start=0;render();});content.append(field(t('decision'),states));
            const rows=el('div',{class:'wun-rows'});for(const card of result.items)rows.append(requestRow(card));content.append(rows);
            const previous=button(t('previous'),()=>{start=Math.max(0,start-30);render();});previous.disabled=start===0;
            const next=button(t('next'),()=>{start+=30;render();});next.disabled=!result.hasMore;
            if(start>0||result.hasMore)content.append(el('div',{class:'wun-pager'},[previous,t('page')+' '+(start/30+1),next]));
            if(!result.items.length){
                const search=button(t('search'),()=>{tab='search';start=0;render();});search.className='emby-button button-flat';
                content.append(el('div',{class:'wun-empty'},[el('span',{class:'material-icons','aria-hidden':'true'},['movie_filter']),el('h2',{},[t('empty')]),search]));
            }
            status('');
        });
    }
    function titleNode(title, imdbId) {
        const valid=/^tt[0-9]{7,12}$/.test(imdbId||'');
        const href=valid?'https://www.imdb.com/title/'+imdbId+'/':'https://www.imdb.com/find/?q='+encodeURIComponent(title);
        return el('h2',{},[el('a',{href,target:'_blank',rel:'noopener noreferrer',title:t('openImdb'),class:'wun-title-link'},[title])]);
    }
    function presenceNode(card) {
        const present=card.catalog?.obtained===true;
        const series=card.kind==='Series'||card.selection?.kind===1||card.selection?.kind==='Series';
        if(!present)return el('p',{class:'wun-presence'},[t('catalogNo')]);
        const complete=card.catalog?.complete===true;
        const label=complete?(/^pt\b/i.test(document.documentElement.lang)?'Completa no catálogo':'Complete in library'):series&&card.catalog.episodeCount!=null?String(card.catalog.episodeCount)+' '+t('episodesHere'):t('availableHere');
        return el('p',{class:'wun-presence'},[el('span',{class:'wun-presence-badge'+(series&&!complete?' wun-presence-partial':'')},[el('span',{class:'material-icons','aria-hidden':'true'},[series&&!complete?'video_library':'check_circle']),label])]);
    }
    function catalogButton(card) {
        const id=card.catalog?.itemId;
        return /^(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i.test(id||'')?button(t('openCatalog'),()=>{location.hash='/details?id='+encodeURIComponent(id);}):null;
    }
    function requestRow(card) {
        const s=card.selection, kind=typeof s.kind==='number'?s.kind:(s.kind==='Movie'?0:1);
        const row=el('article',{class:'wun-row'},[titleNode(s.title,card.imdbId),el('p',{},[t(decisions[enumIndex(card.decision,decisions)])+' · '+t(availability[enumIndex(card.availability,availability)]||'unknown')])]);
        if(s.season!==null&&s.season!==undefined)row.append(el('p',{},['S'+s.season+(s.episode!=null?' · E'+s.episode:'')]));
        if(card.catalog){row.append(presenceNode(card));const open=catalogButton(card);if(open)row.append(open);}
        if(card.monitor)row.append(el('p',{},[t('continuous')]));
        const actions=el('div',{class:'wun-actions'},[button(t(tab==='mine'?'unfollow':'follow'),()=>run(async()=>{await api('requests/'+card.id+'/follow?enabled='+(tab!=='mine'),'PUT',{});await render();})),button(t('history'),()=>showHistory(card))]);
        actions.prepend(button(t('details'),()=>showDetails({kind:kind===0?'Movie':'Series',tmdbId:s.tmdbId,title:s.title,catalog:card.catalog,imdbId:card.imdbId})));
        if(tab==='admin'&&administrator)actions.append(button(t('admin'),()=>edit(card)));row.append(actions);return row;
    }
    function renderSearch(content) {
        const query=el('input',{class:'emby-input',type:'search',maxlength:'150',required:'',minlength:'2',autocomplete:'off',placeholder:t('query')+' / tt0083658 / imdb:tt0083658 / tmdb:78'});
        const kind=multiselect([['All',t('both')],['Movie',t('movie')],['Series',t('series')],['Anime',/^pt\b/i.test(document.documentElement.lang)?'Animes':t('anime')]],searchState.kind);
        const yearFrom=el('input',{class:'emby-input',type:'number',min:'1800',max:'2200',step:'1'}),yearTo=yearFrom.cloneNode();
        const genres=el('input',{class:'emby-input',maxlength:'200',placeholder:'Drama, …'}),minRating=el('input',{class:'emby-input',type:'number',min:'0',max:'10',step:'.1',placeholder:'0–10'});
        const obtained=select([['any',t('any')],['yes',t('catalogYes')],['no',t('catalogNo')]],'any');
        for(const [name,input]of Object.entries({query,yearFrom,yearTo,genres,minRating,obtained}))input.value=searchState[name];
        const advanced=el('div',{class:'wun-advanced',id:'wun-advanced-filters',hidden:''},[
            el('div',{class:'wun-filter-grid'},[field(t('yearFrom'),yearFrom),field(t('yearTo'),yearTo),field(t('genres'),genres),field(t('minRating'),minRating),field(t('obtained'),obtained)])
        ]);
        const toggle=button(t('filters'),()=>{advanced.hidden=!advanced.hidden;toggle.setAttribute('aria-expanded',String(!advanced.hidden));});
        toggle.className='paper-icon-button-light wun-filters-toggle';toggle.title=t('filters');toggle.setAttribute('aria-label',t('filters'));toggle.setAttribute('aria-controls','wun-advanced-filters');toggle.setAttribute('aria-expanded','false');
        toggle.replaceChildren(el('span',{class:'material-icons','aria-hidden':'true'},['tune']));
        const clear=button(t('clearFilters'),()=>{yearFrom.value='';yearTo.value='';genres.value='';minRating.value='';obtained.value='any';toggle.classList.remove('wun-filters-active');form.dispatchEvent(new Event('change'));});clear.className='emby-button wun-clear';advanced.append(clear);
        advanced.addEventListener('input',()=>toggle.classList.toggle('wun-filters-active',[yearFrom,yearTo,genres,minRating].some(x=>x.value)||obtained.value!=='any'));
        const submit=el('button',{class:'emby-button button-flat wun-submit',type:'submit'},[el('span',{class:'material-icons','aria-hidden':'true'},['search']),t('search')]);
        const searchField=field(t('query'),query);searchField.classList.add('wun-query');
        const kindField=field(t('kind'),kind);kindField.classList.add('wun-kind');
        const form=el('form',{class:'wun-search-form'},[el('div',{class:'wun-tools wun-search-bar'},[searchField,kindField,toggle,submit]),advanced]);
        const results=el('div',{class:'wun-rows wun-search-results'});content.append(form,el('p',{class:'wun-hint'},[t('searchHint')]),results);
        const remember=()=>{for(const [name,input]of Object.entries({query,kind,yearFrom,yearTo,genres,minRating,obtained}))searchState[name]=input.value;};
        form.addEventListener('input',remember);form.addEventListener('change',remember);
        function draw(cards){results.replaceChildren();
            for(const card of cards){
                const actions=el('div',{class:'wun-actions'}),open=catalogButton(card);actions.append(button(t('details'),()=>showDetails(card)));if(open)actions.append(open);
                if(!card.catalog?.obtained||card.kind==='Series')actions.append(button(t(card.catalog?.obtained?'requestEpisodes':'request'),()=>card.kind==='Series'?showDetails(card):request(card)));
                const meta=el('div',{class:'wun-result-meta'},[el('span',{class:'wun-type'},[t(card.kind==='Series'?'seriesSingle':'movieSingle')]),...(card.isAnime?[el('span',{class:'wun-type'},[t('anime')])]:[]),...(card.year?[el('span',{},[String(card.year)])]:[]),...(card.rating>0?[el('span',{},['★ '+new Intl.NumberFormat(document.documentElement.lang||'en',{minimumFractionDigits:1,maximumFractionDigits:1}).format(card.rating)])]:[])]);
                const body=el('div',{class:'wun-result-body'},[titleNode(card.title,card.imdbId),meta,el('p',{class:'wun-result-genres'},[(card.genres||[]).join(' · ')]),...(card.catalog?[presenceNode(card)]:[]),actions]);
                const row=el('article',{class:'wun-row'+(card.catalog?.obtained?' wun-available':'')},[body]);
                row.dataset.wunTitle=card.kind+':'+card.tmdbId;
                const cover=el('div',{class:'wun-cover-wrap'});
                if(card.imageUrl&&new URL(card.imageUrl).hostname==='image.tmdb.org')cover.append(el('img',{class:'wun-cover',src:card.imageUrl,alt:'',loading:'lazy',decoding:'async',referrerpolicy:'no-referrer'}));else cover.append(el('div',{class:'wun-cover-placeholder','aria-hidden':'true'},[el('span',{class:'material-icons'},[card.kind==='Series'?'tv':'movie'])]));
                if(card.catalog?.obtained)cover.append(el('span',{class:'wun-cover-status'+(card.kind==='Series'?' wun-cover-partial':''),'aria-hidden':'true',title:card.kind==='Series'?t('seriesPresent'):t('availableHere')},[el('span',{class:'material-icons'},[card.kind==='Series'?'video_library':'check_circle'])]));
                row.prepend(cover);row.addEventListener('pointerenter',()=>{clearTimeout(backdropTimer);backdropTimer=setTimeout(()=>setBackdrop(card.backdropUrl),180);});row.addEventListener('pointerleave',()=>clearTimeout(backdropTimer));row.addEventListener('focusin',()=>setBackdrop(card.backdropUrl));results.append(row);
            }
            const first=cards.find(card=>card.backdropUrl);if(first)setBackdrop(first.backdropUrl);
        }
        if(searchState.cards.length)draw(searchState.cards);
        form.addEventListener('keydown',event=>{
            if((event.key!=='Enter'&&event.code!=='NumpadEnter')||event.isComposing||event.keyCode===229)return;
            if(!event.target.matches('input[type=search],input[type=number],input[type=text],input:not([type])'))return;
            event.preventDefault();event.stopPropagation();
            if(!event.repeat&&live())form.requestSubmit(submit);
        },true);
        form.addEventListener('submit',event=>{event.preventDefault();const id=++ticket;status(t('loading'));run(async()=>{
            const params=new URLSearchParams({query:query.value,kind:kind.value,obtained:obtained.value});
            for(const [name,input]of Object.entries({yearFrom,yearTo,genres,minRating}))if(input.value)params.set(name,input.value);
            const cards=await api('search?'+params);if(!live(id))return;
            searchState.cards=cards;draw(cards);
            status(cards.length?'':t('empty'));
        });});
    }
    async function showDetails(card) {
        const pt=/^pt\b/i.test(document.documentElement.lang);
        const labels=pt?{complete:'Completa no catálogo',current:'Todos os episódios lançados disponíveis',partial:'Episódios faltantes',unknown:'Cobertura ainda não confirmada',ended:'Encerrada',ongoing:'Em andamento',season:'Temporada',available:'Disponível',missing:'Não disponível',future:'Previsto',undated:'Sem data anunciada',watch:'Solicitar acompanhamento de novos episódios',special:'Especiais'}:{complete:'Complete in library',current:'All released episodes available',partial:'Missing episodes',unknown:'Coverage not confirmed',ended:'Ended',ongoing:'Ongoing',season:'Season',available:'Available',missing:'Unavailable',future:'Expected',undated:'No announced date',watch:'Request monitoring for new episodes',special:'Specials'};
        const node=el('dialog',{class:'wun-details','aria-label':card.title});
        function cover(url,cls){
            const art=el('div',{class:cls,'aria-hidden':'true'},[el('span',{class:'material-icons'},[card.kind==='Series'?'tv':'movie'])]);
            if(url){try{const parsed=new URL(url);if(parsed.protocol==='https:'&&parsed.hostname==='image.tmdb.org'){const img=el('img',{src:parsed.href,alt:'',decoding:'async',referrerpolicy:'no-referrer'});img.onerror=()=>img.remove();art.append(img);}}catch{}}
            return art;
        }
        let poster=cover(card.imageUrl,'wun-title-cover');
        const subtitle=el('p',{class:'wun-details-meta'}),titleInfo=el('div',{class:'wun-details-title'},[el('h2',{},[card.title]),subtitle]);
        const heading=el('div',{class:'wun-details-heading'},[poster,titleInfo,button(pt?'Fechar':'Close',()=>node.close())]);
        const close=heading.querySelector('button');close.className='paper-icon-button-light wun-details-close';close.setAttribute('aria-label',pt?'Fechar':'Close');close.title=pt?'Fechar':'Close';close.replaceChildren(el('span',{class:'material-icons','aria-hidden':'true'},['close']));
        const content=el('div',{class:'wun-details-content'},[t('loading')]);node.append(heading,content);node.addEventListener('close',()=>node.remove(),{once:true});root.append(node);node.showModal();
        const actor=owner;let generation=0;
        async function load(season){
            const version=++generation;content.setAttribute('aria-busy','true');
            try{
                const data=await api('details?'+new URLSearchParams({kind:card.kind,tmdbId:card.tmdbId,...(season==null?{}:{season})}));
                if(!node.isConnected||actor!==owner||!live()||version!==generation)return;
                const posterUrl=data.imageUrl||card.imageUrl;
                if(posterUrl&&poster.dataset.url!==posterUrl){const next=cover(posterUrl,'wun-title-cover');next.dataset.url=posterUrl;poster.replaceWith(next);poster=next;}
                subtitle.textContent=[t(card.kind==='Series'?'seriesSingle':'movieSingle'),data.year].filter(Boolean).join(' · ');
                card.catalog={...card.catalog,...data.catalog,complete:data.complete,completeReleased:data.completeReleased};
                const result=[...root.querySelectorAll('[data-wun-title]')].find(e=>e.dataset.wunTitle===card.kind+':'+card.tmdbId);
                if(result){result.querySelector('.wun-presence')?.replaceWith(presenceNode(card));
                    if(data.complete){result.querySelector('.wun-cover-status')?.classList.remove('wun-cover-partial');const icon=result.querySelector('.wun-cover-status .material-icons');if(icon)icon.textContent='check_circle';for(const b of result.querySelectorAll('.wun-actions button'))if(b.textContent.trim()===t('requestEpisodes'))b.hidden=true;}
                }
                content.replaceChildren();content.append(el('p',{class:'wun-synopsis'},[data.overview||'']));
                if(card.kind!=='Series'){
                    content.append(el('p',{},[[data.year,data.runtimeMinutes?data.runtimeMinutes+' min':null].filter(Boolean).join(' · ')]));
                    const open=catalogButton(card);if(open)content.append(open);else content.append(button(t('request'),()=>request(card)));return;
                }
                const label=!data.providerKnown?labels.unknown:data.complete?labels.complete:data.completeReleased?labels.current:labels.partial;
                content.append(el('div',{class:'wun-coverage'+(data.completeReleased?' wun-coverage-complete':'')},[el('strong',{},[label]),el('span',{},[data.owned+' / '+data.released+' · '+(data.ended?labels.ended:data.status==='Unknown'?labels.unknown:labels.ongoing)])]));
                if(!data.ended)content.append(button(labels.watch,()=>request({...card,season:null,episode:null})));
                const seasons=el('div',{class:'wun-season-picker',role:'group','aria-label':labels.season});
                for(const s of data.seasons||[]){
                    const name=s.number===0?labels.special:labels.season+' '+s.number;
                    const option=button(name,()=>load(s.number));option.className='wun-season-option';option.setAttribute('aria-pressed',String(s.number===data.selectedSeason));
                    option.replaceChildren(cover(s.imageUrl,'wun-season-cover'),el('span',{},[el('strong',{},[name]),el('span',{},[String(s.expected)+' '+(pt?'episódios':'episodes')])]));
                    seasons.append(option);
                }
                content.append(el('h3',{class:'wun-season-label'},[labels.season]),seasons);
                if((data.episodes||[]).some(ep=>!ep.available))content.append(button(t('request')+' · '+labels.season+' '+data.selectedSeason,()=>request({...card,season:data.selectedSeason,episode:null})));
                const list=el('div',{class:'wun-episodes'});content.append(list);
                for(const ep of data.episodes||[]){
                    const marker=el('span',{class:'material-icons','aria-hidden':'true'},[ep.available?'check_circle':ep.released?'remove_circle_outline':'event']);
                    const date=ep.airDate?new Intl.DateTimeFormat(document.documentElement.lang||'en',{dateStyle:'medium',timeZone:'UTC'}).format(new Date(ep.airDate)):labels.undated;
                    const text=el('div',{class:'wun-episode-text'},[el('h3',{},[String(ep.number).padStart(2,'0')+' · '+(ep.title||'')]),el('p',{class:'wun-episode-state'},[(ep.available?labels.available:ep.released?labels.missing:labels.future)+' · '+date]),el('p',{},[ep.overview||''])]);
                    const action=ep.available?button(t('openCatalog'),()=>{node.close();location.hash='/details?id='+encodeURIComponent(ep.itemId);}):button(t('request'),()=>request({...card,season:data.selectedSeason,episode:ep.number}));
                    const row=el('article',{class:'wun-episode'+(ep.available?' wun-episode-owned':'')},[marker,text,action]);
                    if(ep.imageUrl){try{const imageUrl=new URL(ep.imageUrl);if(imageUrl.protocol==='https:'&&imageUrl.hostname==='image.tmdb.org'){
                        const thumb=el('div',{class:'wun-episode-art'},[el('img',{src:imageUrl.href,alt:'',loading:'lazy',decoding:'async',referrerpolicy:'no-referrer'}),marker]);
                        thumb.querySelector('img').onerror=()=>{row.classList.remove('wun-episode-with-thumb');thumb.replaceWith(marker);};row.classList.add('wun-episode-with-thumb');row.prepend(thumb);
                    }}catch{}}
                    list.append(row);
                }
            }catch{if(node.isConnected&&actor===owner)content.textContent=t('error');}finally{content.removeAttribute('aria-busy');}
        }
        await load();
    }
    function dialog(title, fields, save) {
        const node=el('dialog',{},[el('h2',{},[title])]);for(const f of fields)node.append(f);
        const close=()=>{node.close();node.remove();};
        const submit=button(t('save'),()=>run(async()=>{submit.disabled=true;try{await save();close();}finally{submit.disabled=false;}}));
        node.append(el('div',{class:'wun-actions'},[button(t('cancel'),close),submit]));node.addEventListener('close',()=>node.remove(),{once:true});root.append(node);node.showModal();return node;
    }
    function request(card) {
        const season=el('input',{class:'emby-input',type:'number',min:'0',step:'1'}),episode=el('input',{class:'emby-input',type:'number',min:'1',step:'1'});
        season.value=card.season??'';episode.value=card.episode??'';
        dialog(card.title,card.kind==='Series'?[field(t('season'),season),field(t('episode'),episode)]:[],async()=>{
            if(!season.checkValidity()||!episode.checkValidity()||(episode.value&&!season.value)){season.reportValidity();throw new Error('invalid scope');}
            await api('requests','POST',{kind:card.kind,tmdbId:card.tmdbId,season:season.value?Number(season.value):null,episode:episode.value?Number(episode.value):null});status(t('sent'));
        });
    }
    function edit(card) {
        const decision=select(decisions.map((x,i)=>[String(i),t(x)]),String(enumIndex(card.decision,decisions)));
        const message=el('textarea',{class:'emby-input',maxlength:'2000'}),note=el('textarea',{class:'emby-input',maxlength:'4000'}),review=el('input',{class:'emby-input',type:'date'}),monitor=select([['true',t('yes')],['false',t('no')]],String(card.monitor));
        dialog(card.selection.title,[field(t('decision'),decision),field(t('message'),message),field(t('note'),note),field(t('review'),review),field(t('monitor'),monitor)],async()=>{await api('admin/requests/'+card.id,'PUT',{revision:card.revision,decision:Number(decision.value),message:message.value,internalNote:note.value,reviewAt:review.value?review.value+'T12:00:00Z':null,monitor:monitor.value==='true'});await render();});
    }
    async function showHistory(card) {const id=ticket;await run(async()=>{const entries=await api((administrator&&tab==='admin'?'admin/':'')+'requests/'+card.id+'/history');if(!live(id))return;const rows=entries.map(entry=>{const item=entry.public||entry;return el('article',{class:'wun-row'},[el('p',{},[new Intl.DateTimeFormat(document.documentElement.lang||'en',{dateStyle:'medium',timeStyle:'short'}).format(new Date(item.created))+' · '+t(decisions[enumIndex(item.decision,decisions)])]),el('p',{},[item.message||'']),...(entry.internalNote?[el('p',{},[t('note')+': '+entry.internalNote])]:[])]);});const node=el('dialog',{},[el('h2',{},[card.selection.title]),...rows,button(t('cancel'),()=>node.close())]);node.addEventListener('close',()=>node.remove(),{once:true});root.append(node);node.showModal();});}
    async function activate() {
        if(!root?.isConnected){active=false;clearSurface();return;}
        const visible=routeActive()&&!!root.getClientRects().length,current=ApiClient.getCurrentUserId();
        if(!visible||!current){if(active){active=false;ticket++;root.querySelectorAll('dialog').forEach(x=>x.remove());}clearSurface();return;}
        if(active&&owner===current){paintSurface();return;}
        const changed=owner!==current;
        if(changed){clearSurface();backdropUrl='';searchState={query:'',kind:'All',yearFrom:'',yearTo:'',genres:'',minRating:'',obtained:'any',cards:[]};tab='all';start=0;filter='';}
        active=true;owner=current;const activationTicket=++ticket;root.replaceChildren();paintSurface();initialBackdrop();
        try{const me=await api('me');if(!live(activationTicket))return;administrator=me.administrator;if(tab==='admin'&&!administrator)tab='all';await render();}
        catch{if(live(activationTicket))root.textContent=t('error');}
    }

    let scheduled=false;new MutationObserver(()=>{if(!scheduled){scheduled=true;requestAnimationFrame(()=>{scheduled=false;activate();});}}).observe(document.documentElement,{subtree:true,childList:true,attributes:true,attributeFilter:['class']});
    window.WunUI={mount(node){if(root!==node){active=false;ticket++;root=node;}activate();}};
})();
