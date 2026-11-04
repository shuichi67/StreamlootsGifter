const BRIDGE = "http://127.0.0.1:8765";

async function readResponse(response) {
  let data = {};
  try { data = await response.json(); } catch { /* empty response */ }
  if (!response.ok) throw new Error(data.error || `Local Bridge returned ${response.status}`);
  return data;
}

async function callBridge(path, options = {}) {
  const { token } = await chrome.storage.local.get("token");
  if (!token) throw new Error("Connect the extension to Streamloots Gifter first.");
  const response = await fetch(`${BRIDGE}${path}`, {
    ...options,
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json",
      ...(options.headers || {})
    }
  });
  return readResponse(response);
}

async function checkConnection() {
  const { token } = await chrome.storage.local.get("token");
  if (!token) return { connected: false, reason: "disconnected" };
  try {
    await callBridge("/health");
    const tabs = await chrome.tabs.query({ url: ["https://www.streamloots.com/*", "https://streamloots.com/*"] });
    let contentScriptReady = false;
    let activityFeedOpen = false;
    let activityFeedReady = false;
    let giftPageReady = false;
    for (const tab of tabs) {
      try { activityFeedOpen ||= /\/activity-feed(?:\/|$)/i.test(new URL(tab.url || "").pathname); } catch { /* malformed or unavailable tab URL */ }
      if (tab.id === undefined) continue;
      try {
        const response = await chrome.tabs.sendMessage(tab.id, { type: "gifter-status" });
        if (response?.ready) {
          contentScriptReady = true;
          activityFeedOpen ||= Boolean(response.activityFeed);
          activityFeedReady ||= Boolean(response.activityFeedReady);
          giftPageReady ||= Boolean(response.canProcessGifts);
        }
      } catch { /* this Streamloots tab has not loaded the current extension script */ }
    }
    const { gifterLastError, activityFeedLastCard } = await chrome.storage.local.get(["gifterLastError", "activityFeedLastCard"]);
    return { connected: true, streamlootsOpen: tabs.length > 0, contentScriptReady, activityFeedOpen, activityFeedReady, giftPageReady, gifterLastError: gifterLastError || "", activityFeedLastCard: activityFeedLastCard || null };
  } catch (error) {
    return {
      connected: false,
      reason: error.message.includes("fetch") ? "app-offline" : "key-invalid",
      error: error.message
    };
  }
}

async function pairExtension() {
  const response = await fetch(`${BRIDGE}/pair`, {
    method: "GET",
    cache: "no-store",
    // Match manual validation's extension-origin preflight. The local Bridge
    // uses that origin to decide whether the pairing endpoint may return a key.
    headers: { "Content-Type": "application/json" }
  });
  const { token } = await readResponse(response);
  if (!token) throw new Error("The app did not return a Bridge key.");
  await chrome.storage.local.set({ token });
  return { ok: true, token };
}

async function connectWithKey(value) {
  const token = String(value || "").trim();
  if (!token) throw new Error("Paste the Bridge key copied from Streamloots Gifter.");
  const response = await fetch(`${BRIDGE}/health`, {
    headers: { "Authorization": `Bearer ${token}` }
  });
  await readResponse(response);
  await chrome.storage.local.set({ token });
  return { ok: true };
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  let task;
  switch (message?.type) {
    case "claim-gift":
      task = callBridge("/next");
      break;
    case "complete-gift":
      task = callBridge("/complete", { method: "POST", body: JSON.stringify(message.payload) });
      break;
    case "streamloots-card-feed":
      task = callBridge("/card-redeemed", { method: "POST", body: JSON.stringify(message.payload) });
      break;
    case "get-streamloots-card-rules":
      task = callBridge("/card-rules");
      break;
    case "get-streamloots-language":
      task = callBridge("/extension-settings");
      break;
    case "pair-bridge":
      task = pairExtension();
      break;
    case "connect-with-key":
      task = connectWithKey(message.token);
      break;
    case "record-gifter-status":
      task = chrome.storage.local.set({
        gifterLastPoll: Number(message.lastPoll) || Date.now(),
        gifterLastError: String(message.error || "")
      }).then(() => ({ ok: true }));
      break;
    case "record-activity-feed-status":
      task = (message.ready
        ? chrome.storage.local.set({ activityFeedReady: true, activityFeedLastSeen: Date.now() })
        : chrome.storage.local.remove("activityFeedLastCard").then(() => chrome.storage.local.set({ activityFeedReady: false, activityFeedLastSeen: Date.now() })))
        .then(() => ({ ok: true }));
      break;
    case "record-activity-feed-card":
      task = chrome.storage.local.set({ activityFeedLastCard: message.card || null }).then(() => ({ ok: true }));
      break;
    case "rotate-bridge-key":
      task = callBridge("/rotate", { method: "POST" }).then(async result => {
        await chrome.storage.local.set({ token: result.token });
        return { ok: true, token: result.token };
      });
      break;
    case "disconnect-bridge":
      task = chrome.storage.local.remove("token").then(() => ({ ok: true }));
      break;
    case "bridge-status":
      task = checkConnection();
      break;
    default:
      return false;
  }

  task.then(sendResponse).catch(error => sendResponse({ error: error.message }));
  return true;
});
