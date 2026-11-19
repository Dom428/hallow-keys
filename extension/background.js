// Hallow Keys extension - background. Talks to Hallow Keys on this PC through native messaging (com.hallow.keys).
// Rules: fills only after a click (popup or the key icon), only into the page the login matches (checked here and again in the app),
// only the top frame or frames with the same origin. Passwords waiting to be saved stay in memory (storage.session) until you decide.
'use strict';
const HOST = 'com.hallow.keys';

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

// fill: ask the app for this login on this tab's page, then hand it to the frames whose origin is the top page's origin
async function fill(tab, id) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin) return { ok: false, msg: 'Not a web page' };
  const r = await ask({ type: 'fill', id: id, url: url });
  if (!r.ok) return r;
  let done = 0;
  try {
    const res = await chrome.tabs.sendMessage(tab.id, { type: 'hk-fill', origin: origin, username: r.username, password: r.password, totp: r.totp });
    done = res && res.filled ? res.filled : 0;
  } catch (e) { }
  r.password = null; r.username = null;
  return { ok: done > 0, msg: done > 0 ? 'Filled' : 'No login form found on this page' };
}

// v1.3: just the 2FA code of one login, into the page's 2FA box (same checks as a fill)
async function fillTotp(tab, id) {
  const url = tab && tab.url; const origin = originOf(url);
  if (!origin) return { ok: false, msg: 'Not a web page' };
  const r = await ask({ type: 'totp', id: id, url: url });
  if (!r.ok) return r;
  let done = 0;
  try { const res = await chrome.tabs.sendMessage(tab.id, { type: 'hk-fill-totp', origin: origin, totp: r.totp }); done = res && res.filled ? res.filled : 0; } catch (e) { }
  r.totp = null;
  return { ok: done > 0, msg: done > 0 ? 'Filled the 2FA code' : 'No 2FA code box found on this page' };
}

chrome.runtime.onMessage.addListener((msg, sender, send) => {
  (async () => {
    if (msg.type === 'popup-status') { send(await ask({ type: 'status' })); return; }
    if (msg.type === 'popup-match') { send(await ask({ type: 'match', url: msg.url })); return; }
    if (msg.type === 'popup-open') { send(await ask({ type: 'open' })); return; }
    if (msg.type === 'popup-fill') { const tab = await chrome.tabs.get(msg.tabId); send(await fill(tab, msg.id)); return; }
    if (msg.type === 'popup-totp') { const tab = await chrome.tabs.get(msg.tabId); send(await fillTotp(tab, msg.id)); return; }
    if (msg.type === 'popup-pending') { const k = 'pending-' + msg.tabId; const s = await chrome.storage.session.get(k); send(s[k] ? { host: s[k].host, username: s[k].username, state: s[k].state } : null); return; }
    if (msg.type === 'popup-save') {
      const k = 'pending-' + msg.tabId; const s = await chrome.storage.session.get(k); const p = s[k];
      await chrome.storage.session.remove(k); chrome.action.setBadgeText({ tabId: msg.tabId, text: '' });
      if (!p || !msg.save) { send({ ok: true }); return; }
      send(await ask({ type: 'save', url: p.url, username: p.username, password: p.password })); return;
    }
    // from the content script in a page
    if (!sender.tab) { send(null); return; }
    const top = originOf(sender.tab.url), here = originOf(sender.url);
    if (!top || here !== top) { send(null); return; }   // cross-origin frames get nothing
    if (msg.type === 'inline-match') { send(await ask({ type: 'match', url: sender.tab.url })); return; }
    if (msg.type === 'inline-fill') { send(await fill(sender.tab, msg.id)); return; }
    if (msg.type === 'inline-totp') { send(await fillTotp(sender.tab, msg.id)); return; }
    if (msg.type === 'submitted') {
      // a login form was sent: offer to save it (the popup asks you; nothing is saved without your OK)
      const c = await ask({ type: 'save-check', url: sender.tab.url, username: msg.username, password: msg.password });
      if (c.ok && (c.state === 'new' || c.state === 'update')) {
        await chrome.storage.session.set({ ['pending-' + sender.tab.id]: { url: sender.tab.url, host: new URL(sender.tab.url).host, username: msg.username, password: msg.password, state: c.state } });
        chrome.action.setBadgeText({ tabId: sender.tab.id, text: '1' }); chrome.action.setBadgeBackgroundColor({ color: '#F07820' });
      }
      send(null); return;
    }
    send(null);
  })();
  return true;
});
chrome.tabs.onRemoved.addListener(id => chrome.storage.session.remove('pending-' + id));
chrome.storage.session.setAccessLevel && chrome.storage.session.setAccessLevel({ accessLevel: 'TRUSTED_CONTEXTS' });
