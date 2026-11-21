// Runs on YouTube pages. YouTube opens videos with in-page (SPA) navigation, which h2o
// can't see in time; this turns a plain left-click on a video link into a real navigation,
// so h2o's navigation handler can send it to the Lean Player instead.
(() => {
  'use strict';
  if (window.top !== window) return;
  const isYouTube = (host) => /(^|\.)youtube\.com$/.test(host);
  if (!isYouTube(location.hostname)) return;

  const isVideoLink = (u) =>
    (isYouTube(u.hostname) && (u.pathname === '/watch' || /^\/(shorts|live)\/[^/]+\/?$/.test(u.pathname))) ||
    u.hostname === 'youtu.be';

  document.addEventListener('click', (e) => {
    if (e.button !== 0 || e.ctrlKey || e.shiftKey || e.altKey || e.metaKey) return;
    const a = e.composedPath().find((n) => n instanceof HTMLAnchorElement && n.href);
    if (!a) return;
    let u;
    try { u = new URL(a.href, location.href); } catch { return; }
    if (!isVideoLink(u) || u.searchParams.get('h2o') === 'fallback') return;
    e.preventDefault();
    e.stopImmediatePropagation();
    location.assign(u.href);
  }, true);
})();
