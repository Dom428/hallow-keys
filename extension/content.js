// Hallow Keys extension - in the page. Never fills by itself: only when you click the extension's popup or the key icon in a
// password box. Only visible fields are filled, and only in the top page or frames with the same origin as it.
'use strict';
(function () {
  const HK = {};
  function sameOriginAsTop() { try { return window.top === window || window.top.location.origin === location.origin; } catch (e) { return false; } }
  HK.sameOriginAsTop = sameOriginAsTop;
  // visible = on the page, has a size, not hidden by CSS, not see-through, and not a hidden input
  function visible(el) {
    if (!el || el.disabled || el.readOnly) return false;
    if (el.type === 'hidden') return false;
    const r = el.getBoundingClientRect(); if (r.width < 4 || r.height < 4) return false;
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
      const s = getComputedStyle(e);
      if (s.display === 'none' || s.visibility === 'hidden' || s.visibility === 'collapse' || parseFloat(s.opacity) < 0.1) return false;
    }
    if (r.bottom < 0 || r.right < 0 || r.left > (document.documentElement.scrollWidth + 50) || r.top > document.documentElement.scrollHeight + 50) return false;
    return true;
  }
  HK.visible = visible;
  const userTypes = ['text', 'email', 'tel', ''];
  // the visible password boxes, and for each the visible text/email box just before it in the same form
  function fields() {
    const pw = [...document.querySelectorAll('input[type=password]')].filter(visible);
    const all = [...document.querySelectorAll('input')];
    const pairs = pw.map(p => {
      const scope = p.form ? [...p.form.querySelectorAll('input')] : all;
      const i = scope.indexOf(p); let user = null;
      for (let j = i - 1; j >= 0; j--) { const c = scope[j]; if (userTypes.includes((c.getAttribute('type') || '').toLowerCase()) && visible(c)) { user = c; break; } if (c.type === 'password') break; }
      return { user: user, pass: p };
    });
    if (pairs.length === 0) {   // a "username first" page: just the user box
      const u = all.find(c => userTypes.includes((c.getAttribute('type') || '').toLowerCase()) && visible(c) && /user|mail|login|account|ident/i.test((c.name || '') + (c.id || '') + (c.autocomplete || '')));
      if (u) pairs.push({ user: u, pass: null });
    }
    return pairs;
  }
  HK.fields = fields;
  function setValue(el, v) {
    const proto = Object.getPrototypeOf(el); const d = Object.getOwnPropertyDescriptor(proto, 'value') || Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    d.set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true }));
  }
  function fill(username, password, totp) {
    if (!sameOriginAsTop()) return 0;
    let n = 0;
    for (const f of fields()) {
      if (f.user && username) { setValue(f.user, username); n++; }
      if (f.pass && password) { setValue(f.pass, password); n++; break; }   // the first visible password box only
    }
    if (totp) { const o = [...document.querySelectorAll('input[autocomplete=one-time-code]')].find(visible); if (o) { setValue(o, totp); n++; } }
    return n;
  }
  HK.fill = fill;

  // filling: the request comes from the extension itself, with the origin it was matched for
  if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.onMessage) {
    chrome.runtime.onMessage.addListener((m, sender, send) => {
      if (m.type !== 'hk-fill' || sender.id !== chrome.runtime.id) return false;
      if (m.origin !== location.origin || !sameOriginAsTop()) return false;
      const n = fill(m.username, m.password, m.totp); m.password = null;
      if (n > 0) { send({ filled: n }); return false; }
      return false;
    });
  }

  // the key icon in password boxes: a click (a real one, not a script's) shows the logins for this site
  function addIcon(p) {
    if (p.dataset.hkIcon) return; p.dataset.hkIcon = '1';
    const host = document.createElement('span'); const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = '<style>button{all:unset;cursor:pointer;width:18px;height:18px;display:flex;align-items:center;justify-content:center;border-radius:4px;background:#2a2333;color:#f07820;font:12px sans-serif}' +
      'ul{all:unset;position:absolute;top:22px;right:0;min-width:220px;background:#211b2a;border:1px solid #3a3248;border-radius:8px;padding:4px;z-index:2147483647;display:block;list-style:none}' +
      'li{all:unset;display:block;padding:6px 8px;border-radius:6px;color:#ece8f4;font:13px Segoe UI,sans-serif;cursor:pointer}li:hover{background:#3a2f4a}li small{display:block;color:#8f88a0;font-size:11px}</style>' +
      '<button title="Fill from Hallow Keys">&#128273;</button>';
    host.style.cssText = 'position:absolute;z-index:2147483646;';
    const place = () => { const r = p.getBoundingClientRect(); host.style.top = (window.scrollY + r.top + (r.height - 18) / 2) + 'px'; host.style.left = (window.scrollX + r.right - 24) + 'px'; host.style.display = visible(p) ? 'block' : 'none'; };
    document.body.appendChild(host); place();
    window.addEventListener('resize', place); window.addEventListener('scroll', place, true);
    const btn = root.querySelector('button');
    btn.addEventListener('click', ev => {
      if (!ev.isTrusted) return; ev.preventDefault(); ev.stopPropagation();
      let ul = root.querySelector('ul'); if (ul) { ul.remove(); return; }
      chrome.runtime.sendMessage({ type: 'inline-match' }, r => {
        ul = document.createElement('ul');
        const items = (r && r.ok && r.items) || [];
        const add = (text, sub, fn) => { const li = document.createElement('li'); li.textContent = text; if (sub) { const s = document.createElement('small'); s.textContent = sub; li.appendChild(s); } li.addEventListener('click', e => { if (!e.isTrusted) return; ul.remove(); fn(); }); ul.appendChild(li); };
        if (r && r.locked) add('Hallow Keys is locked', 'Click to open it', () => chrome.runtime.sendMessage({ type: 'popup-open' }));
        else if (!r || !r.ok) add((r && r.msg) || 'Hallow Keys isn\'t running', null, () => { });
        else if (items.length === 0) add('No logins for this site', null, () => { });
        else items.forEach(it => add(it.title, it.username, () => chrome.runtime.sendMessage({ type: 'inline-fill', id: it.id })));
        root.appendChild(ul);
      });
    });
  }
  function scan() { if (!sameOriginAsTop()) return; document.querySelectorAll('input[type=password]').forEach(p => { if (visible(p)) addIcon(p); }); }
  HK.scan = scan;

  // saving: when a form with a password is sent, offer to save it (the popup asks - nothing is saved without your OK)
  function captured(form) {
    const p = [...(form ? form.querySelectorAll('input[type=password]') : document.querySelectorAll('input[type=password]'))].find(x => x.value && visible(x));
    if (!p) return null;
    const pair = fields().find(f => f.pass === p);
    const user = pair && pair.user ? pair.user.value : '';
    return { username: user, password: p.value };
  }
  HK.captured = captured;
  if (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.sendMessage && sameOriginAsTop()) {
    document.addEventListener('submit', e => { if (!e.isTrusted) return; const c = captured(e.target); if (c) chrome.runtime.sendMessage({ type: 'submitted', username: c.username, password: c.password }); }, true);
    document.addEventListener('click', e => {
      if (!e.isTrusted) return; const b = e.target.closest && e.target.closest('button,input[type=submit]'); if (!b) return;
      const form = b.form || b.closest('form'); const c = captured(form); if (c && (!form || b.type === 'submit' || b.type === '')) chrome.runtime.sendMessage({ type: 'submitted', username: c.username, password: c.password });
    }, true);
    scan(); new MutationObserver(() => scan()).observe(document.documentElement, { childList: true, subtree: true });
  }
  if (window.__hallowKeysTest) window.__hallowKeysTest(HK);
})();
