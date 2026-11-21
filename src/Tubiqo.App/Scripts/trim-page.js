// Runs on YouTube pages (top-level only). Applies the enabled Trim Rules: the Rule Pack's
// hide selectors as one stylesheet, plus the few behaviours that need code, which live
// here in the app (never in the Rule Pack): hover previews, autoplay-next and the
// "Show comments" button.
(() => {
  'use strict';
  const config = /*TUBIQO_TRIM*/null;
  if (!config || window.top !== window) return;
  if (!/(^|\.)youtube\.com$/.test(location.hostname)) return;
  const on = new Set(config.enabled);

  // --- Hide: constructed stylesheet (not a <style> tag), so the page's CSP can't refuse it.
  const css = config.hideCss + (on.has('comments')
    ? '.tubiqo-comments-button{display:block;margin:16px 0;padding:8px 16px;border-radius:18px;border:1px solid #888;' +
      'background:transparent;color:inherit;font:500 14px Roboto,Arial,sans-serif;cursor:pointer}' +
      'html.tubiqo-show-comments .tubiqo-comments-button{display:none}'
    : '');
  if (css) {
    const sheet = new CSSStyleSheet();
    sheet.replaceSync(css);
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
  }

  // --- No hover previews: thumbnails never hear the pointer arriving, and preview players can't start.
  if (on.has('hover-previews')) {
    const thumbs = 'ytd-thumbnail, ytd-rich-grid-media, ytd-video-renderer, ytd-rich-item-renderer, ' +
      'ytd-compact-video-renderer, yt-lockup-view-model, ytd-playlist-video-renderer, ytd-grid-video-renderer';
    for (const type of ['mouseover', 'mouseenter', 'pointerover', 'pointerenter', 'mousemove']) {
      window.addEventListener(type, (e) => {
        const t = e.target;
        if (t instanceof Element && t.closest(thumbs)) e.stopImmediatePropagation();
      }, true);
    }
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      if (this.closest && this.closest('ytd-video-preview, #video-preview, #inline-preview-player')) {
        this.pause();
        return Promise.resolve();
      }
      return play.call(this);
    };
  }

  // --- No autoplay-next (Watch Page Fallback): turn the player's autoplay switch off, cancel countdowns.
  if (on.has('autoplay-next')) {
    setInterval(() => {
      if (location.pathname !== '/watch') return;
      const toggle = document.querySelector('.ytp-autonav-toggle-button[aria-checked="true"]');
      if (toggle) toggle.click();
      const cancel = document.querySelector('.ytp-autonav-endscreen-upnext-cancel-button');
      if (cancel && cancel.offsetParent) cancel.click();
    }, 1500);
  }

  // --- "Show comments" button above the hidden comments.
  if (on.has('comments')) {
    const addButton = () => {
      const comments = document.querySelector('ytd-comments#comments');
      if (!comments || comments.previousElementSibling?.classList.contains('tubiqo-comments-button')) return;
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'tubiqo-comments-button';
      b.textContent = 'Show comments';
      b.addEventListener('click', () => document.documentElement.classList.add('tubiqo-show-comments'));
      comments.before(b);
    };
    new MutationObserver(addButton).observe(document, { childList: true, subtree: true });
  }
})();
