(() => {
  "use strict";

  const CHANNEL = "streamloots-gifter-activity-feed";
  const EVENTS_PATH = /\/pages\/[^/]+\/events\/?$/i;
  if (!/\/activity-feed(?:\/|$)/i.test(location.pathname)) return;
  if (window.__streamlootsGifterCursorMonitor) return;
  window.__streamlootsGifterCursorMonitor = true;
  const log = (...values) => console.info("[SLG API]", ...values);
  log("Cursor monitor installed on Activity Feed", { path: location.pathname });

  const pendingPolls = window.__streamlootsGifterPendingPolls = window.__streamlootsGifterPendingPolls || [];

  function eventCursor(rawUrl) {
    try {
      const url = new URL(rawUrl, location.href);
      if (url.hostname !== "api.streamloots.com" || !EVENTS_PATH.test(url.pathname)) return null;
      return url.searchParams.get("before") || "";
    } catch {
      return null;
    }
  }

  function collectRedeemedCards(payload) {
    const cards = [];
    const pending = Array.isArray(payload) ? [...payload] : [payload];
    const visited = new Set();
    while (pending.length) {
      const item = pending.pop();
      if (!item || typeof item !== "object" || visited.has(item)) continue;
      visited.add(item);
      const eventId = item.id || item._id;
      if (item.type === "CollectionCard" && item.subtype === "Redeemed" && eventId && item.createdAt && item.metadata?.cardName) {
        cards.push({
          id: String(eventId),
          createdAt: String(item.createdAt),
          username: String(item.username || item.userName || item.user?.username || item.user?.userName || item.metadata?.username || ""),
          pageSlug: String(item.pageSlug || ""),
          cardName: String(item.metadata.cardName),
          cardFields: Array.isArray(item.metadata.cardFields)
            ? item.metadata.cardFields.map(field => ({
                name: String(field?.name || ""),
                label: String(field?.label || ""),
                value: String(field?.value || "")
              }))
            : []
        });
        continue;
      }
      for (const value of Object.values(item)) {
        if (Array.isArray(value)) pending.push(...value);
        else if (value && typeof value === "object") pending.push(value);
      }
    }
    return cards;
  }

  function summarizeResponse(payload) {
    const pending = Array.isArray(payload) ? [...payload] : [payload];
    const visited = new Set();
    const eventTypes = [];
    while (pending.length && eventTypes.length < 20) {
      const item = pending.pop();
      if (!item || typeof item !== "object" || visited.has(item)) continue;
      visited.add(item);
      if (item.type || item.subtype) {
        eventTypes.push({
          id: String(item.id || item._id || ""),
          type: String(item.type || ""),
          subtype: String(item.subtype || ""),
          createdAt: String(item.createdAt || ""),
          cardName: String(item.metadata?.cardName || "")
        });
      }
      for (const value of Object.values(item)) {
        if (Array.isArray(value)) pending.push(...value);
        else if (value && typeof value === "object") pending.push(value);
      }
    }
    return { root: Array.isArray(payload) ? `array(${payload.length})` : payload && typeof payload === "object" ? Object.keys(payload).slice(0, 12) : typeof payload, eventTypes };
  }

  function publishPoll(cursor, payload) {
    if (!cursor) {
      log("Matched /events response, but the request has no before= cursor.");
      return;
    }
    const cards = collectRedeemedCards(payload);
    const responseSummary = summarizeResponse(payload);
    const message = {
      source: CHANNEL,
      kind: "event-poll",
      cursor,
      observedAt: Date.now(),
      cards,
      responseSummary
    };
    log("Poll response captured", { cursor, redeemedCards: cards.length, ids: cards.map(card => card.id), responseSummary, status: payload === null ? "empty or non-JSON" : "JSON" });
    pendingPolls.push(message);
    if (pendingPolls.length > 30) pendingPolls.splice(0, pendingPolls.length - 30);
    window.postMessage(message, location.origin);
  }

  const originalFetch = window.fetch;
  window.fetch = function (...args) {
    const input = args[0];
    const requestUrl = input instanceof Request ? input.url : String(input || "");
    const cursor = eventCursor(requestUrl);
    const responsePromise = Reflect.apply(originalFetch, this, args);
    if (cursor === null) return responsePromise;
    if (!cursor) {
      log("Found the /events endpoint, but its URL has no before= cursor.");
      return responsePromise;
    }
    log("Captured fetch request", { cursor });
    return responsePromise.then(response => {
      try {
        response.clone().json().then(payload => publishPoll(cursor, payload)).catch(() => publishPoll(cursor, null));
      } catch {
        publishPoll(cursor, null);
      }
      return response;
    }, error => {
      log("Fetch request failed", { cursor, error: String(error) });
      throw error;
    });
  };

  const originalOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function (method, url, ...rest) {
    this.__streamlootsGifterCursor = eventCursor(String(url || ""));
    if (this.__streamlootsGifterCursor) log("Captured XHR request", { cursor: this.__streamlootsGifterCursor });
    else if (this.__streamlootsGifterCursor === "") log("Found an XHR /events endpoint URL without a before= cursor.");
    return Reflect.apply(originalOpen, this, [method, url, ...rest]);
  };
  const originalSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.send = function (...args) {
    const cursor = this.__streamlootsGifterCursor;
    if (cursor) {
      this.addEventListener("load", () => {
        try {
          const payload = this.responseType === "json" ? this.response : JSON.parse(this.responseText);
          publishPoll(cursor, payload);
        } catch {
          publishPoll(cursor, null);
        }
      }, { once: true });
    }
    return Reflect.apply(originalSend, this, args);
  };

  window.addEventListener("message", event => {
    if (event.source !== window || event.origin !== location.origin || event.data?.source !== CHANNEL || event.data?.kind !== "ping") return;
    window.postMessage({ source: CHANNEL, kind: "ready" }, location.origin);
    log("Monitor connected to the extension content script.");
    for (const poll of pendingPolls) window.postMessage(poll, location.origin);
  });
})();
