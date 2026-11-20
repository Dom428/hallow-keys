// Hallow Keys extension - background. Talks to Hallow Keys on this PC through native messaging (com.hallow.keys).
// Rules: fills only after you pick something (suggestions, key icon, popup), only into the page the login matches (checked here and
// again in the app), only the top frame or frames with the same origin. Cards and addresses only on https pages. Passwords waiting
// to be saved stay in memory (storage.session) until you decide.
// v1.5: cards and addresses; two-step sign-ins (the login you picked on the username page is offered first on the password page
// for 5 minutes, in that tab); the 2FA code filled on the next page after a password fill (same site, same tab, within 2 minutes,
// once); sign-up forms offered for saving.
// v1.6: passkeys - the prompt in the page (webauthn-bridge.js) asks for pk-list / pk-get / pk-create after a real click; they go to the
// app with the address of the frame that asked (never what the page says about itself), https only, top page or same-origin frames.
'use strict';
const HOST = 'com.hallow.keys';
const REMEMBER_MS = 5 * 60 * 1000, TOTP_NEXT_MS = 2 * 60 * 1000;

function ask(msg) {
  return new Promise(resolve => {
    try {
      chrome.runtime.sendNativeMessage(HOST, msg, reply => {
        if (chrome.runtime.lastError) resolve({ ok: false, running: false, msg: 'Hallow Keys isn\'t connected to this browser - open Hallow Keys > Browser and click Connect browsers.' });
        else resolve(reply || { ok: false });
      });
    } catch (e) { resolve({ ok: false, msg: String(e) }); }
  });
}
function originOf(u) { try { const x = new URL(u); return (x.protocol === 'https:' || x.protocol === 'http:') ? x.origin : null; } catch (e) { return null; } }
function isHttps(u) { try { return new URL(u).protocol === 'https:'; } catch (e) { return false; } }
// "accounts.example.co.uk" -> "example.co.uk" (a hint for the UI only - the app checks sites with its own, stricter rules)
const MULTI = new Set(['co.uk', 'org.uk', 'ac.uk', 'gov.uk', 'com.au', 'net.au', 'org.au', 'co.nz', 'co.jp', 'co.kr', 'com.br', 'com.mx', 'co.in', 'co.za', 'com.cn', 'com.tr', 'github.io']);
function siteOf(u) { try { const h = new URL(u).hostname; const p = h.split('.'); if (p.length <= 2) return h; const two = p.slice(-2).join('.'); return MULTI.has(two) && p.length >= 3 ? p.slice(-3).join('.') : two; } catch (e) { return null; } }
const store = {
  async get(k) { const s = await chrome.storage.session.get(k); return s[k]; },
  set(k, v) { return chrome.storage.session.set({ [k]: v }); },
  remove(k) { return chrome.storage.session.remove(k); }
};
function to(tabId, msg, frameId) { return Promise.resolve(chrome.tabs.sendMessage(tabId, msg, frameId === undefined ? undefined : { frameId: frameId })).catch(() => null); }

// fill: ask the app for this login on this tab's page, then hand it to the frames whose origin is the top page's origin
async function fill(tab, id, frameId) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin) return { ok: false, msg: 'Not a web page' };
  const r = await ask({ type: 'fill', id: id, url: url });
  if (!r.ok) return r;
  const res = await to(tab.id, { type: 'hk-fill', origin: origin, username: r.username, password: r.password, totp: r.totp }, frameId);
  const done = res && res.filled ? res.filled : 0;
  const hadTotp = !!r.totp; r.password = null; r.username = null; r.totp = null;
  if (done > 0) {
    await store.set('remember-' + tab.id, { id: id, site: siteOf(url), until: Date.now() + REMEMBER_MS });   // two-step sign-ins
    if (hadTotp && !(res && res.totp)) await store.set('totpnext-' + tab.id, { id: id, url: url, until: Date.now() + TOTP_NEXT_MS });
    else await store.remove('totpnext-' + tab.id);
  }
  return { ok: done > 0, msg: done > 0 ? 'Filled' : 'No login form found on this page' };
}

// v1.3: just the 2FA code of one login, into the page's 2FA box (same checks as a fill). sameSiteAs: v1.5's code after a password fill
async function fillTotp(tab, id, frameId, sameSiteAs) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin) return { ok: false, msg: 'Not a web page' };
  const q = { type: 'totp', id: id, url: url }; if (sameSiteAs) q.sameSiteAs = sameSiteAs;
  const r = await ask(q);
  if (!r.ok) return r;
  const res = await to(tab.id, { type: 'hk-fill-totp', origin: origin, totp: r.totp }, frameId);
  const done = res && res.filled ? res.filled : 0;
  r.totp = null;
  return { ok: done > 0, msg: done > 0 ? 'Filled the 2FA code' : 'No 2FA code box found on this page' };
}

// v1.5: a card (any https page - cards aren't tied to a site). The security code only on the second click (cvc) or with the setting on
async function fillCard(tab, id, frameId, cvcClick) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin || !isHttps(url)) return { ok: false, msg: 'Cards are only filled on secure (https) pages' };
  const s = await chrome.storage.local.get({ cvcWithCard: false });
  const r = await ask({ type: 'card', id: id, url: url, cvc: !!(cvcClick || s.cvcWithCard) });
  if (!r.ok) return r;
  const card = { holder: r.holder, number: r.number, expMonth: r.expMonth, expYear: r.expYear, cvc: r.cvc || null };
  const res = await to(tab.id, { type: 'hk-fill-card', origin: origin, card: card, onlyCvc: !!cvcClick }, frameId);
  card.number = null; card.cvc = null; r.number = null; r.cvc = null;
  const done = res && res.filled ? res.filled : 0;
  return { ok: done > 0, needCvc: !!(res && res.needCvc && r.hasCvc), msg: done > 0 ? (cvcClick ? 'Filled the security code' : 'Filled the card') : 'No card form found on this page' };
}
async function fillIdentity(tab, id, frameId) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin || !isHttps(url)) return { ok: false, msg: 'Addresses are only filled on secure (https) pages' };
  const r = await ask({ type: 'identity', id: id, url: url });
  if (!r.ok) return r;
  const res = await to(tab.id, { type: 'hk-fill-identity', origin: origin, identity: r }, frameId);
  const done = res && res.filled ? res.filled : 0;
  return { ok: done > 0, msg: done > 0 ? 'Filled the address' : 'No address form found on this page' };
}
// logins for a page, with the one picked on the username page first (two-step sign-ins)
async function match(tab, kind) {
  const r = await ask({ type: 'match', url: tab.url, kind: kind || 'login' });
  if (r && r.ok && (kind || 'login') === 'login') {
    const m = await store.get('remember-' + tab.id);
    if (m && m.until > Date.now() && m.site === siteOf(tab.url) && r.items.some(i => i.id === m.id)) r.remembered = m.id;
  }
  return r;
}

// v1.6: passkeys. Only strings and short lists of strings go to the app; the app checks the page's address and the rpId again.
const str = (v, max) => typeof v === 'string' && v.length <= max ? v : '';
const strs = (v, max) => Array.isArray(v) ? v.filter(x => typeof x === 'string' && x.length <= 1400).slice(0, max) : [];
async function passkey(msg, url) {
  if (!isHttps(url)) return { ok: false, error: 'SecurityError', msg: 'Passkeys only work on secure (https) pages' };
  const q = { url: url, rpId: str(msg.rpId, 253) };
  if (msg.type === 'pk-list') return await ask(Object.assign(q, { type: 'passkey-list', allow: strs(msg.allow, 64) }));
  if (msg.type === 'pk-get') return await ask(Object.assign(q, { type: 'passkey-get', id: str(msg.id, 64), challenge: str(msg.challenge, 1400), allow: strs(msg.allow, 64) }));
  return await ask(Object.assign(q, { type: 'passkey-create', rpName: str(msg.rpName, 256), challenge: str(msg.challenge, 1400), userId: str(msg.userId, 100), userName: str(msg.userName, 256),
    displayName: str(msg.displayName, 256), algs: strs(msg.algs, 20), exclude: strs(msg.exclude, 64) }));
}
async function handle(msg, sender) {
  if (!msg || typeof msg.type !== 'string' || sender.id !== chrome.runtime.id) return null;
  if (msg.type === 'popup-open') return await ask({ type: 'open' });
  if (msg.type.startsWith('popup-')) {
    if (sender.tab) return null;   // only the popup (an extension page), never a web page
    if (msg.type === 'popup-status') return await ask({ type: 'status' });
    const tab = msg.tabId !== undefined ? await chrome.tabs.get(msg.tabId) : null;
    if (!tab) return { ok: false };
    if (msg.type === 'popup-match') return await match(tab, msg.kind);
    if (msg.type === 'popup-fill') return await fill(tab, msg.id);
    if (msg.type === 'popup-totp') return await fillTotp(tab, msg.id);
    if (msg.type === 'popup-card') return await fillCard(tab, msg.id, undefined, !!msg.cvc);
    if (msg.type === 'popup-identity') return await fillIdentity(tab, msg.id);
    if (msg.type === 'popup-pending') { const p = await store.get('pending-' + tab.id); return p ? { host: p.host, username: p.username, state: p.state, signup: !!p.signup } : null; }
    if (msg.type === 'popup-save') {
      const k = 'pending-' + tab.id; const p = await store.get(k);
      await store.remove(k); chrome.action.setBadgeText({ tabId: tab.id, text: '' });
      if (!p || !msg.save) return { ok: true };
      return await ask({ type: 'save', url: p.url, username: p.username, password: p.password });
    }
    return null;
  }
  // from the content script in a page
  if (!sender.tab) return null;
  const top = originOf(sender.tab.url), here = originOf(sender.url);
  if (!top || here !== top) return null;   // cross-origin frames get nothing
  const tab = sender.tab, frame = sender.frameId;
  switch (msg.type) {
    case 'inline-match': return await match(tab, msg.kind);
    case 'inline-fill': return await fill(tab, msg.id, frame);
    case 'inline-totp': return await fillTotp(tab, msg.id, frame);
    case 'inline-card': return await fillCard(tab, msg.id, frame, !!msg.cvc);
    case 'inline-identity': return await fillIdentity(tab, msg.id, frame);
    case 'pk-status': return await ask({ type: 'status' });
    case 'pk-list': case 'pk-get': case 'pk-create': return await passkey(msg, sender.url);
    case 'otp-ready': {
      // a 2FA box appeared: right after a password fill with a 2FA key, on the same site, in this tab - fill the code once
      const k = 'totpnext-' + tab.id; const p = await store.get(k);
      if (!p || p.until < Date.now()) { if (p) await store.remove(k); return null; }
      if (siteOf(p.url) !== siteOf(tab.url)) return null;
      const r = await fillTotp(tab, p.id, frame, p.url);
      if (r.ok) await store.remove(k);
      return { ok: r.ok };
    }
    case 'username-step':
      if (typeof msg.username === 'string' && msg.username.length <= 500) await store.set('step-' + tab.id, { username: msg.username, site: siteOf(tab.url), until: Date.now() + REMEMBER_MS });
      return null;
    case 'submitted': {
      // a login or sign-up form was sent: offer to save it (the popup asks you; nothing is saved without your OK)
      let user = typeof msg.username === 'string' ? msg.username : '';
      if (!user) { const st = await store.get('step-' + tab.id); if (st && st.until > Date.now() && st.site === siteOf(tab.url)) user = st.username; }
      const c = await ask({ type: 'save-check', url: tab.url, username: user, password: msg.password });
      if (c.ok && (c.state === 'new' || c.state === 'update')) {
        await store.set('pending-' + tab.id, { url: tab.url, host: new URL(tab.url).host, username: user, password: msg.password, state: c.state, signup: !!msg.signup });
        chrome.action.setBadgeText({ tabId: tab.id, text: '1' }); chrome.action.setBadgeBackgroundColor({ color: '#F07820' });
      }
      return null;
    }
  }
  return null;
}
chrome.runtime.onMessage.addListener((msg, sender, send) => { handle(msg, sender).then(send, () => send(null)); return true; });
chrome.tabs.onRemoved.addListener(id => { for (const k of ['pending-', 'remember-', 'totpnext-', 'step-']) chrome.storage.session.remove(k + id); });
chrome.storage.session.setAccessLevel && chrome.storage.session.setAccessLevel({ accessLevel: 'TRUSTED_CONTEXTS' });
if (typeof globalThis !== 'undefined' && globalThis.__hallowKeysBgTest) globalThis.__hallowKeysBgTest({ handle, siteOf, fill, fillCard, match });
