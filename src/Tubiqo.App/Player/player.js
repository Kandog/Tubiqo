// Lean Player: shows YouTube's embed player alone and reports its state to Tubiqo.
// Talks to the embed iframe with the IFrame API's postMessage protocol directly, so no
// YouTube script ever runs in this page's origin (which can message the Tubiqo host).
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
  let lastSent = 0, lastSentState = null;
  // At most one progress update a second, but a change of state (playing, paused, ended...) is always sent at once:
  // a paused video sends nothing more, so a dropped "paused" would never be corrected.
  const send = (force) => {
    const now = Date.now();
    if (force || state.state !== lastSentState || now - lastSent >= 1000) {
      lastSent = now; lastSentState = state.state; toHost({ ...state });
    }
  };

  // The embed player only reports state changes and errors to pages that subscribe to them.
  let subscribed = false;
  const subscribe = () => {
    if (subscribed) return;
    subscribed = true;
    for (const name of ['onStateChange', 'onError']) toPlayer({ event: 'command', func: 'addEventListener', args: [name] });
  };

  window.addEventListener('message', (e) => {
    if (e.origin !== EMBED_ORIGIN || e.source !== frame.contentWindow) return;
    let data;
    try { data = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; } catch { return; }
    if (!data || typeof data.event !== 'string') return;
    connected = true;
    subscribe();

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
      if (typeof info.volume === 'number') state.volume = info.volume;
      if (typeof info.muted === 'boolean') state.muted = info.muted;
    }
    if (data.event === 'onReady' || data.event === 'initialDelivery') { subscribe(); applyPending(); }
    send(data.event === 'onReady' || data.event === 'initialDelivery');
  });

  // Commands from Tubiqo (the host app only): restore the saved volume, pause when minimized.
  let pending = [];
  const command = (func, args = []) => toPlayer({ event: 'command', func, args });
  const run = (msg) => {
    if (msg.type === 'setVolume' && typeof msg.volume === 'number') {
      command('setVolume', [Math.max(0, Math.min(100, Math.round(msg.volume)))]);
      command(msg.muted === true ? 'mute' : 'unMute');
    } else if (msg.type === 'pause') {
      command('pauseVideo');
    }
  };
  const applyPending = () => { const queued = pending; pending = []; queued.forEach(run); };
  if (window.chrome && chrome.webview) {
    chrome.webview.addEventListener('message', (e) => {
      const msg = e.data;
      if (!msg || typeof msg !== 'object' || typeof msg.type !== 'string') return;
      if (connected) run(msg); else pending.push(msg);
    });
  }
})();
