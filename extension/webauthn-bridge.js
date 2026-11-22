// Hallow Keys extension 1.6 - passkeys, the extension's side in the page (isolated world: the page can't reach into it).
// webauthn-page.js passes a site's passkey request here; this shows Hallow Keys' prompt in a closed shadow root and only after a real
// click there asks the extension (and through it the app) to save or use a passkey. The app checks the page's address and the site
// name (rpId) again and signs; the private key never leaves the app. Only https pages, only the top page or frames with its origin.
// Passkey autofill (mediation "conditional"): the request waits here, and content.js offers the passkeys in its suggestions under
// the username box (window.__hallowKeysPasskeys is shared with content.js, never with the page).
// v1.7 (Keys 2.12): a site that asks for a security key or a phone gets "Use another device" first; that row says how to hand the
// sign-in to your phone (Windows' window: "iPhone, iPad or Android device", scan the QR code).
(function () {
  'use strict';
  if (window.__hallowKeysBridge) return; window.__hallowKeysBridge = true;
  const api = (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.id) ? chrome : null;
  if (!api || location.protocol !== 'https:') return;
  function sameTop() { try { return window.top === window || window.top.location.origin === location.origin; } catch (e) { return false; } }
  function send(msg) { return new Promise(res => { try { api.runtime.sendMessage(msg, r => { void (api.runtime.lastError); res(r || null); }); } catch (e) { res(null); } }); }
  function reply(rid, data) { window.postMessage(Object.assign({ __hk: 'pk-res', rid: rid }, data), location.origin); }
  const PK = window.__hallowKeysPasskeys = { cond: null };
  let cur = null;   // the open prompt: { rid, close }

  window.addEventListener('message', e => {
    if (e.source !== window || !e.data || e.data.__hk !== 'pk-req' || typeof e.data.rid !== 'string') return;
    const d = e.data;
    if (d.op === 'abort') { if (cur) cur.close(); if (PK.cond) { PK.cond = null; } return; }
    reply(d.rid, { ack: true });
    if (!sameTop()) { reply(d.rid, { action: 'fallback' }); return; }
    if (d.op === 'status') { send({ type: 'pk-status' }).then(r => reply(d.rid, { action: 'status', running: !!(r && r.running) })); return; }
    const q = d.req || {};
    if (d.op === 'create') return create(d.rid, q);
    if (d.op === 'get') return q.conditional ? conditional(d.rid, q) : get(d.rid, q);
    reply(d.rid, { action: 'error', name: 'NotSupportedError', msg: 'Unknown request' });
  });

  // ---- the prompt: a card at the top right, in a closed shadow root. Rows carry data-hk names (the tests click them by name)
  const css = ':host{all:initial}.card{position:fixed;top:14px;right:14px;width:340px;background:#211b2a;border:1px solid #3a3248;border-radius:12px;padding:12px;box-shadow:0 10px 34px rgba(0,0,0,.5);' +
    'font:13px "Segoe UI",system-ui,sans-serif;color:#ece8f4;z-index:2147483647}.top{display:flex;align-items:center;gap:8px;margin-bottom:8px}.logo{width:22px;height:22px;border-radius:6px;background:#f07820;color:#16121c;' +
    'display:flex;align-items:center;justify-content:center;font-weight:700}.ttl{font-weight:600;font-size:14px}.sub{color:#8f88a0;font-size:12px;margin:0 0 8px}.row{display:flex;gap:9px;align-items:center;padding:8px 9px;border-radius:8px;' +
    'cursor:pointer;margin:2px 0}.row:hover{background:#3a2f4a}.row.main{background:#f07820;color:#16121c;font-weight:600}.row.main:hover{background:#f59a50}.row b{display:block;font-weight:600}.row small{display:block;color:#8f88a0;font-size:11px}' +
    '.row.main small{color:#3a2a1a}.k{width:18px;text-align:center}.msg{padding:6px 9px;color:#f0be5a}.sep{border-top:1px solid #3a3248;margin:6px 0}';
  function prompt(rid, title, sub, rows, onClose) {
    if (cur) cur.close(true);
    const host = document.createElement('hallow-keys-passkey'); const root = host.attachShadow({ mode: 'closed' });
    host.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;opacity:1;visibility:visible;display:block;filter:none;transform:none;clip-path:none;mix-blend-mode:normal;';
    const st = document.createElement('style'); st.textContent = css; root.appendChild(st);
    const card = document.createElement('div'); card.className = 'card'; card.setAttribute('role', 'dialog'); card.setAttribute('data-hk', 'prompt'); root.appendChild(card);
    const me = { rid: rid, host: host, card: card, closed: false, timers: [] };
    me.close = quiet => { if (me.closed) return; me.closed = true; me.timers.forEach(clearInterval); host.remove(); if (cur === me) cur = null; if (!quiet && onClose) onClose(); };
    me.render = (t, s, rs) => {
      me.shownAt = Date.now(); card.replaceChildren();
      const top = document.createElement('div'); top.className = 'top';
      const logo = document.createElement('div'); logo.className = 'logo'; logo.textContent = 'H'; top.appendChild(logo);
      const tt = document.createElement('div'); tt.className = 'ttl'; tt.textContent = t; top.appendChild(tt); card.appendChild(top);
      if (s) { const p = document.createElement('div'); p.className = 'sub'; p.textContent = s; card.appendChild(p); }
      for (const r of rs) {
        if (r.sep) { const x = document.createElement('div'); x.className = 'sep'; card.appendChild(x); continue; }
        if (r.msg) { const x = document.createElement('div'); x.className = 'msg'; x.setAttribute('data-hk', r.hk || 'msg'); x.textContent = r.msg; card.appendChild(x); continue; }
        const d = document.createElement('div'); d.className = 'row' + (r.main ? ' main' : ''); d.setAttribute('data-hk', r.hk); d.setAttribute('role', 'button'); d.tabIndex = 0;
        const k = document.createElement('span'); k.className = 'k'; k.textContent = r.icon || '\u{1F511}'; d.appendChild(k);
        const tx = document.createElement('div'); const b = document.createElement('b'); b.textContent = r.title; tx.appendChild(b);
        if (r.sub) { const sm = document.createElement('small'); sm.textContent = r.sub; tx.appendChild(sm); } d.appendChild(tx);
        const go = ev => { if (!ev.isTrusted || me.closed || me.busy || !fair(me, ev)) return; ev.preventDefault(); ev.stopPropagation(); r.action(); };   // a real click or key, never a script's
        d.addEventListener('click', go); d.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') go(ev); });
        card.appendChild(d);
      }
    };
    me.render(title, sub, rows);
    (document.body || document.documentElement).appendChild(host);
    cur = me; return me;
  }
  // against click-jacking: the page can style our host element, so a click counts only if the prompt has been up a moment, is fully
  // visible (no opacity, filter, clip or transform from the page) and is what's really under the pointer
  function fair(me, ev) {
    if (Date.now() - me.shownAt < 350) return false;
    const s = getComputedStyle(me.host);
    if (s.display === 'none' || s.visibility !== 'visible' || s.opacity !== '1' || s.filter !== 'none' || s.clipPath !== 'none' || s.transform !== 'none' || s.mixBlendMode !== 'normal') return false;
    if (ev.type === 'click' && document.elementFromPoint(ev.clientX, ev.clientY) !== me.host) return false;
    return true;
  }
  const fallbackRow = (p, rid, main) => ({ hk: 'fallback', main: !!main, icon: '\u{1F4F1}', title: 'Use another device or the browser\'s own', sub: 'Your phone (scan the QR code Windows shows), a security key or Windows Hello', action: () => { p.close(true); reply(rid, { action: 'fallback' }); } });
  // v1.7: the site asked for a security key or a phone rather than a passkey on this PC
  const wantsOther = q => q.attachment === 'cross-platform' || (Array.isArray(q.hints) && (q.hints[0] === 'security-key' || q.hints[0] === 'hybrid'));
  const cancelRow = (p, rid) => ({ hk: 'cancel', icon: '✕', title: 'Cancel', action: () => { p.close(true); reply(rid, { action: 'error', name: 'NotAllowedError', msg: 'The operation either timed out or was not allowed.' }); } });
  const notAllowed = (rid, r) => reply(rid, { action: 'error', name: (r && r.error) || 'NotAllowedError', msg: (r && r.msg) || 'The operation either timed out or was not allowed.' });

  // ---- locked: say so, offer to open Hallow Keys, and look again every 1.5 s (for 3 minutes) until it's unlocked
  function whenLocked(p, rid, redo, extraRows) {
    p.render('Hallow Keys is locked', 'Unlock it to use your passkeys.', [
      { hk: 'unlock', main: true, icon: '\u{1F513}', title: 'Unlock Hallow Keys', sub: 'Opens Hallow Keys on this PC', action: () => send({ type: 'popup-open' }) },
      { msg: 'Waiting for Hallow Keys to be unlocked...', hk: 'waiting' }, { sep: true }, fallbackRow(p, rid), cancelRow(p, rid)]);
    let n = 0; const t = setInterval(async () => { if (p.closed) return clearInterval(t); if (++n > 120) { clearInterval(t); return; } const r = await redo(); if (r && r.error) { clearInterval(t); p.close(true); notAllowed(rid, r); return; } if (r && r.ok) { clearInterval(t); if (!p.closed) extraRows(r); } }, 1500);   // not running (yet) or still locked: keep waiting
    p.timers.push(t);
  }

  async function create(rid, q) {
    if (q.algs && q.algs.length && !q.algs.includes('-7')) { reply(rid, { action: 'fallback' }); return; }   // Keys makes ES256 keys only
    const check = () => send({ type: 'pk-list', rpId: q.rpId, allow: [] });
    const r = await check();
    if (!r || r.running === false) { reply(rid, { action: 'fallback' }); return; }   // Keys isn't running or connected: the browser's own
    if (r.error) { notAllowed(rid, r); return; }   // wrong rpId, http... (checked in the app)
    const who = q.userName || q.displayName || 'this account';
    const p = prompt(rid, 'Save a passkey?', '', [], () => notAllowed(rid));
    const other = wantsOther(q);
    const ready = info => p.render('Save a passkey in Hallow Keys?', 'For ' + who + ' on ' + (info.rpId || q.rpId) + '. It stays in your vault and syncs with it.', (other ? [{ msg: 'This site asked for a security key or your phone.', hk: 'asked-other' }, fallbackRow(p, rid, true), { sep: true }] : []).concat([
      { hk: 'save', main: !other, icon: '\u{1F511}', title: 'Save passkey in Hallow Keys', sub: who, action: async () => {
        p.busy = true; p.render('Saving the passkey...', 'Hallow Keys is making a new key for ' + (info.rpId || q.rpId) + '.', []);
        const c = await send(Object.assign({ type: 'pk-create' }, q)); p.close(true);
        if (c && c.ok) reply(rid, { action: 'cred', cred: c }); else notAllowed(rid, c);
      } }, { sep: true }].concat(other ? [] : [fallbackRow(p, rid)]).concat([cancelRow(p, rid)])));
    if (r.locked) whenLocked(p, rid, check, ready); else ready(r);
  }
  function getRows(p, rid, q, r) {
    const items = r.items || []; const rows = [];
    const other = wantsOther(q);
    if (other) rows.push({ msg: 'This site asked for a security key or your phone.', hk: 'asked-other' }, fallbackRow(p, rid, true), { sep: true });
    items.forEach((it, i) => rows.push({ hk: 'pick-' + i, main: i === 0 && !other, icon: '\u{1F511}', title: it.userName || it.displayName || it.title, sub: it.title + (it.displayName && it.displayName !== it.userName ? ' - ' + it.displayName : ''), action: () => use(p, rid, q, it.id) }));
    if (!items.length) rows.push({ msg: 'No passkeys for ' + (r.rpId || q.rpId) + ' in Hallow Keys.', hk: 'none' });
    rows.push({ sep: true }); if (!other) rows.push(fallbackRow(p, rid)); rows.push(cancelRow(p, rid));
    p.render('Sign in with a passkey', 'Hallow Keys - ' + (r.rpId || q.rpId), rows);
  }
  async function use(p, rid, q, id) {
    p.busy = true; p.render('Signing in...', 'Hallow Keys is signing for ' + q.rpId + '.', []);
    const c = await send({ type: 'pk-get', rpId: q.rpId, id: id, challenge: q.challenge, allow: q.allow || [] }); p.close(true);
    if (c && c.ok) reply(rid, { action: 'cred', cred: c }); else notAllowed(rid, c);
  }
  async function get(rid, q) {
    const list = () => send({ type: 'pk-list', rpId: q.rpId, allow: q.allow || [] });
    const r = await list();
    if (!r || r.running === false) { reply(rid, { action: 'fallback' }); return; }
    if (r.error) { notAllowed(rid, r); return; }
    const p = prompt(rid, 'Sign in with a passkey', '', [], () => notAllowed(rid));
    if (r.locked) whenLocked(p, rid, list, x => getRows(p, rid, q, x)); else getRows(p, rid, q, r);
  }
  // passkey autofill: wait for a pick in the suggestions (content.js asks PK.list / PK.pick); a new request or an abort replaces it
  async function conditional(rid, q) {
    const r = await send({ type: 'pk-list', rpId: q.rpId, allow: q.allow || [] });
    if (!r || r.running === false) { reply(rid, { action: 'fallback' }); return; }
    if (r.error) { notAllowed(rid, r); return; }
    if (PK.cond) notAllowed(PK.cond.rid, { error: 'AbortError', msg: 'A new request replaced this one' });
    PK.cond = { rid: rid, q: q };
  }
  PK.active = () => !!PK.cond;
  PK.list = async () => { if (!PK.cond) return null; const q = PK.cond.q; return await send({ type: 'pk-list', rpId: q.rpId, allow: q.allow || [] }); };
  PK.pick = async id => {
    const c0 = PK.cond; if (!c0) return false; const q = c0.q;
    const c = await send({ type: 'pk-get', rpId: q.rpId, id: id, challenge: q.challenge, allow: q.allow || [] });
    if (PK.cond !== c0) return false;
    if (c && c.ok) { PK.cond = null; reply(c0.rid, { action: 'cred', cred: c }); return true; }
    return false;
  };
})();
