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
    let tooltipTimer,tooltip,tooltipButton;
    const tooltipStyle=document.createElement('style');
    tooltipStyle.textContent='#wun-header-tooltip{position:fixed;z-index:1500;pointer-events:none;max-width:300px;box-sizing:border-box;border-radius:4px;padding:4px 8px;font:500 .6875rem/1.35 "Noto Sans",sans-serif;background:var(--jf-palette-Tooltip-bg,rgba(97,97,97,.92));color:var(--jf-palette-common-white,#fff);box-shadow:none}';
    document.head.append(tooltipStyle);
    function hideTooltip(){clearTimeout(tooltipTimer);tooltip?.remove();tooltip=null;tooltipButton?.removeAttribute('aria-describedby');tooltipButton=null;}
    function showTooltip(button,delay){
        hideTooltip();tooltipTimer=setTimeout(()=>{
            if(!button.isConnected)return;
            tooltipButton=button;tooltip=document.createElement('div');tooltip.id='wun-header-tooltip';tooltip.setAttribute('role','tooltip');tooltip.textContent=button.getAttribute('aria-label');document.body.append(tooltip);
            const rect=button.getBoundingClientRect();tooltip.style.left=Math.max(8,Math.min(innerWidth-tooltip.offsetWidth-8,rect.left+(rect.width-tooltip.offsetWidth)/2))+'px';
            tooltip.style.top=Math.min(innerHeight-tooltip.offsetHeight-8,rect.bottom+14)+'px';button.setAttribute('aria-describedby',tooltip.id);
        },delay);
    }
    document.addEventListener('keydown',event=>{if(event.key==='Escape')hideTooltip();});
    window.addEventListener('hashchange',hideTooltip);window.addEventListener('scroll',hideTooltip,true);window.addEventListener('resize',hideTooltip);
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
                    hideTooltip();
                    if (state.ready) {prepareSurface();location.hash = '/userpluginsettings.html?pageUrl=' + encodeURIComponent(page);}
                });
                header.insertBefore(button, anchor || header.firstChild);
            }
            if(!button.dataset.wunTooltipBound){
                button.dataset.wunTooltipBound='true';
                button.addEventListener('mouseenter',()=>showTooltip(button,700));button.addEventListener('mouseleave',hideTooltip);
                button.addEventListener('focus',()=>showTooltip(button,0));button.addEventListener('blur',hideTooltip);button.addEventListener('click',hideTooltip);
            }
            button.disabled = !state.ready;
            button.removeAttribute('title');
            button.setAttribute('aria-label', label + (state.ready ? '' : portuguese ? ' — em desenvolvimento' : ' — in development'));
            if(tooltipButton===button&&tooltip&&tooltip.textContent!==button.getAttribute('aria-label'))tooltip.textContent=button.getAttribute('aria-label');
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
