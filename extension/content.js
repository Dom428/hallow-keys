// Hallow Keys extension - in the page. Never fills by itself: only when you pick something - in the suggestions under a box,
// the key icon or the extension's popup (the one exception: right after you filled a login that has a 2FA key, its code goes into
// the 2FA box on the next page of the same site, within 2 minutes). Only visible fields are filled, only in the top page or frames
// with the same origin as it, and cards and addresses only on https pages.
// v1.5: cards (checkout forms, split expiry and number boxes, the security code only after a second click or the setting) and
// addresses; suggestions under the focused box in a closed shadow root (titles and usernames only - nothing secret goes into the
// page until you pick one); two-step sign-ins; sign-up forms offered for saving.
'use strict';
(function () {
  if (window.__hallowKeysLoaded) return; window.__hallowKeysLoaded = true;
  const HK = {};
  const api = (typeof chrome !== 'undefined' && chrome.runtime && chrome.runtime.id) ? chrome : null;
  function send(msg) {
    return new Promise(res => {
      if (!api) return res(null);
      try { api.runtime.sendMessage(msg, r => { void (api.runtime.lastError); res(r); }); } catch (e) { res(null); }
    });
  }
  function sameOriginAsTop() { try { return window.top === window || window.top.location.origin === location.origin; } catch (e) { return false; } }
  HK.sameOriginAsTop = sameOriginAsTop;
  const secure = () => location.protocol === 'https:';
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
  const typeOf = c => (c.getAttribute('type') || '').toLowerCase();
  const userTypes = ['text', 'email', 'tel', ''];
  function labelText(el) {
    let t = '';
    try { if (el.id) { const l = document.querySelector('label[for="' + CSS.escape(el.id) + '"]'); if (l) t += ' ' + l.textContent; } } catch (e) { }
    const p = el.closest && el.closest('label'); if (p) t += ' ' + p.textContent;
    const lb = el.getAttribute('aria-labelledby'); if (lb) lb.split(/\s+/).forEach(id => { const x = document.getElementById(id); if (x) t += ' ' + x.textContent; });
    return t;
  }
  function desc(el) { return [el.getAttribute('name'), el.id, el.getAttribute('placeholder'), el.getAttribute('aria-label'), el.getAttribute('data-testid'), labelText(el)].join(' ').toLowerCase().replace(/\s+/g, ' '); }
  function ac(el) { return (el.getAttribute('autocomplete') || '').toLowerCase().split(/\s+/).filter(t => t && !/^section-/.test(t) && !['shipping', 'billing', 'home', 'work', 'mobile', 'fax', 'pager', 'on'].includes(t)); }
  HK.desc = desc;

  // ---- logins: the visible password boxes, and for each the visible text/email box just before it in the same form
  function fields() {
    const pw = [...document.querySelectorAll('input[type=password]')].filter(visible);
    const all = [...document.querySelectorAll('input')];
    const pairs = pw.map(p => {
      const scope = p.form ? [...p.form.querySelectorAll('input')] : all;
      const i = scope.indexOf(p); let user = null;
      for (let j = i - 1; j >= 0; j--) { const c = scope[j]; if (userTypes.includes(typeOf(c)) && visible(c) && !cardField(c)) { user = c; break; } if (c.type === 'password') break; }
      return { user: user, pass: p };
    });
    if (pairs.length === 0) {   // a "username first" page: just the user box
      const u = all.find(c => userTypes.includes(typeOf(c)) && visible(c) && (/user|mail|login|account|ident/i.test((c.name || '') + ' ' + (c.id || '') + ' ' + (c.autocomplete || '')) || ac(c).includes('username')) && !idOnlyForm(c));
      if (u) pairs.push({ user: u, pass: null });
    }
    return pairs;
  }
  HK.fields = fields;
  // an email box inside an address form isn't a "username first" page
  function idOnlyForm(c) { const f = c.form; if (!f) return false; return [...f.querySelectorAll('input,select,textarea')].filter(x => x !== c && visible(x) && idField(x)).length >= 2; }

  // ---- 2FA code boxes (v1.3)
  const otpRe = /one.?time|\botp|totp|2fa|mfa|two.?factor|verification.?code|verify.?code|auth\w*.?code|passcode|sms.?code|login.?code/i;
  function otpFields() {
    const login = new Set(); for (const f of fields()) { if (f.user) login.add(f.user); }
    const vis = [...document.querySelectorAll('input')].filter(i => ['text', 'tel', 'number', ''].includes(typeOf(i)) && !login.has(i) && !cardField(i) && visible(i));
    const one = vis.find(i => ac(i).includes('one-time-code') && i.maxLength !== 1);
    if (one) return [one];
    const groups = new Map();
    for (const i of vis.filter(x => x.maxLength === 1)) { const k = i.form || (i.parentElement && i.parentElement.parentElement) || document.body; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(i); }
    for (const g of groups.values()) if (g.length >= 4 && g.length <= 8) return g;
    const named = vis.find(i => i.maxLength !== 1 && otpRe.test([i.name, i.id, i.placeholder, i.getAttribute('aria-label') || '', i.getAttribute('autocomplete') || ''].join(' ')));
    return named ? [named] : [];
  }
  HK.otpFields = otpFields;

  // ---- v1.5 cards: what a box is for (autocomplete cc-*, then its name / id / placeholder / label)
  const rx = {
    cvc: /\b(cvc2?|cvv2?|csc|cid|cvn)\b|security.?(code|number)|card.?(code|verification)|verification.?(value|number)/,
    number: /card.?(number|no\b|num)|\bcc.?(number|num|no)\b|ccnum|cardnumber|credit.?card.?(number|no)?$|kartennummer|num[eé]ro.?(de.?)?carte|\bpan\b/,
    name: /card.?holder|name.?on.?(the.?)?card|\bcc.?name|holder.?name|card.?name|cardholder|nombre.?(del.?)?titular/,
    month: /exp\w*.?(month|mon|mm)\b|(card|cc).?(month|mm)\b|exp.?m\b|\bmm\b|\bmonth\b/,
    year: /exp\w*.?(year|yr|yy)|(card|cc).?(year|yy)|exp.?y\b|\byy(yy)?\b|\byear\b/,
    exp: /expir|exp.?date|\bexp\b|mm.?\/?.?yy|valid.?(thru|through|until|to)|cc.?exp|card.?exp|ablauf/
  };
  function looksMonthSelect(el) { if (el.tagName !== 'SELECT') return false; const o = [...el.options].map(x => (x.value + ' ' + x.text).toLowerCase()); return o.length >= 12 && o.length <= 14 && o.filter(t => /\b(0?[1-9]|1[0-2])\b|jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec/.test(t)).length >= 12; }
  function looksYearSelect(el) { if (el.tagName !== 'SELECT') return false; const y = new Date().getFullYear(); return [...el.options].filter(x => { const n = parseInt(x.value || x.text, 10); return (n >= y - 1 && n <= y + 25) || (n >= (y % 100) - 1 && n <= (y % 100) + 25 && /^\d{2}$/.test((x.value || x.text).trim())); }).length >= 4; }
  function cardField(el) {
    if (!el || !el.tagName) return null;
    const t = typeOf(el); if (el.tagName === 'INPUT' && !['text', 'tel', 'number', 'password', ''].includes(t)) return null;
    const a = ac(el);
    if (a.includes('cc-number')) return 'number'; if (a.includes('cc-csc')) return 'cvc'; if (a.includes('cc-exp')) return 'exp';
    if (a.includes('cc-exp-month')) return 'month'; if (a.includes('cc-exp-year')) return 'year';
    if (a.includes('cc-name') || a.includes('cc-given-name') || a.includes('cc-family-name')) return 'name';
    if (a.some(x => ['username', 'current-password', 'new-password', 'email', 'one-time-code', 'name', 'given-name', 'family-name', 'tel', 'postal-code'].includes(x))) return null;
    const d = desc(el); if (!d.trim()) return null;
    if (rx.cvc.test(d)) return 'cvc';
    if (rx.number.test(d)) return 'number';
    if (rx.name.test(d)) return 'name';
    if (/(exp|valid|card|cc)/.test(d) || el.tagName === 'SELECT') {
      if (rx.month.test(d) && !rx.year.test(d)) return 'month';
      if (rx.year.test(d) && !rx.month.test(d)) return 'year';
    }
    if (rx.exp.test(d)) return 'exp';
    if (looksMonthSelect(el) && /exp|card|cc|month/.test(d)) return 'month';
    if (looksYearSelect(el) && /exp|card|cc|year/.test(d)) return 'year';
    return null;
  }
  HK.cardField = cardField;
  // the card form: the visible boxes of the form (or page) that has a visible card number box
  function cardForm(near) {
    const nums = [...document.querySelectorAll('input')].filter(i => visible(i) && cardField(i) === 'number');
    if (!nums.length) return null;
    let scopeEl = near && near.form ? near.form : (nums[0].form || null);
    if (near && !near.form) scopeEl = null;
    const all = [...(scopeEl || document).querySelectorAll('input,select')].filter(visible);
    const f = { number: [], name: [], exp: [], month: [], year: [], cvc: [] };
    for (const el of all) { const k = cardField(el); if (k) f[k].push(el); }
    if (!f.number.length) return null;
    return f;
  }
  HK.cardForm = cardForm;

  // ---- v1.5 addresses / identities
  const idRx = [
    ['email', /e.?mail/], ['tel', /phone|\btel\b|mobile|telefon/],
    ['given', /first.?name|\bfname\b|given|forename|vorname|pr[eé]nom/], ['family', /last.?name|\blname\b|surname|family.?name|nachname/],
    ['address2', /address.?(line)?.?2\b|addr.?2|apartment|suite|\bapt\b|address2|street2/], ['address1', /address.?(line)?.?1\b|addr.?1|street|address1|stra(ss|ß)e|\baddress\b/],
    ['city', /\bcity\b|\btown\b|locality|suburb|\bort\b|ville/], ['state', /\bstate\b|province|region|county/],
    ['postcode', /post.?code|\bzip\b|zip.?code|postal|\bplz\b/], ['country', /country|\bland\b|pays/],
    ['name', /full.?name|your.?name|^\s*name\b|\bname\s*$|recipient/]
  ];
  const idAc = { 'name': 'name', 'given-name': 'given', 'family-name': 'family', 'email': 'email', 'tel': 'tel', 'tel-national': 'tel', 'street-address': 'address', 'address-line1': 'address1', 'address-line2': 'address2', 'address-level2': 'city', 'address-level1': 'state', 'postal-code': 'postcode', 'country': 'country', 'country-name': 'country' };
  function idField(el) {
    if (!el || !el.tagName) return null;
    const t = typeOf(el);
    if (el.tagName === 'INPUT' && !['text', 'email', 'tel', ''].includes(t)) return null;
    if (cardField(el)) return null;
    const a = ac(el);
    for (const x of a) if (idAc[x]) return idAc[x];
    if (a.some(x => ['username', 'current-password', 'new-password', 'one-time-code', 'off', 'organization'].includes(x))) return null;
    if (t === 'email') return 'email';
    if (t === 'tel') return 'tel';
    const d = desc(el); if (!d.trim()) return null;
    if (/user|login|company|organi[sz]ation|business|search|coupon|promo|captcha|card|holder/.test(d)) return null;
    for (const [k, r] of idRx) if (r.test(d)) return el.tagName === 'TEXTAREA' && (k === 'address1') ? 'address' : k;
    return null;
  }
  HK.idField = idField;
  // an address form: two or more different kinds of address boxes together (not a login or card form)
  function idForm(near) {
    const scopeEl = near && near.form ? near.form : null;
    const login = new Set(); for (const p of fields()) { if (p.user) login.add(p.user); if (p.pass) login.add(p.pass); }
    const all = [...(scopeEl || document).querySelectorAll('input,select,textarea')].filter(el => visible(el) && !login.has(el));
    const f = {}; let kinds = 0;
    for (const el of all) { const k = idField(el); if (!k) continue; if (!f[k]) { f[k] = []; kinds++; } f[k].push(el); }
    return kinds >= 2 ? f : null;
  }
  HK.idForm = idForm;

  // ---- setting values the way a person typing would (frameworks listen for input / change)
  function setValue(el, v) {
    if (el.tagName === 'SELECT') { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true })); return; }
    const proto = Object.getPrototypeOf(el); const d = Object.getOwnPropertyDescriptor(proto, 'value') || Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    d.set.call(el, v); el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true }));
  }
  // pick the option of a <select> that fits; true if one did
  function choose(sel, test) { const o = [...sel.options].find(x => !x.disabled && test((x.value || '').trim().toLowerCase(), (x.text || '').trim().toLowerCase())); if (!o) return false; setValue(sel, o.value); return true; }
  const months = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

  function fillTotp(code) {
    if (!sameOriginAsTop() || !code) return 0;
    const boxes = otpFields(); if (!boxes.length) return 0;
    if (boxes.length === 1) { setValue(boxes[0], code); return 1; }
    for (let i = 0; i < boxes.length && i < code.length; i++) setValue(boxes[i], code[i]);
    return boxes.length;
  }
  HK.fillTotp = fillTotp;
  function fill(username, password, totp) {
    if (!sameOriginAsTop()) return { filled: 0 };
    let n = 0, t = 0;
    for (const f of fields()) {
      if (f.user && username) { setValue(f.user, username); n++; }
      if (f.pass && password) { setValue(f.pass, password); n++; break; }   // the first visible password box only
    }
    if (totp && otpFields().length) t = fillTotp(totp) > 0 ? 1 : 0;
    return { filled: n + t, totp: t > 0 };
  }
  HK.fill = fill;

  // v1.5: a card. onlyCvc = the second click (just the security code). Returns { filled, needCvc }
  function fillCard(c, onlyCvc, near) {
    if (!sameOriginAsTop() || !secure() || !c) return { filled: 0 };
    const f = cardForm(near); if (!f) return { filled: 0 };
    let n = 0;
    if (onlyCvc) { if (c.cvc) for (const el of f.cvc) { setValue(el, c.cvc); n++; } return { filled: n, needCvc: false }; }
    const digits = (c.number || '').replace(/\D/g, '');
    if (digits && f.number.length > 1) {   // split into boxes of 4
      let at = 0; for (const el of f.number) { const len = el.maxLength > 0 && el.maxLength < 10 ? el.maxLength : 4; setValue(el, digits.substr(at, len)); at += len; n++; }
    } else if (digits) { setValue(f.number[0], digits); n++; }
    if (c.holder) for (const el of f.name) { setValue(el, c.holder); n++; }
    const mm = c.expMonth, yyyy = c.expYear;
    if (mm && yyyy) {
      const yy = yyyy.slice(-2), mi = parseInt(mm, 10);
      for (const el of f.exp) {
        if (el.tagName === 'SELECT') { if (choose(el, (v, t) => v.replace(/\s/g, '').includes(mm + '/' + yy) || t.replace(/\s/g, '').includes(mm + '/' + yy))) n++; continue; }
        const hint = ((el.getAttribute('placeholder') || '') + ' ' + desc(el)).toLowerCase();
        const v = /yyyy/.test(hint) || (el.maxLength >= 7 && el.maxLength < 10 && !/yy\b/.test(hint)) ? mm + '/' + yyyy : el.maxLength === 4 ? mm + yy : /mm\s?-\s?yy/.test(hint) ? mm + '-' + yy : (/mm \/ yy/.test(hint) ? mm + ' / ' + yy : mm + '/' + yy);
        setValue(el, v); n++;
      }
      for (const el of f.month) {
        if (el.tagName === 'SELECT') { if (choose(el, (v, t) => parseInt(v, 10) === mi || parseInt(t, 10) === mi || t.startsWith(months[mi - 1]) || v.startsWith(months[mi - 1]))) n++; }
        else { setValue(el, el.maxLength === 1 ? String(mi) : mm); n++; }
      }
      for (const el of f.year) {
        if (el.tagName === 'SELECT') { if (choose(el, (v, t) => v === yyyy || t === yyyy || v === yy || t === yy || v.endsWith(yyyy))) n++; }
        else { setValue(el, el.maxLength === 2 || /\byy\b/.test(desc(el) + ' ' + (el.getAttribute('placeholder') || '').toLowerCase()) && !/yyyy/.test((el.getAttribute('placeholder') || '').toLowerCase()) ? yy : yyyy); n++; }
      }
    }
    let needCvc = false;
    if (f.cvc.length) { if (c.cvc) { for (const el of f.cvc) { setValue(el, c.cvc); n++; } } else needCvc = true; }
    return { filled: n, needCvc: needCvc };
  }
  HK.fillCard = fillCard;

  // countries: a few names and codes so "United Kingdom" picks "GB" / "UK" in a list
  const countries = { gb: ['united kingdom', 'uk', 'great britain', 'britain', 'england', 'scotland', 'wales', 'northern ireland', 'gbr'], us: ['united states', 'united states of america', 'usa', 'us', 'america'], ca: ['canada', 'can'], au: ['australia', 'aus'], nz: ['new zealand', 'nzl'],
    ie: ['ireland', 'irl', 'eire'], de: ['germany', 'deutschland', 'deu'], fr: ['france', 'fra'], es: ['spain', 'españa', 'espana', 'esp'], it: ['italy', 'italia', 'ita'], nl: ['netherlands', 'the netherlands', 'holland', 'nederland', 'nld'], be: ['belgium', 'belgique', 'belgië', 'bel'],
    pt: ['portugal', 'prt'], se: ['sweden', 'sverige', 'swe'], no: ['norway', 'norge', 'nor'], dk: ['denmark', 'danmark', 'dnk'], fi: ['finland', 'suomi', 'fin'], pl: ['poland', 'polska', 'pol'], at: ['austria', 'österreich', 'aut'], ch: ['switzerland', 'schweiz', 'suisse', 'che'],
    jp: ['japan', 'jpn'], in: ['india', 'ind'], br: ['brazil', 'brasil', 'bra'], mx: ['mexico', 'méxico', 'mex'], za: ['south africa', 'zaf'], sg: ['singapore', 'sgp'], cn: ['china', 'chn'], kr: ['south korea', 'korea', 'kor'] };
  function countryCode(s) { s = (s || '').trim().toLowerCase().replace(/\./g, ''); if (!s) return null; if (countries[s]) return s; for (const k in countries) if (countries[k].includes(s)) return k; return null; }
  HK.countryCode = countryCode;
  function chooseCountry(sel, country) {
    const want = countryCode(country), name = (country || '').trim().toLowerCase();
    return choose(sel, (v, t) => (name && (t === name || v === name)) || (want && (countryCode(v) === want || countryCode(t) === want)));
  }
  // v1.5: an address
  function fillIdentity(d, near) {
    if (!sameOriginAsTop() || !secure() || !d) return { filled: 0 };
    const f = idForm(near); if (!f) return { filled: 0 };
    let n = 0;
    const put = (k, v) => { if (!v || !f[k]) return; for (const el of f[k]) { if (el.tagName === 'SELECT') { if (k === 'country' ? chooseCountry(el, v) : choose(el, (a, b) => a === v.toLowerCase() || b === v.toLowerCase())) n++; } else { setValue(el, v); n++; } } };
    put('given', d.given); put('family', d.family); put('name', d.name); put('email', d.email); put('tel', d.tel);
    if (f.address) for (const el of f.address) { setValue(el, el.tagName === 'TEXTAREA' ? d.address : (d.address || '').split('\n').map(x => x.trim()).filter(Boolean).join(', ')); n++; }
    if (f.address1 && !f.address2) put('address1', (d.address || '').split('\n').map(x => x.trim()).filter(Boolean).join(', ')); else { put('address1', d.address1); put('address2', d.address2); }
    put('city', d.city); put('postcode', d.postcode); put('country', d.country);
    return { filled: n };
  }
  HK.fillIdentity = fillIdentity;

  // filling: the request comes from the extension itself, with the origin it was matched for
  if (api && api.runtime.onMessage) {
    api.runtime.onMessage.addListener((m, sender, send2) => {
      if (!m || typeof m.type !== 'string' || !m.type.startsWith('hk-fill') || sender.id !== api.runtime.id) return false;
      if (m.origin !== location.origin || !sameOriginAsTop()) return false;
      let r = { filled: 0 };
      if (m.type === 'hk-fill-totp') { r = { filled: fillTotp(m.totp) }; m.totp = null; }
      else if (m.type === 'hk-fill') { r = fill(m.username, m.password, m.totp); m.password = null; m.totp = null; }
      else if (m.type === 'hk-fill-card') { r = fillCard(m.card, m.onlyCvc, UI.field); if (m.card) { m.card.number = null; m.card.cvc = null; } }
      else if (m.type === 'hk-fill-identity') r = fillIdentity(m.identity, UI.field);
      if (r.filled > 0) send2(r);
      return false;
    });
  }

  // ---- v1.5 suggestions under the focused box: a closed shadow root (the page can't read it), titles and usernames only
  const UI = { host: null, root: null, box: null, rows: [], sel: -1, field: null, kind: null, seq: 0, inline: true, cvcFor: null };
  HK.UI = UI;
  function kindOf(el) {
    if (!el || el.tagName === 'BUTTON') return null;
    if (otpFields().includes(el)) return 'otp';
    if (fields().some(p => p.user === el || p.pass === el)) return 'login';
    const c = cardField(el); if (c && cardForm(el)) return 'card';
    const i = idField(el); if (i && idForm(el)) return 'identity';
    return null;
  }
  HK.kindOf = kindOf;
  const css = ':host{all:initial}.box{position:absolute;min-width:240px;max-width:380px;background:#211b2a;border:1px solid #3a3248;border-radius:9px;padding:4px;box-shadow:0 6px 24px rgba(0,0,0,.45);font:13px "Segoe UI",system-ui,sans-serif;color:#ece8f4;z-index:2147483647}' +
    '.row{display:flex;gap:8px;align-items:center;padding:6px 8px;border-radius:6px;cursor:pointer}.row.on,.row:hover{background:#3a2f4a}.row b{display:block;font-weight:600}.row small{display:block;color:#8f88a0;font-size:11px}' +
    '.k{color:#f07820;font-size:13px}.head{padding:4px 8px 2px;color:#8f88a0;font-size:11px;letter-spacing:.3px}';
  function ensureHost() {
    if (UI.host && UI.host.isConnected) return;
    UI.host = document.createElement('hallow-keys-suggest'); UI.root = UI.host.attachShadow({ mode: 'closed' });
    const st = document.createElement('style'); st.textContent = css; UI.root.appendChild(st);
    UI.box = document.createElement('div'); UI.box.className = 'box'; UI.root.appendChild(UI.box);
    UI.box.addEventListener('mousedown', e => e.preventDefault());   // keep the focus in the page's box
    UI.host.style.cssText = 'position:absolute;left:0;top:0;width:0;height:0;z-index:2147483647;';
    document.documentElement.appendChild(UI.host);
  }
  function place() {
    if (!UI.field || !UI.box) return;
    const r = UI.field.getBoundingClientRect();
    UI.box.style.top = (window.scrollY + r.bottom + 3) + 'px'; UI.box.style.left = (window.scrollX + r.left) + 'px'; UI.box.style.width = Math.max(240, Math.min(380, r.width)) + 'px';
  }
  function close() { UI.seq++; UI.rows = []; UI.sel = -1; UI.field = null; UI.kind = null; UI.cvcFor = null; if (UI.host) { UI.host.remove(); UI.host = null; UI.box = null; UI.root = null; } }
  HK.close = close;
  function render(head, rows) {
    ensureHost(); UI.box.replaceChildren(); UI.rows = rows; UI.sel = rows.length ? 0 : -1;
    if (head) { const h = document.createElement('div'); h.className = 'head'; h.textContent = head; UI.box.appendChild(h); }
    rows.forEach((r, i) => {
      const d = document.createElement('div'); d.className = 'row' + (i === 0 ? ' on' : '');
      const k = document.createElement('span'); k.className = 'k'; k.textContent = '\u{1F511}'; d.appendChild(k);
      const t = document.createElement('div'); const b = document.createElement('b'); b.textContent = r.title; t.appendChild(b);
      if (r.sub) { const s = document.createElement('small'); s.textContent = r.sub; t.appendChild(s); }
      d.appendChild(t);
      d.addEventListener('click', e => { if (!e.isTrusted) return; e.preventDefault(); e.stopPropagation(); pick(i); });
      UI.box.appendChild(d);
    });
    place();
  }
  function highlight(i) { if (!UI.box) return; UI.sel = i; [...UI.box.querySelectorAll('.row')].forEach((d, j) => d.classList.toggle('on', j === i)); }
  async function pick(i) {
    const r = UI.rows[i]; if (!r) return;
    const field = UI.field;
    if (r.action) await r.action(field);
  }
  // open the suggestions for a box. force = from the key icon (shows "nothing here" messages, and works with suggestions off)
  async function open(field, force) {
    const kind = kindOf(field); if (!kind) { if (force) close(); return; }
    if (!UI.inline && !force) return;
    if ((kind === 'card' || kind === 'identity') && !secure()) return;
    const my = ++UI.seq; UI.field = field; UI.kind = kind;
    const r = await send({ type: 'inline-match', kind: kind === 'otp' ? 'login' : kind });
    if (my !== UI.seq || document.activeElement !== field && !force) return;
    UI.field = field; UI.kind = kind;
    if (!r || r.running === false) { if (force) render(null, [{ title: (r && r.msg) || 'Hallow Keys isn\'t running', sub: null }]); else close(); return; }
    if (r.locked) { render(null, [{ title: 'Hallow Keys is locked', sub: 'Unlock it to see your items', action: () => { close(); send({ type: 'popup-open' }); } }]); return; }
    if (!r.ok) { if (force) render(null, [{ title: r.msg || 'Something went wrong', sub: null }]); else close(); return; }
    let items = r.items || [];
    const rows = [];
    if (kind === 'otp') {
      items = items.filter(it => it.totp);
      items.forEach(it => rows.push({ title: it.title, sub: (it.username ? it.username + ' - ' : '') + '2FA code', action: async () => { close(); await send({ type: 'inline-totp', id: it.id }); } }));
      if (!rows.length) { if (force) render(null, [{ title: 'No 2FA codes for this site', sub: 'Add the 2FA key to the login in Hallow Keys' }]); else close(); return; }
      render('Hallow Keys - 2FA code', rows); return;
    }
    if (kind === 'login') {
      if (r.remembered) items = items.slice().sort((a, b) => (b.id === r.remembered) - (a.id === r.remembered));
      items.forEach(it => rows.push({ title: it.title, sub: (it.id === r.remembered ? 'Continue as ' : '') + (it.username || ''), action: async () => { close(); await send({ type: 'inline-fill', id: it.id }); } }));
      if (!rows.length) { if (force) render(null, [{ title: 'No logins for this site', sub: null }]); else close(); return; }
      render('Hallow Keys', rows); return;
    }
    if (kind === 'card') {
      items.forEach(it => rows.push({ title: it.title, sub: it.sub, action: async f => {
        const res = await send({ type: 'inline-card', id: it.id });
        if (res && res.ok && res.needCvc) {   // the security code: only after a second click
          UI.field = f; render('Hallow Keys - card filled', [{ title: 'Fill the security code', sub: it.title + ' - ' + it.sub, action: async () => { close(); await send({ type: 'inline-card', id: it.id, cvc: true }); } }]);
        } else close();
      } }));
      if (!rows.length) { if (force) render(null, [{ title: 'No cards in Hallow Keys', sub: null }]); else close(); return; }
      render('Hallow Keys - cards', rows); return;
    }
    items.forEach(it => rows.push({ title: it.title, sub: it.sub, action: async () => { close(); await send({ type: 'inline-identity', id: it.id }); } }));
    if (!rows.length) { if (force) render(null, [{ title: 'No addresses in Hallow Keys', sub: null }]); else close(); return; }
    render('Hallow Keys - addresses', rows);
  }
  HK.open = open;

  // the key icon in password, 2FA and card number boxes: a click (a real one, not a script's) opens the same list
  function addIcon(p, otp) {
    if (p.dataset.hkIcon) return; p.dataset.hkIcon = '1';
    const host = document.createElement('span'); const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = '<style>button{all:unset;cursor:pointer;width:18px;height:18px;display:flex;align-items:center;justify-content:center;border-radius:4px;background:#2a2333;color:#f07820;font:12px sans-serif}</style>' +
      '<button title="' + (otp ? 'Fill the 2FA code from Hallow Keys' : 'Fill from Hallow Keys') + '">&#128273;</button>';
    host.style.cssText = 'position:absolute;z-index:2147483646;';
    const placeIcon = () => { const r = p.getBoundingClientRect(); host.style.top = (window.scrollY + r.top + (r.height - 18) / 2) + 'px'; host.style.left = (window.scrollX + r.right - 24) + 'px'; host.style.display = visible(p) ? 'block' : 'none'; };
    document.body.appendChild(host); placeIcon();
    window.addEventListener('resize', placeIcon); window.addEventListener('scroll', placeIcon, true);
    const btn = root.querySelector('button');
    btn.addEventListener('mousedown', e => e.preventDefault());
    btn.addEventListener('click', ev => {
      if (!ev.isTrusted) return; ev.preventDefault(); ev.stopPropagation();
      if (UI.field === p && UI.host) { close(); return; }
      p.focus(); open(p, true);
    });
  }
  let otpAsked = null;
  function scan() {
    if (!sameOriginAsTop()) return;
    document.querySelectorAll('input[type=password]').forEach(p => { if (visible(p)) addIcon(p); });
    const o = otpFields();
    if (o.length) {
      addIcon(o[o.length - 1], true);
      // v1.5: a 2FA box appeared - if you just filled a login with a 2FA key on this site, the extension fills the code
      const key = location.href + '|' + o.length; if (otpAsked !== key) { otpAsked = key; send({ type: 'otp-ready' }); }
    }
    if (secure()) { const c = cardForm(); if (c) addIcon(c.number[0]); }
  }
  HK.scan = scan;

  // saving: when a form with a password is sent, offer to save it (the popup asks - nothing is saved without your OK).
  // v1.5: sign-up and change-password forms (a "new password" box, or a password typed twice) offer the new password.
  function captured(form) {
    const scope = form || document;
    const pws = [...scope.querySelectorAll('input[type=password]')].filter(x => x.value && visible(x));
    if (!pws.length) return null;
    const isNew = x => ac(x).includes('new-password') || /\bnew|confirm|repeat|retype|again|create|choose/.test(desc(x));
    let p = pws[0], signup = false; const news = pws.filter(isNew);
    if (news.length) { p = news[0]; signup = true; if (news.length >= 2 && news[0].value !== news[1].value) return null; }
    else if (pws.length >= 2 && pws[pws.length - 1].value === pws[pws.length - 2].value) { p = pws[pws.length - 2]; signup = true; }
    let user = ''; const pair = fields().find(f => f.pass === p) || fields().find(f => pws.includes(f.pass) && f.user);
    if (pair && pair.user) user = pair.user.value;
    if (!user) { const u = [...scope.querySelectorAll('input')].find(c => (typeOf(c) === 'email' || ac(c).includes('username') || /user|mail|login/.test(desc(c))) && c.value && visible(c)); if (u) user = u.value; }
    return { username: user, password: p.value, signup: signup };
  }
  HK.captured = captured;
  // a "username first" page that's sent: remember the username for the password page (for saving)
  function usernameStep(form) {
    const f = fields(); if (f.length !== 1 || f[0].pass || !f[0].user || !f[0].user.value) return null;
    if (form && f[0].user.form !== form) return null;
    return f[0].user.value;
  }
  HK.usernameStep = usernameStep;
  // the extension's settings (popup): suggestions on / off
  if (api && api.storage && api.storage.local) {
    try { api.storage.local.get({ inline: true }, s => { UI.inline = !s || s.inline !== false; }); } catch (e) { }
    try { api.storage.onChanged.addListener((c, area) => { if (area === 'local' && c.inline) { UI.inline = c.inline.newValue !== false; if (!UI.inline) close(); } }); } catch (e) { }
  }
  if (api && sameOriginAsTop()) {
    const sent = (c) => send({ type: 'submitted', username: c.username, password: c.password, signup: c.signup });
    document.addEventListener('submit', e => {
      if (!e.isTrusted) return; const c = captured(e.target); if (c) { sent(c); return; }
      const u = usernameStep(e.target); if (u) send({ type: 'username-step', username: u });
    }, true);
    document.addEventListener('click', e => {
      if (!e.isTrusted) return; const b = e.target.closest && e.target.closest('button,input[type=submit]'); if (!b) return;
      const form = b.form || b.closest('form'); if (form || b.type === 'submit' || b.type === '') { const c = captured(form); if (c) { sent(c); return; } const u = usernameStep(form); if (u) send({ type: 'username-step', username: u }); }
    }, true);
    // suggestions: on focus (a real one) and on a click into the box
    document.addEventListener('focusin', e => { if (!e.isTrusted) return; const t = e.target; if (t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA') && t !== UI.field) { close(); if (t.tagName !== 'SELECT') open(t, false); } }, true);
    document.addEventListener('mousedown', e => { if (!e.isTrusted) return; const t = e.target; if (t === UI.field) return; if (t && t.tagName === 'INPUT' && document.activeElement === t && !UI.host) open(t, false); }, true);
    document.addEventListener('focusout', e => { if (!e.isTrusted || e.target !== UI.field) return; setTimeout(() => { if (UI.field && document.activeElement !== UI.field) close(); }, 150); }, true);
    document.addEventListener('keydown', e => {
      if (!UI.host || e.target !== UI.field) return;
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); return; }
      if (!e.isTrusted) return;
      if (e.key === 'ArrowDown' && UI.rows.length) { e.preventDefault(); highlight((UI.sel + 1) % UI.rows.length); }
      else if (e.key === 'ArrowUp' && UI.rows.length) { e.preventDefault(); highlight((UI.sel - 1 + UI.rows.length) % UI.rows.length); }
      else if (e.key === 'Enter' && UI.sel >= 0 && UI.rows[UI.sel] && UI.rows[UI.sel].action) { e.preventDefault(); e.stopPropagation(); pick(UI.sel); }
      else if (e.key === 'Tab') close();
    }, true);
    window.addEventListener('resize', place); window.addEventListener('scroll', place, true);
    scan(); let pend = 0; new MutationObserver(() => { if (!pend) pend = setTimeout(() => { pend = 0; scan(); }, 250); }).observe(document.documentElement, { childList: true, subtree: true });
  }
  if (window.__hallowKeysTest) window.__hallowKeysTest(HK);
})();
