// Hallow Keys extension 1.6 - passkeys, the part that runs in the page itself (MAIN world, https pages only, before the page's scripts).
// It wraps navigator.credentials.create / get for passkey (publicKey) requests in the top page or frames with the same origin, and
// asks the extension's own prompt (webauthn-bridge.js, which the page can't reach into) instead of the browser's. Everything else -
// passwords, federated logins, cross-origin frames, ES256-less requests - goes to the browser untouched, and the prompt's "Use another
// device or the browser's own" calls the browser's original function. No key is ever in the page: the answer is the signed result only.
// v1.7 (Keys 2.12): sites that ask for a security key or a phone (authenticatorAttachment "cross-platform", hints) get the prompt with
// "Use another device" first; getClientCapabilities() says what Keys adds (passkeys on this PC, autofill, related origins) while it runs.
// Related origins (one site, several domains - Microsoft's sign-in) are checked by the app against the site's own list.
(function () {
  'use strict';
  if (window.__hallowKeysPasskeyPage) return;
  try { Object.defineProperty(window, '__hallowKeysPasskeyPage', { value: true }); } catch (e) { return; }
  if (location.protocol !== 'https:' || !window.isSecureContext || typeof navigator.credentials === 'undefined' || typeof CredentialsContainer === 'undefined') return;
  const proto = CredentialsContainer.prototype; const origCreate = proto.create, origGet = proto.get;
  const PKC = window.PublicKeyCredential;
  function sameTop() { try { return window.top === window || window.top.location.origin === location.origin; } catch (e) { return false; } }
  const enc = buf => { const b = new Uint8Array(buf); let s = ''; for (let i = 0; i < b.length; i++) s += String.fromCharCode(b[i]); return btoa(s).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, ''); };
  const dec = s => { s = String(s || '').replace(/-/g, '+').replace(/_/g, '/'); while (s.length % 4) s += '='; const b = atob(s); const u = new Uint8Array(b.length); for (let i = 0; i < b.length; i++) u[i] = b.charCodeAt(i); return u.buffer; };
  const bytes = x => { if (x instanceof ArrayBuffer) return x; if (ArrayBuffer.isView(x)) return x.buffer.slice(x.byteOffset, x.byteOffset + x.byteLength); throw new TypeError('Expected a BufferSource'); };
  const copy = ab => ab.slice(0);
  // to the extension's isolated script and back (window messages tagged for Hallow Keys; the bridge acknowledges at once)
  const pending = new Map(); let seq = 0;
  window.addEventListener('message', e => {
    if (e.source !== window || !e.data || e.data.__hk !== 'pk-res') return;
    const p = pending.get(e.data.rid); if (!p) return;
    if (e.data.ack) { p.acked = true; return; }
    pending.delete(e.data.rid); p.done(e.data);
  });
  function call(op, payload) {
    return new Promise(done => {
      const rid = 'hk' + (++seq) + '-' + Math.random().toString(36).slice(2);
      const p = { done: done, acked: false }; pending.set(rid, p);
      window.postMessage(Object.assign({ __hk: 'pk-req', rid: rid, op: op }, payload), location.origin);
      setTimeout(() => { if (!p.acked && pending.has(rid)) { pending.delete(rid); done({ action: 'fallback' }); } }, 1500);   // no bridge: the browser's own
    });
  }
  function abortErr(signal) { return signal && signal.reason !== undefined ? signal.reason : new DOMException('The operation was aborted.', 'AbortError'); }
  function withAbort(signal, rid, promise) {
    if (!signal) return promise;
    return new Promise((res, rej) => {
      const onAbort = () => { window.postMessage({ __hk: 'pk-req', rid: 'abort-' + Math.random(), op: 'abort' }, location.origin); rej(abortErr(signal)); };
      signal.addEventListener('abort', onAbort, { once: true });
      promise.then(v => { signal.removeEventListener('abort', onAbort); res(v); }, er => { signal.removeEventListener('abort', onAbort); rej(er); });
    });
  }
  function define(o, props) { for (const k in props) Object.defineProperty(o, k, { value: props[k], enumerable: true, configurable: true }); return o; }
  function credential(c, kind, extResults) {
    const rawId = dec(c.id);
    const R = kind === 'create' ? window.AuthenticatorAttestationResponse : window.AuthenticatorAssertionResponse;
    const resp = Object.create(R ? R.prototype : Object.prototype);
    const cd = dec(c.clientDataJSON), ad = dec(c.authenticatorData);
    if (kind === 'create') {
      const att = dec(c.attestationObject), pub = dec(c.publicKey), tr = (c.transports || []).slice();
      define(resp, {
        clientDataJSON: cd, attestationObject: att,
        getAuthenticatorData: function () { return copy(ad); }, getPublicKey: function () { return copy(pub); },
        getPublicKeyAlgorithm: function () { return c.publicKeyAlgorithm; }, getTransports: function () { return tr.slice(); },
        toJSON: function () { return { clientDataJSON: c.clientDataJSON, attestationObject: c.attestationObject, authenticatorData: c.authenticatorData, publicKey: c.publicKey, publicKeyAlgorithm: c.publicKeyAlgorithm, transports: tr.slice() }; }
      });
    } else {
      define(resp, {
        clientDataJSON: cd, authenticatorData: ad, signature: dec(c.signature), userHandle: c.userHandle ? dec(c.userHandle) : null,
        toJSON: function () { return { clientDataJSON: c.clientDataJSON, authenticatorData: c.authenticatorData, signature: c.signature, userHandle: c.userHandle || undefined }; }
      });
    }
    const cred = Object.create(PKC ? PKC.prototype : Object.prototype);
    const ext = extResults || {};
    define(cred, {
      id: c.id, rawId: rawId, type: 'public-key', response: resp, authenticatorAttachment: 'platform',
      getClientExtensionResults: function () { return JSON.parse(JSON.stringify(ext)); },
      toJSON: function () { return { id: c.id, rawId: c.id, type: 'public-key', response: resp.toJSON(), authenticatorAttachment: 'platform', clientExtensionResults: JSON.parse(JSON.stringify(ext)) }; }
    });
    return cred;
  }
  function fail(r) {
    if (r.name === 'TypeError') return new TypeError(r.msg || 'Invalid request');
    return new DOMException(r.msg || 'The operation either timed out or was not allowed.', r.name || 'NotAllowedError');
  }
  function hintsOf(pk) { return Array.isArray(pk.hints) ? pk.hints.filter(h => typeof h === 'string').slice(0, 5).map(h => h.slice(0, 20)) : []; }
  function rpIdOf(pk, kind) { const v = kind === 'create' ? (pk.rp && pk.rp.id) : pk.rpId; return v === undefined || v === null ? location.hostname : String(v); }

  proto.create = function create(options) {
    const self = this, args = arguments;
    if (!options || !options.publicKey || !sameTop()) return origCreate.apply(self, args);
    const signal = options.signal; if (signal && signal.aborted) return Promise.reject(abortErr(signal));
    let req;
    try {
      const pk = options.publicKey;
      req = { rpId: rpIdOf(pk, 'create'), rpName: pk.rp && pk.rp.name ? String(pk.rp.name) : '', challenge: enc(bytes(pk.challenge)),
        userId: enc(bytes(pk.user.id)), userName: String(pk.user.name || ''), displayName: String(pk.user.displayName || ''),
        algs: (pk.pubKeyCredParams || []).filter(p => p && p.type === 'public-key').map(p => String(p.alg)),
        exclude: (pk.excludeCredentials || []).filter(c => c && c.type === 'public-key').map(c => enc(bytes(c.id))),
        attachment: pk.authenticatorSelection && typeof pk.authenticatorSelection.authenticatorAttachment === 'string' ? pk.authenticatorSelection.authenticatorAttachment.slice(0, 20) : '',
        hints: hintsOf(pk) };
    } catch (e) { return origCreate.apply(self, args); }
    const credProps = !!(options.publicKey.extensions && options.publicKey.extensions.credProps);
    return withAbort(signal, null, call('create', { req: req }).then(r => {
      if (r.action === 'fallback') return origCreate.apply(self, args);
      if (r.action === 'cred') return credential(r.cred, 'create', credProps ? { credProps: { rk: true } } : {});
      throw fail(r);
    }));
  };
  proto.get = function get(options) {
    const self = this, args = arguments;
    if (!options || !options.publicKey || !sameTop()) return origGet.apply(self, args);
    const signal = options.signal; if (signal && signal.aborted) return Promise.reject(abortErr(signal));
    let req;
    try {
      const pk = options.publicKey;
      req = { rpId: rpIdOf(pk, 'get'), challenge: enc(bytes(pk.challenge)), allow: (pk.allowCredentials || []).filter(c => c && c.type === 'public-key').map(c => enc(bytes(c.id))),
        conditional: options.mediation === 'conditional', hints: hintsOf(pk) };
    } catch (e) { return origGet.apply(self, args); }
    return withAbort(signal, null, call('get', { req: req }).then(r => {
      if (r.action === 'fallback') return origGet.apply(self, args);
      if (r.action === 'cred') return credential(r.cred, 'get', {});
      throw fail(r);
    }));
  };
  // passkey autofill (conditional UI) is offered in the suggestions under username boxes; Keys counts as a platform authenticator
  if (PKC && sameTop()) {
    const ask = what => call('status', {}).then(r => r && r.running ? true : null);
    const origCond = PKC.isConditionalMediationAvailable, origUv = PKC.isUserVerifyingPlatformAuthenticatorAvailable;
    try { PKC.isConditionalMediationAvailable = function isConditionalMediationAvailable() { return ask().then(v => v || (origCond ? origCond.call(PKC) : false)); }; } catch (e) { }
    try { PKC.isUserVerifyingPlatformAuthenticatorAvailable = function isUserVerifyingPlatformAuthenticatorAvailable() { return ask().then(v => v || (origUv ? origUv.call(PKC) : false)); }; } catch (e) { }
    // v1.7: only where the browser has it (never invented): while Keys runs, it adds passkeys on this PC, autofill and related origins
    const origCaps = PKC.getClientCapabilities;
    if (typeof origCaps === 'function') try {
      PKC.getClientCapabilities = function getClientCapabilities() {
        return Promise.all([origCaps.call(PKC).catch(() => ({})), ask()]).then(([caps, on]) => {
          const c = Object.assign({}, caps);
          if (on) { c.passkeyPlatformAuthenticator = true; c.userVerifyingPlatformAuthenticator = true; c.conditionalGet = true; c.relatedOrigins = true; }
          return c;
        });
      };
    } catch (e) { }
  }
})();
