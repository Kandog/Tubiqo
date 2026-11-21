// Lean Player: shows YouTube's embed player alone and reports its state to h2o.
// Talks to the embed iframe with the IFrame API's postMessage protocol directly, so no
// YouTube script ever runs in this page's origin (which can message the h2o host).
(() => {
  'use strict';
  const EMBED_ORIGIN = 'https://www.youtube-nocookie.com';
  const query = new URLSearchParams(location.search);
  const videoId = query.get('v') || '';
  const start = Math.max(0, parseInt(query.get('t') || '0', 10) || 0);

  const toHost = (msg) => { if (window.chrome && chrome.webview) chrome.webview.postMessage(msg); };

  if (!/^[A-Za-z0-9_-]{11}$/.test(videoId)) {
    const p = document.getElementById('message');
    p.textContent = 'This link does not point to a YouTube video.';
    p.hidden = false;
    return;
  }

  const params = new URLSearchParams({
    autoplay: '1', playsinline: '1', rel: '0', enablejsapi: '1',
    origin: location.origin, widget_referrer: location.origin,
  });
  if (start > 0) params.set('start', String(start));

  const frame = document.createElement('iframe');
  frame.src = `${EMBED_ORIGIN}/embed/${videoId}?${params}`;
  frame.allow = 'autoplay; encrypted-media; fullscreen';
  frame.allowFullscreen = true;
  frame.referrerPolicy = 'strict-origin-when-cross-origin';
  document.body.appendChild(frame);

  const toPlayer = (msg) =>
    frame.contentWindow && frame.contentWindow.postMessage(JSON.stringify({ ...msg, id: 1, channel: 'widget' }), EMBED_ORIGIN);

  // Handshake: keep saying "listening" until the player answers.
  let connected = false;
  const handshake = setInterval(() => connected ? clearInterval(handshake) : toPlayer({ event: 'listening' }), 250);
  frame.addEventListener('load', () => toPlayer({ event: 'listening' }));

  const state = { type: 'state', videoId, title: '', author: '', time: 0, duration: 0, state: -1 };
  let lastSent = 0;
  const send = (force) => {
    const now = Date.now();
    if (force || now - lastSent >= 1000) { lastSent = now; toHost({ ...state }); }
  };

  window.addEventListener('message', (e) => {
    if (e.origin !== EMBED_ORIGIN || e.source !== frame.contentWindow) return;
    let data;
    try { data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch { return; }
    if (!data || typeof data.event !== 'string') return;
    connected = true;

    if (data.event === 'onError') {
      toHost({ type: 'error', videoId, code: Number(data.info) });
      return;
    }
    if (data.event === 'onStateChange' && typeof data.info === 'number') {
      state.state = data.info;
      send(true);
      return;
    }
    const info = data.info;
    if (info && typeof info === 'object') {
      if (typeof info.currentTime === 'number') state.time = info.currentTime;
      if (typeof info.duration === 'number') state.duration = info.duration;
      if (typeof info.playerState === 'number') state.state = info.playerState;
      if (info.videoData && typeof info.videoData === 'object') {
        if (typeof info.videoData.title === 'string') state.title = info.videoData.title;
        if (typeof info.videoData.author === 'string') state.author = info.videoData.author;
      }
    }
    send(data.event === 'onReady' || data.event === 'initialDelivery');
  });
})();
