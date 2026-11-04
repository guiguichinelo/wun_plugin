/* WUN header integration. Independent of server branding and PLM. */
(() => {
    'use strict';
    if (window.WunHeader?.surfaceVersion===4) return;
    document.querySelectorAll("[data-wun-header]").forEach(node=>node.remove());
    const state = window.WunHeader = { ready: false, surfaceVersion:4 };
    const page = '/WUN/web/requests.html';
    const transitionStyle=document.createElement('style');
    transitionStyle.textContent='body.wun-route-pending .backgroundContainer{opacity:var(--wun-host-background-opacity,1)!important;transition:none!important}body.wun-route-pending .backdropContainer{opacity:1!important;transition:none!important}#wun-transition-backdrop{position:absolute;inset:0;z-index:3;background-size:cover;background-position:center;pointer-events:none;transition:none!important;animation:none!important}body.wun-route-pending #wun-page-backdrop{transition:none!important;animation:none!important}';
    document.head.append(transitionStyle);
    function prepareSurface(){
        const owner=window.ApiClient?.getCurrentUserId();if(!owner)return;
        const hostBackground=document.querySelector('.backgroundContainer');
        if(hostBackground)document.body.style.setProperty('--wun-host-background-opacity',getComputedStyle(hostBackground).opacity);
        const candidates=[...document.querySelectorAll('#wun-page-backdrop,#plm-page-backdrop,.backdropImage')];
        const source=candidates.find(node=>getComputedStyle(node).backgroundImage!=='none'&&node.getClientRects().length);
        if(source){
            const css=getComputedStyle(source),match=css.backgroundImage.match(/^url\(["']?(.*?)["']?\)$/);
            if(match){state.surface={owner,url:match[1],filter:css.filter};
                let layer=document.getElementById('wun-transition-backdrop');if(!layer){layer=document.createElement('div');layer.id='wun-transition-backdrop';layer.setAttribute('aria-hidden','true');source.parentElement.append(layer);}
                layer.style.backgroundImage=css.backgroundImage;layer.style.filter=css.filter;
            }
        }
        if(!document.getElementById('wun-transition-backdrop')&&state.surface?.owner===owner){
            const container=document.querySelector('.backdropContainer');
            if(container){const layer=document.createElement('div');layer.id='wun-transition-backdrop';layer.setAttribute('aria-hidden','true');layer.style.backgroundImage='url('+JSON.stringify(state.surface.url)+')';layer.style.filter=state.surface.filter;container.append(layer);}
        }
        document.body.classList.add('wun-route-pending');
    }
    document.addEventListener('click',event=>{if(event.target.closest('[data-wun-header]')&&state.ready)prepareSurface();},true);
    window.addEventListener('hashchange',()=>{
        const current=new URL(location.hash.slice(1)||'/',location.origin==='null'?'http://localhost':location.origin);
        if(current.searchParams.get('pageUrl')===page)document.body.classList.add('wun-route-pending');
        else{if(document.body.classList.contains('wun-route-pending'))document.body.classList.remove('wun-route-pending');document.getElementById('wun-transition-backdrop')?.remove();}
    });
    let queued = false;
    function update() {
        queued = false;
        const route=new URL(location.hash.slice(1)||'/',location.origin==='null'?'http://localhost':location.origin);
        if(route.searchParams.get('pageUrl')!==page){
            if(document.body.classList.contains('wun-route-pending'))document.body.classList.remove('wun-route-pending');
            document.getElementById('wun-transition-backdrop')?.remove();
        }
        const user=window.ApiClient?.getCurrentUserId();
        if(state.surface?.owner!==user)state.surface=null;
        const native=[...document.querySelectorAll('.backdropImage,#plm-page-backdrop')].find(e=>getComputedStyle(e).backgroundImage.startsWith('url('));
        if(native&&user){const css=getComputedStyle(native),match=css.backgroundImage.match(/^url\(["']?(.*?)["']?\)$/);if(match)state.surface={owner:user,url:match[1],filter:css.filter};}
        const portuguese = /^pt\b/i.test(document.documentElement.lang);
        const label = portuguese ? 'Solicitações' : 'Requests';
        const headers = [...document.querySelectorAll('.skinHeader .headerRight')];
        document.querySelectorAll('header button[aria-controls="app-sync-play-menu"],header button[aria-controls="app-remote-play-menu"]').forEach(anchor => {
            if (!headers.includes(anchor.parentElement)) headers.push(anchor.parentElement);
        });
        headers.forEach(header => {
            const anchor = header.querySelector('.headerSyncButton,.headerCastButton,[aria-controls="app-sync-play-menu"],[aria-controls="app-remote-play-menu"]');
            let button = header.querySelector('[data-wun-header]');
            if (!button) {
                button = document.createElement('button');
                button.type = 'button';
                button.dataset.wunHeader = 'true';
                button.className = header.closest('.skinHeader') ? 'headerButton headerButtonRight paper-icon-button-light' : anchor.className;
                const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
                icon.setAttribute('viewBox', '0 0 24 24');
                icon.setAttribute('width', '24');
                icon.setAttribute('height', '24');
                icon.setAttribute('fill', 'currentColor');
                icon.setAttribute('aria-hidden', 'true');
                icon.setAttribute('focusable', 'false');
                icon.dataset.wunIcon = 'desk-bell';
                const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                path.setAttribute('d', 'M2 17h20v2H2v-2zm11-9.83V7h1V5h-4v2h1v.17C6.63 7.66 3 11.43 3 16h18c0-4.57-3.63-8.34-8-8.83z');
                icon.append(path);
                button.append(icon);
                button.addEventListener('click', () => {
                    if (state.ready) {prepareSurface();location.hash = '/userpluginsettings.html?pageUrl=' + encodeURIComponent(page);}
                });
                header.insertBefore(button, anchor || header.firstChild);
            }
            button.disabled = !state.ready;
            button.title = label + (state.ready ? '' : portuguese ? ' — em desenvolvimento' : ' — in development');
            button.setAttribute('aria-label', button.title);
            button.setAttribute('aria-disabled', String(!state.ready));
        });
    }
    function schedule() {
        if (!queued) { queued = true; requestAnimationFrame(update); }
    }
    new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
    new MutationObserver(schedule).observe(document.documentElement, { attributes:true,subtree:true,attributeFilter:['style','class'] });
    new MutationObserver(schedule).observe(document.documentElement, { attributes: true, attributeFilter: ['lang'] });
    // Require the actual plugin page, not a web-server SPA fallback returning HTTP 200.
    fetch(new URL('../WUN/web/requests.html', location.href.split('#')[0]), { cache: 'no-store' })
        .then(async response => { state.ready = response.ok && (await response.text()).includes('data-wun-requests'); schedule(); })
        .catch(() => { state.ready = false; schedule(); });
    update();
})();
