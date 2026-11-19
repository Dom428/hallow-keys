// Hallow Keys popup: the logins for this site (titles and usernames only); a click fills that one. Also asks about a login to save.
'use strict';
const $ = id => document.getElementById(id);
function text(el, t) { el.textContent = t; return el; }
function send(m) { return new Promise(r => chrome.runtime.sendMessage(m, r)); }
(async () => {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  let url = tab && tab.url; let host = '';
  try { const u = new URL(url); if (u.protocol !== 'https:' && u.protocol !== 'http:') url = null; host = u.host; } catch (e) { url = null; }
  text($('host'), url ? host : 'Not a web page');
  // a login waiting to be saved
  const p = await send({ type: 'popup-pending', tabId: tab.id });
  if (p) {
    const box = document.createElement('div'); box.className = 'save';
    box.appendChild(text(document.createElement('div'), (p.state === 'update' ? 'Update the password for ' : 'Save a login for ') + p.host + '?'));
    box.appendChild(text(document.createElement('small'), p.username || '(no username)'));
    const yes = text(document.createElement('button'), p.state === 'update' ? 'Update' : 'Save'); yes.className = 'btn go';
    const no = text(document.createElement('button'), 'Not now'); no.className = 'btn';
    yes.onclick = async () => { const r = await send({ type: 'popup-save', tabId: tab.id, save: true }); box.replaceChildren(text(document.createElement('div'), r && r.ok ? 'Saved in Hallow Keys.' : (r && r.msg) || 'Couldn\'t save - is Hallow Keys unlocked?')); };
    no.onclick = async () => { await send({ type: 'popup-save', tabId: tab.id, save: false }); box.remove(); };
    box.appendChild(document.createElement('br')); box.appendChild(yes); box.appendChild(no); $('save').appendChild(box);
  }
  if (!url) return;
  const r = await send({ type: 'popup-match', url: url });
  const list = $('list');
  if (!r || r.running === false) { list.appendChild(text(document.createElement('div'), (r && r.msg) || 'Hallow Keys isn\'t running.')).className = 'msg'; const b = text(document.createElement('button'), 'Open Hallow Keys'); b.className = 'btn go'; b.style.margin = '0 14px 12px'; b.onclick = () => send({ type: 'popup-open' }); list.appendChild(b); return; }
  if (r.locked) { list.appendChild(text(document.createElement('div'), 'Hallow Keys is locked.')).className = 'msg'; const b = text(document.createElement('button'), 'Unlock it'); b.className = 'btn go'; b.style.margin = '0 14px 12px'; b.onclick = () => { send({ type: 'popup-open' }); window.close(); }; list.appendChild(b); return; }
  if (!r.ok) { list.appendChild(text(document.createElement('div'), r.msg || 'Something went wrong.')).className = 'msg'; return; }
  if (!r.items.length) { list.appendChild(text(document.createElement('div'), 'No logins for this site.')).className = 'msg'; return; }
  const say = t => { const m = document.createElement('div'); m.className = 'msg'; m.textContent = t; list.prepend(m); };
  for (const it of r.items) {
    const row = document.createElement('div'); row.className = 'row';
    const b = document.createElement('button'); b.className = 'item';
    b.appendChild(text(document.createElement('b'), it.title)); b.appendChild(text(document.createElement('small'), it.username || ''));
    b.onclick = async () => { const f = await send({ type: 'popup-fill', tabId: tab.id, id: it.id }); if (f && f.ok) window.close(); else say((f && f.msg) || 'Couldn\'t fill'); };
    row.appendChild(b);
    if (it.totp) {   // v1.3: fill only the 2FA code (for the page that asks for it after you sign in)
      const c = text(document.createElement('button'), '2FA'); c.className = 'btn code'; c.title = 'Fill the 2FA code';
      c.onclick = async () => { const f = await send({ type: 'popup-totp', tabId: tab.id, id: it.id }); if (f && f.ok) window.close(); else say((f && f.msg) || 'Couldn\'t fill the code'); };
      row.appendChild(c);
    }
    list.appendChild(row);
  }
})();