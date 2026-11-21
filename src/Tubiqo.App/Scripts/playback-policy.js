// Runs in every frame before page scripts. Applies the Playback Policy by changing what the
// page is told the browser can play: YouTube's player only picks formats it believes are
// supported, so AV1 (no hardware decode on the target GPU) and resolutions above the Quality
// Cap are never chosen, and adaptive quality still works below the cap.
(() => {
  'use strict';
  const policy = /*TUBIQO_POLICY*/null;
  if (!policy) return;

  const num = (re, s) => { const m = re.exec(s); return m ? Number(m[1]) : NaN; };

  // True when a MIME type string (as YouTube probes it, e.g.
  // 'video/webm; codecs="vp09.00.51.08"; width=3840; height=2160; framerate=60') breaks the policy.
  const refusedType = (type) => {
    const t = String(type == null ? '' : type).toLowerCase();
    if (!policy.allowAv1 && /\bav01\b|\bav1\b/.test(t)) return true;
    return tooBig(num(/(?:^|;)\s*width\s*=\s*"?(\d+)/, t), num(/(?:^|;)\s*height\s*=\s*"?(\d+)/, t));
  };

  // Compare the short side, so a 1080x1920 portrait Short counts as 1080p.
  const tooBig = (w, h) => {
    if (Number.isFinite(w) && Number.isFinite(h)) return Math.min(w, h) > policy.maxShortSide;
    return false; // one dimension alone can't tell orientation; leave it to the other checks
  };

  for (const MS of [window.MediaSource, window.ManagedMediaSource, window.WebKitMediaSource]) {
    if (!MS || typeof MS.isTypeSupported !== 'function') continue;
    const original = MS.isTypeSupported.bind(MS);
    MS.isTypeSupported = (type) => !refusedType(type) && original(type);
  }

  if (window.HTMLMediaElement) {
    const original = HTMLMediaElement.prototype.canPlayType;
    HTMLMediaElement.prototype.canPlayType = function (type) {
      return refusedType(type) ? '' : original.call(this, type);
    };
  }

  const mc = navigator.mediaCapabilities;
  if (mc && typeof mc.decodingInfo === 'function') {
    const original = mc.decodingInfo.bind(mc);
    mc.decodingInfo = (config) => {
      const v = config && config.video;
      if (v && (refusedType(v.contentType) || tooBig(Number(v.width), Number(v.height)))) {
        return Promise.resolve({ supported: false, smooth: false, powerEfficient: false });
      }
      return original(config);
    };
  }
})();
