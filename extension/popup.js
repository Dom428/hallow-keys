// Hallow Keys popup: the logins for this site (titles and usernames only); a click fills that one. Also asks about a login to save.
// v1.5: cards and addresses (https pages), the login picked on a username page first, and two switches (suggestions under fields,
// fill a card's security code with the card).
'use strict';
const $ = id => document.getElementById(id);
function send(m) { return new Promise(r => chrome.runtime.sendMessage(m, r)); }
function el(tag, cls, t) { const e = document.createElement(tag); if (cls) e.className = cls; if (t !== undefined) e.textContent = t; return e; }
(async () => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  let url = tab && tab.url; let host = ''; let https = false;
  try { const u = new URL(url); if (u.protocol !== 'https:' && u.protocol !== 'http:') url = null; host = u.host; https = u.protocol === 'https:'; } catch (e) { url = null; }
  $('host').textContent = url ? host : 'Not a web page';
  settings();
  // a login waiting to be saved
  const p = await send({ type: 'popup-pending', tabId: tab.id });
  if (p) {
    const box = el('div', 'save');
    box.appendChild(el('div', null, (p.state === 'update' ? 'Update the password for ' : p.signup ? 'Save your new login for ' : 'Save a login for ') + p.host + '?'));
    box.appendChild(el('small', null, p.username || '(no username)'));
    const yes = el('button', 'btn go', p.state === 'update' ? 'Update' : 'Save');
    const no = el('button', 'btn', 'Not now');
    yes.onclick = async () => { const r = await send({ type: 'popup-save', tabId: tab.id, save: true }); box.replaceChildren(el('div', null, r && r.ok ? 'Saved in Hallow Keys.' : (r && r.msg) || 'Couldn\'t save - is Hallow Keys unlocked?')); };
    no.onclick = async () => { await send({ type: 'popup-save', tabId: tab.id, save: false }); box.remove(); };
    box.appendChild(document.createElement('br')); box.appendChild(yes); box.appendChild(no); $('save').appendChild(box);
  }
  if (!url) return;
  const r = await send({ type: 'popup-match', tabId: tab.id, url: url });
  const list = $('list');
  if (!r || r.running === false) { list.appendChild(el('div', 'msg', (r && r.msg) || 'Hallow Keys isn\'t running.')); const b = el('button', 'btn go', 'Open Hallow Keys'); b.style.margin = '0 14px 12px'; b.onclick = () => send({ type: 'popup-open' }); list.appendChild(b); return; }
  if (r.locked) { list.appendChild(el('div', 'msg', 'Hallow Keys is locked.')); const b = el('button', 'btn go', 'Unlock it'); b.style.margin = '0 14px 12px'; b.onclick = () => { send({ type: 'popup-open' }); window.close(); }; list.appendChild(b); return; }
  if (!r.ok) { list.appendChild(el('div', 'msg', r.msg || 'Something went wrong.')); return; }
  const say = t => { const m = el('div', 'msg', t); list.prepend(m); };
  const items = r.remembered ? r.items.slice().sort((a, b) => (b.id === r.remembered) - (a.id === r.remembered)) : r.items;
  if (!items.length) list.appendChild(el('div', 'msg', 'No logins for this site.'));
  for (const it of items) {
    const row = el('div', 'row');
    const b = el('button', 'item');
    b.appendChild(el('b', null, it.title)); b.appendChild(el('small', null, (it.id === r.remembered ? 'Continue as ' : '') + (it.username || '')));
    b.onclick = async () => { const f = await send({ type: 'popup-fill', tabId: tab.id, id: it.id }); if (f && f.ok) window.close(); else say((f && f.msg) || 'Couldn\'t fill'); };
    row.appendChild(b);
    if (it.totp) {   // v1.3: fill only the 2FA code (for the page that asks for it after you sign in)
      const c = el('button', 'btn code', '2FA'); c.title = 'Fill the 2FA code';
      c.onclick = async () => { const f = await send({ type: 'popup-totp', tabId: tab.id, id: it.id }); if (f && f.ok) window.close(); else say((f && f.msg) || 'Couldn\'t fill the code'); };
      row.appendChild(c);
    }
    list.appendChild(row);
  }
  // v1.5: cards and addresses - any https page
  if (!https) return;
  for (const kind of ['card', 'identity']) {
    const k = await send({ type: 'popup-match', tabId: tab.id, url: url, kind: kind });
    if (!k || !k.ok || !k.items.length) continue;
    list.appendChild(el('div', 'sec', kind === 'card' ? 'Cards' : 'Addresses'));
    for (const it of k.items) {
      const row = el('div', 'row'); const b = el('button', 'item');
      b.appendChild(el('b', null, it.title)); b.appendChild(el('small', null, it.sub || ''));
      b.onclick = async () => {
        const f = await send({ type: kind === 'card' ? 'popup-card' : 'popup-identity', tabId: tab.id, id: it.id });
        if (f && f.ok && f.needCvc) {   // the security code: a second click
          const c = el('button', 'btn go code', 'Fill security code');
          c.onclick = async () => { const g = await send({ type: 'popup-card', tabId: tab.id, id: it.id, cvc: true }); if (g && g.ok) window.close(); else say((g && g.msg) || 'Couldn\'t fill the security code'); };
          row.appendChild(c); say('Card filled - click Fill security code to add the code too.'); return;
        }
        if (f && f.ok) window.close(); else say((f && f.msg) || 'Couldn\'t fill');
      };
      row.appendChild(b); list.appendChild(row);
    }
  }
})();
// v1.5 settings: kept in this browser (chrome.storage.local)
function settings() {
  chrome.storage.local.get({ inline: true, cvcWithCard: false }, s => {
    const box = $('opts');
    const sw = (label, key, val) => { const l = el('label', 'opt'); const c = el('input'); c.type = 'checkbox'; c.checked = val; c.onchange = () => chrome.storage.local.set({ [key]: c.checked }); l.appendChild(c); l.appendChild(el('span', null, label)); box.appendChild(l); };
    sw('Show suggestions under fields', 'inline', s.inline !== false);
    sw('Fill a card\'s security code with the card', 'cvcWithCard', !!s.cvcWithCard);
  });
}
