let isWorking = false;
const isActivityFeed = /\/activity-feed(?:\/|$)/i.test(location.pathname);
let activityFeedReady = false;
const seenActivityCards = new Map();
const seenActivityCardTtlMs = 10 * 60 * 1000;
const maxSeenActivityCards = 5000;

function hasSeenActivityCard(eventId, now = Date.now()) {
  const expiresAt = seenActivityCards.get(eventId);
  if (!expiresAt) return false;
  if (expiresAt <= now) {
    seenActivityCards.delete(eventId);
    return false;
  }
  return true;
}

function rememberActivityCard(eventId, now = Date.now()) {
  if (!eventId) return;
  for (const [id, expiresAt] of seenActivityCards) {
    if (expiresAt > now) break;
    seenActivityCards.delete(id);
  }
  seenActivityCards.delete(eventId);
  seenActivityCards.set(eventId, now + seenActivityCardTtlMs);
  while (seenActivityCards.size > maxSeenActivityCards)
    seenActivityCards.delete(seenActivityCards.keys().next().value);
}
let streamlootsLanguageCache = "";
let streamlootsLanguageCacheUntil = 0;
let streamlootsLanguageRequest = null;

async function getStreamlootsLanguage() {
  if (Date.now() < streamlootsLanguageCacheUntil) return streamlootsLanguageCache;
  if (streamlootsLanguageRequest) return streamlootsLanguageRequest;
  streamlootsLanguageRequest = chrome.runtime.sendMessage({ type: "get-streamloots-language" })
    .then(result => {
      const configured = result?.streamloots_language;
      const pageLanguage = document.documentElement.lang || navigator.language || "";
      streamlootsLanguageCache = configured === "Spanish" || /^es(?:-|$)/i.test(pageLanguage) ? "Spanish" : "English";
      streamlootsLanguageCacheUntil = Date.now() + 30_000;
      return streamlootsLanguageCache;
    })
    .catch(() => {
      streamlootsLanguageCache = /^es(?:-|$)/i.test(document.documentElement.lang || navigator.language || "") ? "Spanish" : "English";
      streamlootsLanguageCacheUntil = Date.now() + 10_000;
      return streamlootsLanguageCache;
    })
    .finally(() => { streamlootsLanguageRequest = null; });
  return streamlootsLanguageRequest;
}

function exactLocalizedAction(root, selector, english, spanish, preferredLanguage) {
  const labels = preferredLanguage === "Spanish" ? [...spanish, ...english] : [...english, ...spanish];
  const normalizedLabels = new Set(labels.map(label => label.normalize("NFKC").replace(/\s+/g, " ").trim().toLocaleLowerCase()));
  return [...root.querySelectorAll(selector)].find(element => visible(element) &&
    normalizedLabels.has((element.textContent || "").normalize("NFKC").replace(/\s+/g, " ").trim().toLocaleLowerCase()));
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "gifter-status") {
    sendResponse({ ready: true, working: isWorking, activityFeed: isActivityFeed, activityFeedReady, canProcessGifts: canOpenGiftForm() });
  }
});

async function recordPollStatus(error = "") {
  try {
    await chrome.runtime.sendMessage({
      type: "record-gifter-status",
      lastPoll: Date.now(),
      error
    });
  } catch {
    // A status update should not stop gift polling if the worker is restarting.
  }
}

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function visible(element) {
  return Boolean(element && (element.offsetWidth || element.offsetHeight || element.getClientRects().length));
}

function activateMenuChoice(element) {
  const rect = element.getBoundingClientRect();
  const point = {
    bubbles: true,
    cancelable: true,
    composed: true,
    view: window,
    button: 0,
    buttons: 1,
    clientX: rect.left + rect.width / 2,
    clientY: rect.top + rect.height / 2
  };
  const pointerPoint = { ...point, pointerId: 1, pointerType: "mouse", isPrimary: true };
  element.dispatchEvent(new PointerEvent("pointerdown", pointerPoint));
  element.dispatchEvent(new MouseEvent("mousedown", point));
  element.dispatchEvent(new PointerEvent("pointerup", { ...pointerPoint, buttons: 0 }));
  element.dispatchEvent(new MouseEvent("mouseup", { ...point, buttons: 0 }));
  element.dispatchEvent(new MouseEvent("click", { ...point, buttons: 0, detail: 1 }));
}

function exactTextElement(root, selector, text) {
  return [...root.querySelectorAll(selector)].find(element =>
    visible(element) && element.textContent.trim() === text
  );
}

async function waitFor(getValue, timeout = 10000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const value = getValue();
    if (value) return value;
    await sleep(200);
  }
  throw new Error("Timed out waiting for the Streamloots gift form.");
}

let cachedGiftForm = null;
let cachedGiftAction = null;
let giftActionLookupExpiresAt = 0;
const giftActionLabels = new Set(["Gift packs", "Regalar cofres", "Regalar packs"]);

function findGiftAction() {
  const isGiftAction = element => giftActionLabels.has((element?.textContent || "").replace(/\s+/g, " ").trim());
  if (cachedGiftAction?.isConnected && visible(cachedGiftAction) && isGiftAction(cachedGiftAction)) return cachedGiftAction;
  cachedGiftAction = null;
  if (Date.now() < giftActionLookupExpiresAt) return null;
  giftActionLookupExpiresAt = Date.now() + 3000;
  cachedGiftAction = [...document.querySelectorAll("button, [role='button']")]
    .find(element => visible(element) && isGiftAction(element)) || null;
  return cachedGiftAction;
}

function findGiftForm() {
  // These stable controls uniquely identify the gift form. Resolve their
  // nearest shared container directly instead of scanning page candidates,
  // reading their text, and counting descendants on every queue poll.
  const recipient = document.getElementById("gift-chests-username");
  const quantity = document.getElementById("gift-chests-quantity");
  const picker = document.getElementById("setId-public-sets-picker");
  if (!recipient || !quantity || !picker || !visible(recipient) || !visible(quantity) || !visible(picker)) {
    cachedGiftForm = null;
    return null;
  }

  if (cachedGiftForm?.isConnected && visible(cachedGiftForm) &&
      cachedGiftForm.contains(recipient) && cachedGiftForm.contains(quantity) && cachedGiftForm.contains(picker))
    return cachedGiftForm;

  for (let ancestor = recipient.parentElement, depth = 0;
       ancestor && ancestor !== document.body && ancestor !== document.documentElement && depth < 16;
       ancestor = ancestor.parentElement, depth++) {
    if (ancestor.contains(quantity) && ancestor.contains(picker)) {
      cachedGiftForm = ancestor;
      return cachedGiftForm;
    }
  }
  cachedGiftForm = null;
  return null;
}

async function closeGiftForm(form) {
  const formRoot = findGiftForm() || form;
  if (!formRoot || !visible(formRoot)) return true;

  const hasCloseControl = element => [...element.querySelectorAll('button, [role="button"], [data-testid], [class*="close"], [class*="Close"]')].some(button => {
    const label = [
      button.getAttribute("aria-label"), button.getAttribute("title"), button.getAttribute("data-testid"),
      typeof button.className === "string" ? button.className : "", button.textContent
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim();
    return /^(?:close|dismiss|cerrar|×|✕|x)$/i.test(label) || /(?:^|[\s_-])(?:close|dismiss|cerrar)(?:$|[\s_-])/i.test(label);
  });

  let shell = formRoot;
  let ancestor = formRoot;
  for (let depth = 0; depth < 7 && ancestor.parentElement && ancestor.parentElement !== document.body; depth++) {
    ancestor = ancestor.parentElement;
    const classNames = typeof ancestor.className === "string" ? ancestor.className.toLowerCase() : "";
    const looksLikeDialog = ancestor.getAttribute("role") === "dialog" || ancestor.getAttribute("aria-modal") === "true" ||
      classNames.includes("modal") || classNames.includes("drawer") || classNames.includes("dialog");
    if (hasCloseControl(ancestor) || looksLikeDialog) {
      shell = ancestor;
      if (hasCloseControl(ancestor)) break;
    }
  }

  const closeButton = [...shell.querySelectorAll('button, [role="button"], [data-testid], [class*="close"], [class*="Close"]')].find(button => {
    if (!visible(button)) return false;
    const label = [
      button.getAttribute("aria-label"), button.getAttribute("title"), button.getAttribute("data-testid"),
      typeof button.className === "string" ? button.className : "", button.textContent
    ].filter(Boolean).join(" ").replace(/\s+/g, " ").trim();
    return /^(?:close|dismiss|cerrar|×|✕|x)$/i.test(label) || /(?:^|[\s_-])(?:close|dismiss|cerrar)(?:$|[\s_-])/i.test(label);
  });

  if (closeButton) closeButton.click();
  await waitFor(() => !findGiftForm() ? true : null, 1400).catch(() => null);
  if (!findGiftForm()) return true;

  for (const target of [shell, document, window]) {
    target.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
    target.dispatchEvent(new KeyboardEvent("keyup", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
  }
  await waitFor(() => !findGiftForm() ? true : null, 1400).catch(() => null);
  return !findGiftForm();
}

function visibleNotices() {
  const selector = '[role="alert"], [role="status"], [aria-live="polite"], [aria-live="assertive"], [data-sonner-toast], [class*="toast"], [class*="Toast"], [class*="notification"], [class*="Notification"]';
  return [...document.querySelectorAll(selector)]
    .filter(visible)
    .map(element => (element.innerText || element.textContent || "").replace(/\s+/g, " ").trim())
    .filter(Boolean);
}

const successMessagePattern = /\b(?:success(?:ful(?:ly)?)?\s+(?:sent|gifted|delivered)|(?:sent|gifted|delivered)\s+successfully|(?:gift|pack|chest)\s+(?:was\s+)?(?:sent|gifted|delivered)|(?:sent|gifted|delivered)\s+(?:the\s+)?(?:gift|pack|chest)|gift\s+(?:has\s+been\s+)?(?:sent|delivered)|pack\s+(?:has\s+been\s+)?sent|(?:regalo|cofre|pack)\s+(?:enviado|regalado|entregado)|(?:enviado|regalado|entregado)\s+(?:con\s+)?(?:éxito|exito)|se\s+ha\s+(?:enviado|regalado|entregado))\b/i;

function hasNewSuccessMessage(previousLines, previousNotices) {
  const notices = visibleNotices().filter(text => !previousNotices.has(text));
  const newLines = (document.body.innerText || "").split(/[\r\n]+/)
    .map(line => line.replace(/\s+/g, " ").trim())
    .filter(line => line && !previousLines.has(line));
  return [...notices, ...newLines].some(text => successMessagePattern.test(text));
}

function normalizeFeedText(value) {
  return String(value || "").replace(/\s+/g, " ").trim();
}

function normalizeCardName(value) {
  return normalizeFeedText(value)
    .replace(/\s*[·•⋅∙\-–—]\s*(?:just now|(?:about\s+)?(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+(?:seconds?|minutes?|hours?|days?|weeks?|months?|years?)\s+ago|hace\s+(?:un|una|uno|\d+)\s+(?:segundos?|minutos?|horas?|días?|dias?|semanas?|meses?|años?|anos?))\s*$/i, "")
    .replace(/\s+/g, " ")
    .trim();
}

function relativeAgeMilliseconds(value) {
  const text = normalizeFeedText(value).toLocaleLowerCase();
  if (/^(?:just now|less than a minute ago|ahora mismo|hace un momento|hace menos de un minuto)$/.test(text)) return 0;
  const spanishMatch = text.match(/^hace\s+(un|una|uno|\d+)\s+(segundos?|minutos?|horas?|días?|dias?|semanas?|meses?|años?|anos?)$/i);
  if (spanishMatch) {
    const amount = spanishMatch[1].match(/^\d+$/) ? Number(spanishMatch[1]) : 1;
    const unit = spanishMatch[2].toLowerCase();
    const unitMs = unit.startsWith("segundo") ? 1000 : unit.startsWith("minuto") ? 60_000
      : unit.startsWith("hora") ? 3_600_000 : unit.startsWith("día") || unit.startsWith("dia") ? 86_400_000
        : unit.startsWith("semana") ? 604_800_000 : unit.startsWith("mes") ? 2_592_000_000 : 31_536_000_000;
    return amount * unitMs;
  }
  const match = text.match(/^(a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+(seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|weeks?|months?|years?)\s+ago$/i);
  if (!match) return Number.POSITIVE_INFINITY;
  const amount = ({ a: 1, an: 1, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10 })[match[1].toLowerCase()] ?? Number(match[1]);
  const unit = match[2].toLowerCase();
  const unitMs = unit.startsWith("sec") ? 1000
    : unit.startsWith("min") ? 60_000
      : unit.startsWith("hour") || unit.startsWith("hr") ? 3_600_000
        : unit.startsWith("day") ? 86_400_000
          : unit.startsWith("week") ? 604_800_000
            : unit.startsWith("month") ? 2_592_000_000
              : 31_536_000_000;
  return amount * unitMs;
}

function isWithinCardAgeLimit(entry) {
  if (entry.playedAt) {
    const playedAt = Date.parse(entry.playedAt);
    if (Number.isFinite(playedAt)) return Date.now() - playedAt <= 10 * 60_000 && playedAt <= Date.now() + 60_000;
  }
  return relativeAgeMilliseconds(entry.relativeTime) <= 10 * 60_000;
}

function parseActivityFeedRow(element) {
  const text = normalizeFeedText(element.innerText || element.textContent);
  if (!text || text.length > 280) return null;
  const markerMatch = text.match(/\s+(has played|ha jugado|jugó|jugaba)\s+/i);
  if (!markerMatch || markerMatch.index <= 0) return null;
  const marker = markerMatch.index;
  const markerText = markerMatch[0];
  const redeemer = text.slice(0, marker).trim();
  let tail = text.slice(marker + markerText.length).trim();
  const age = tail.match(/\s*[·•⋅∙\-–—]\s*((?:just now|(?:about\s+)?(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+(?:seconds?|minutes?|hours?|days?|weeks?|months?|years?)\s+ago|hace\s+(?:un|una|uno|\d+)\s+(?:segundos?|minutos?|horas?|días?|dias?|semanas?|meses?|años?|anos?)))\s*$/i);
  const relativeTime = age?.[1]?.trim() || "";
  if (age) tail = tail.slice(0, age.index).trim();
  const cardName = tail.replace(/[·•]+$/g, "").trim();
  if (!redeemer || !cardName || cardName.length > 120) return null;
  // Generic data-id/data-key attributes can identify the card component rather
  // than an individual play. Trust only a redemption-specific ID or unique URL.
  const dataId = element.getAttribute("data-redemption-id") || "";
  const href = element.closest("a[href]")?.getAttribute("href") || "";
  const hrefHasUniqueId = /(?:[0-9a-f]{8}-[0-9a-f-]{27,}|\d{8,})/i.test(href);
  const stableId = dataId || (hrefHasUniqueId ? href : "");
  return { element, redeemer, cardName, relativeTime, eventId: stableId, stableIdentity: Boolean(stableId) };
}

function getActivityFeedRows() {
  const matches = [];
  // Keep fallback scans inside the activity surface instead of traversing the
  // entire creator page and its unrelated controls.
  const root = document.querySelector("main, [role='main']") || document.body;
  const nodes = root.querySelectorAll("article, li, button, a[href], [role='button']");
  for (const element of nodes) {
    if (!visible(element)) continue;
    const parsed = parseActivityFeedRow(element);
    if (parsed) matches.push(parsed);
  }
  // Streamloots often renders the same row text in several nested elements.
  // Keep the outer row for each physical entry, but preserve separate rows
  // even when user, card, and rounded relative time are identical.
  const entriesByElement = new Map(matches.map(entry => [entry.element, entry]));
  return matches.filter(entry => {
    const signature = `${entry.redeemer.toLocaleLowerCase()}|${entry.cardName}|${entry.relativeTime.toLocaleLowerCase()}`;
    for (let ancestor = entry.element.parentElement; ancestor && root.contains(ancestor); ancestor = ancestor.parentElement) {
      const parentEntry = entriesByElement.get(ancestor);
      if (!parentEntry) continue;
      const parentSignature = `${parentEntry.redeemer.toLocaleLowerCase()}|${parentEntry.cardName}|${parentEntry.relativeTime.toLocaleLowerCase()}`;
      if (parentSignature === signature) return false;
    }
    return true;
  });
}

function getSelectedCardDetails() {
  const titlePattern = /Redeemed card|Carta canjeada|Carta redimida|Carta jugada/i;
  const userPattern = /User information|Información del usuario|Informaci[oó]n de usuario/i;
  const candidates = [...document.querySelectorAll("aside, [role='dialog'], [class*='drawer'], [class*='Drawer'], [class*='panel'], [class*='Panel']")]
    .filter(visible)
    .map(element => ({ element, text: (element.innerText || "").trim() }))
    .filter(item => titlePattern.test(item.text) && userPattern.test(item.text));
  candidates.sort((a, b) => a.text.length - b.text.length);
  return candidates[0] || null;
}

function getActivityRowClickTarget(row) {
  if (row.matches("button, [role='button'], a[href]")) return row;
  return [...row.querySelectorAll("button, [role='button'], a[href]")].find(visible) || row;
}

function labeledValue(lines, labels, excludeAfterLabel = "") {
  const sectionEnd = excludeAfterLabel ? lines.findIndex(line => new RegExp(`^${excludeAfterLabel}\\s*:?$`, "i").test(line.trim())) : -1;
  for (let index = 0; index < lines.length; index++) {
    if (sectionEnd >= 0 && index > sectionEnd) continue;
    const line = lines[index].trim();
    for (const label of labels) {
      const match = line.match(new RegExp(`^${label}(?:\\s*:\\s*(.*))?\\s*$`, "i"));
      if (match) {
        if (match[1]?.trim()) return match[1].trim();
        let valueIndex = index + 1;
        let next = lines[valueIndex]?.trim() || "";
        if (next === ":") {
          valueIndex++;
          next = lines[valueIndex]?.trim() || "";
        } else if (next.startsWith(":")) {
          next = next.slice(1).trim();
          if (next) return next;
          valueIndex++;
          next = lines[valueIndex]?.trim() || "";
        }
        return next;
      }
    }
  }
  return "";
}

function extractCardInput(entry, details) {
  const detailText = details?.text || "";
  const lines = detailText.split(/[\r\n]+/).map(line => line.trim()).filter(Boolean);
  const inputLabels = [
    "User input", "Card input", "User message", "Custom message", "Text entered", "Input text",
    "Your input", "Your message", "Your answer", "Card answer", "Card response", "Text response",
    "Response", "Answer", "Recipient username", "Target username", "Recipient", "Input", "Text",
    "Nombre de usuario", "Usuario", "Nombre del usuario", "Destinatario", "Usuario destinatario", "Nombre del destinatario",
    "Mensaje", "Respuesta", "Texto", "Tu respuesta", "Tu mensaje", "Respuesta de la carta"
  ];
  const fromDetails = labeledValue(lines, inputLabels);
  if (fromDetails) return fromDetails;
  // Streamloots exposes the username entered for this card in the User
  // information block. Keep the value even when it matches the redeemer:
  // viewers can intentionally enter their own Streamloots username.
  const userInfoIndex = lines.findIndex(line => /^(?:User information|Información del usuario|Informaci[oó]n de usuario)\s*:?$/i.test(line));
  if (userInfoIndex >= 0) {
    const enteredUsername = labeledValue(lines.slice(userInfoIndex + 1), ["Username", "Nombre de usuario", "Usuario"]);
    if (enteredUsername) return enteredUsername;
  }
  const panel = details?.element;
  if (panel) {
    const dataField = [...panel.querySelectorAll(
      "[data-user-input], [data-card-input], [data-answer], [data-response], input:not([type='hidden']), textarea, [contenteditable='true']"
    )].find(element => visible(element));
    const value = normalizeFeedText(dataField?.value || dataField?.textContent || dataField?.getAttribute("data-user-input") || dataField?.getAttribute("data-card-input") || dataField?.getAttribute("data-answer") || dataField?.getAttribute("data-response") || "");
    if (value) return value;

    // Some feed versions render the submitted value as plain text in a labeled
    // row rather than an input element. Read a nearby small value for labels
    // used by those layouts.
    const labelNodes = [...panel.querySelectorAll("div, p, span, dt, dd, strong, b, label")]
      .filter(element => visible(element) && inputLabels.some(label => {
        const text = normalizeFeedText(element.innerText || element.textContent).replace(/:$/, "");
        return text.toLocaleLowerCase() === label.toLocaleLowerCase();
      }));
    for (const label of labelNodes) {
      const parent = label.parentElement;
      const siblings = parent ? [...parent.children].filter(child => child !== label) : [];
      const siblingValue = normalizeFeedText(siblings.map(child => child.innerText || child.textContent).join(" "));
      if (siblingValue) return siblingValue;
      const nextValue = normalizeFeedText(label.nextElementSibling?.innerText || label.nextElementSibling?.textContent || "");
      if (nextValue && nextValue.length <= 240) return nextValue;
    }
  }
  const rowField = entry.element.querySelector("[data-user-input], [data-card-input], input:not([type='hidden']), textarea, [contenteditable='true']");
  return normalizeFeedText(rowField?.value || rowField?.textContent || rowField?.getAttribute("data-user-input") || rowField?.getAttribute("data-card-input") || "");
}

async function readRedeemedCardDetails(entry) {
  if (entry.apiEvent) {
    return { redeemer: entry.redeemer, input: entry.input || "", playedAt: entry.playedAt, eventId: entry.deliveryId };
  }
  // Clear the previous details first. Repeated plays by the same viewer can
  // have identical card labels, so matching the old drawer text is not enough.
  const previousDetails = getSelectedCardDetails();
  if (previousDetails) {
    const closeButton = [...previousDetails.element.querySelectorAll("button, [role='button']")].find(button => {
      const label = `${button.getAttribute("aria-label") || ""} ${button.getAttribute("title") || ""} ${button.textContent || ""}`.trim();
      return /^(?:close|dismiss|cerrar|×|✕|x)$/i.test(label);
    });
    if (closeButton) closeButton.click();
    else {
      previousDetails.element.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
      document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", code: "Escape", bubbles: true, cancelable: true }));
    }
    const closeDeadline = Date.now() + 800;
    while (Date.now() < closeDeadline && getSelectedCardDetails()) await sleep(40);
    if (getSelectedCardDetails()) throw new Error("Could not close the previous card details before opening this redeem. It will retry automatically.");
  }
  // Always open this exact feed row after clearing any previous drawer.
  const clickTarget = getActivityRowClickTarget(entry.element);
  if (!visible(clickTarget)) throw new Error("The redeemed card row is no longer visible.");
  const rect = clickTarget.getBoundingClientRect();
  const point = {
    bubbles: true, cancelable: true, composed: true, view: window,
    button: 0, buttons: 1,
    clientX: rect.left + Math.max(1, rect.width / 2),
    clientY: rect.top + Math.max(1, rect.height / 2)
  };
  clickTarget.dispatchEvent(new PointerEvent("pointerdown", { ...point, pointerId: 1, pointerType: "mouse", isPrimary: true }));
  clickTarget.dispatchEvent(new MouseEvent("mousedown", point));
  clickTarget.dispatchEvent(new PointerEvent("pointerup", { ...point, pointerId: 1, pointerType: "mouse", isPrimary: true, buttons: 0 }));
  clickTarget.dispatchEvent(new MouseEvent("mouseup", { ...point, buttons: 0 }));
  clickTarget.dispatchEvent(new MouseEvent("click", { ...point, buttons: 0, detail: 1 }));
  let details = null;
  const deadline = Date.now() + 3500;
  while (Date.now() < deadline) {
    await sleep(50);
    details = getSelectedCardDetails();
    const detailHasTarget = details && details.text.toLocaleLowerCase().includes(entry.cardName.toLocaleLowerCase()) &&
      details.text.toLocaleLowerCase().includes(entry.redeemer.toLocaleLowerCase());
    if (detailHasTarget) break;
  }
  const detailHasTarget = details && details.text.toLocaleLowerCase().includes(entry.cardName.toLocaleLowerCase()) &&
    details.text.toLocaleLowerCase().includes(entry.redeemer.toLocaleLowerCase());
  if (!detailHasTarget) throw new Error(`Could not open details for “${entry.cardName}” redeemed by ${entry.redeemer}. It will retry automatically.`);
  const detailText = details?.text || "";
  const playedBy = detailText.match(/(?:Played by|Jugado por|Canjeado por)\s*:?\s*([^\r\n]+)/i)?.[1]?.trim();
  const playedAt = details.element.querySelector("time[datetime]")?.getAttribute("datetime") ||
    detailText.match(/(?:Played at|Jugado el|Jugado en|Canjeado el|Canjeado en|Fecha)\s*:?\s*([^\r\n]+)/i)?.[1]?.trim() || "";
  if (!playedAt) throw new Error("The card details did not include its play time. It will retry automatically.");
  const resolvedRedeemer = playedBy || entry.redeemer;
  const preciseTimestamp = /:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?$/i.test(playedAt);
  return {
    redeemer: resolvedRedeemer,
    input: extractCardInput(entry, details),
    playedAt,
    // The visible panel can round time to minutes. Don't merge separate rows
    // that share that rounded timestamp.
    eventId: preciseTimestamp
      ? `streamloots-play:${resolvedRedeemer.trim().toLocaleLowerCase()}|${normalizeFeedText(entry.cardName)}|${normalizeFeedText(playedAt).toLocaleLowerCase()}`
      : entry.deliveryId || `streamloots-play:${resolvedRedeemer.trim().toLocaleLowerCase()}|${normalizeFeedText(entry.cardName)}|${normalizeFeedText(playedAt).toLocaleLowerCase()}`
  };
}

async function sendActivityFeedCard(entry) {
  try {
    if (!isWithinCardAgeLimit(entry)) return;
    void chrome.runtime.sendMessage({
      type: "record-activity-feed-card",
      card: { card_name: entry.cardName, redeemer: entry.redeemer, detected_at: Date.now(), forwarded: false }
    }).catch(() => {});
    const details = await readRedeemedCardDetails(entry);
    if (!isWithinCardAgeLimit(entry)) return;
    if (!details.eventId) throw new Error("Skipped this card because its redemption timestamp could not be read safely.");
    const { processedStreamlootsCardIds = [] } = await chrome.storage.local.get("processedStreamlootsCardIds");
    if (processedStreamlootsCardIds.includes(details.eventId)) {
      console.info("[SLG Delivery] Already handled in this browser; skipping", { id: details.eventId, card: entry.cardName });
      return;
    }
    console.info("[SLG Delivery] Sending card event to the app", { id: details.eventId, card: entry.cardName, redeemer: details.redeemer, playedAt: details.playedAt });
    const result = await chrome.runtime.sendMessage({
      type: "streamloots-card-feed",
      payload: {
        event_id: details.eventId,
        card_name: entry.cardName,
        redeemer: details.redeemer,
        input: details.input,
        played_at: details.playedAt
      }
    });
    if (result?.error) throw new Error(result.error);
    if (result?.accepted === false) throw new Error(result.message || "The app did not accept this card event.");
    console.info("[SLG Delivery] App accepted the card event", { id: details.eventId, message: result?.message || "accepted" });
    await chrome.storage.local.set({
      processedStreamlootsCardIds: [...new Set([...processedStreamlootsCardIds, details.eventId])].slice(-2000)
    });
    void chrome.runtime.sendMessage({
      type: "record-activity-feed-card",
      card: { card_name: entry.cardName, redeemer: details.redeemer, detected_at: Date.now(), forwarded: true }
    }).catch(() => {});
    await recordPollStatus("");
  } catch (error) {
    console.error("[SLG Delivery] Card event was not delivered", error);
    throw error;
  }
}

function initializeActivityFeedWatcher() {
  if (!isActivityFeed) return;
  let baselineReady = false;
  let baselineTimer = null;
  let scanTimer = null;
  let apiPollSeen = false;
  let hookInstalledAt = 0;
  let fallbackNoticeLogged = false;
  let lastObservedCursor = null;
  let cursorCheckTimer = null;
  let processing = false;
  let observer = null;
  let feedPoll = null;
  let rowSequence = 0;
  const feedSession = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const seenRows = new WeakMap();
  const cursorEvents = new Map();
  const cursorTransitions = [];
  const eventStates = new Map();
  let cachedCardRuleNames = [];
  let cardRulesExpireAt = 0;
  let cardRulesRequest = null;
  const debug = (...values) => console.info("[SLG Watcher]", ...values);
  debug("Started on Activity Feed", { page: location.pathname, cursorCheckSeconds: 10 });
  const getCardRuleNames = () => {
    if (Date.now() < cardRulesExpireAt) return Promise.resolve(cachedCardRuleNames);
    if (cardRulesRequest) return cardRulesRequest;
    cardRulesRequest = chrome.runtime.sendMessage({ type: "get-streamloots-card-rules" })
      .then(result => {
        if (result?.error) throw new Error(result.error);
        cachedCardRuleNames = result?.card_names || [];
        cardRulesExpireAt = Date.now() + 3000;
        return cachedCardRuleNames;
      })
      .finally(() => { cardRulesRequest = null; });
    return cardRulesRequest;
  };
  const markFeedReady = () => {
    if (activityFeedReady) return;
    activityFeedReady = true;
    debug("Activity Feed content script is ready.");
    void chrome.runtime.sendMessage({ type: "record-activity-feed-status", ready: true }).catch(() => {});
  };
  const queueEvent = (entry, deliveryId) => {
    if (entry.stableIdentity && hasSeenActivityCard(entry.eventId)) return;
    if (eventStates.has(deliveryId)) {
      debug("Event already in the local queue", { id: deliveryId });
      return;
    }
    entry.deliveryId = deliveryId;
    eventStates.set(deliveryId, { entry, attempts: 0, nextAttemptAt: 0, done: false, detectedAt: Date.now() });
    debug("Queued event for rule check", { id: entry.eventId || deliveryId, card: entry.cardName, redeemer: entry.redeemer, playedAt: entry.playedAt || "unknown" });
  };
  const entryFromApiEvent = event => {
    const fields = Array.isArray(event.cardFields) ? event.cardFields : [];
    const inputField = fields.find(field =>
      /username|recipient|user|input|message|answer/i.test(`${field.label || ""} ${field.name || ""}`) && normalizeFeedText(field.value)
    );
    return {
      element: null,
      eventId: String(event.id),
      stableIdentity: true,
      apiEvent: true,
      cardName: String(event.cardName),
      redeemer: String(event.username || event.userName || event.user?.username || event.user?.userName || ""),
      input: normalizeFeedText(inputField?.value || ""),
      playedAt: String(event.createdAt),
      relativeTime: ""
    };
  };
  const acceptCursorPoll = message => {
    if (!apiPollSeen) {
      apiPollSeen = true;
      observer?.disconnect();
      if (feedPoll) clearInterval(feedPoll);
      debug("API event polling is active; disabled DOM fallback observation.");
    }
    markFeedReady();
    const cursor = String(message.cursor || "");
    if (!cursor) return;
    if (!cursorEvents.has(cursor)) cursorEvents.set(cursor, new Map());
    const bucket = cursorEvents.get(cursor);
    for (const card of Array.isArray(message.cards) ? message.cards : []) {
      if (!card?.id || !card?.createdAt || !card?.cardName) continue;
      bucket.set(String(card.id), card);
    }
    debug("Received events poll", { cursor, cardsInResponse: message.cards?.length || 0, cardsBufferedForCursor: bucket.size, cardNames: [...bucket.values()].map(card => card.cardName), responseSummary: message.responseSummary || null });
    if (lastObservedCursor === null) {
      lastObservedCursor = cursor;
      debug("Initial cursor recorded", { cursor });
    } else if (cursor !== lastObservedCursor) {
      debug("Cursor changed", { previous: lastObservedCursor, current: cursor });
      cursorTransitions.push({ previous: lastObservedCursor, current: cursor });
      lastObservedCursor = cursor;
      checkCursor("cursor changed in poll response");
    }
    while (cursorEvents.size > 8) cursorEvents.delete(cursorEvents.keys().next().value);
  };
  const handleHookMessage = messageEvent => {
    if (messageEvent.source !== window || messageEvent.origin !== location.origin || messageEvent.data?.source !== "streamloots-gifter-activity-feed") return;
    const message = messageEvent.data;
    if (message.kind === "ready") {
      hookInstalledAt = Date.now();
      debug("Page network monitor connected; waiting for an events poll.");
      markFeedReady();
      return;
    }
    if (message.kind === "event-poll") acceptCursorPoll(message);
  };
  window.addEventListener("message", handleHookMessage);
  window.postMessage({ source: "streamloots-gifter-activity-feed", kind: "ping" }, location.origin);
  const processEvent = async state => {
    const entry = state.entry;
    if (!isWithinCardAgeLimit(entry)) {
      state.done = true;
      return;
    }
    const ruleNames = await getCardRuleNames();
    const matchesRule = ruleNames.some(name => normalizeCardName(name) === normalizeCardName(entry.cardName));
    debug("Checked card against configured rules", { card: entry.cardName, matchesRule, configuredRuleCount: ruleNames.length, id: entry.eventId || entry.deliveryId });
    if (!matchesRule) {
      void chrome.runtime.sendMessage({
        type: "record-activity-feed-card",
        card: { card_name: entry.cardName, redeemer: entry.redeemer, detected_at: Date.now(), forwarded: true, ignored: true }
      }).catch(() => {});
      state.done = true;
      return;
    }
    await sendActivityFeedCard(entry);
    state.done = true;
  };
  const pumpQueue = async () => {
    if (processing) return;
    processing = true;
    let processedAny = false;
    try {
      while (true) {
        const now = Date.now();
        let state = null;
        for (const candidate of eventStates.values()) {
          if (now - candidate.detectedAt > seenActivityCardTtlMs) {
            debug("Dropping an event that exceeded the replay window", {
              id: candidate.entry.eventId || candidate.entry.deliveryId,
              card: candidate.entry.cardName
            });
            if (candidate.entry.stableIdentity) rememberActivityCard(candidate.entry.eventId, now);
            eventStates.delete(candidate.entry.deliveryId);
            continue;
          }
          if (candidate.nextAttemptAt <= now) {
            state = candidate;
            break;
          }
        }
        if (!state) break;
        try {
          await processEvent(state);
          processedAny = true;
          if (state.done) {
            if (state.entry.stableIdentity) rememberActivityCard(state.entry.eventId);
            eventStates.delete(state.entry.deliveryId);
          }
        } catch (error) {
          state.attempts++;
          state.nextAttemptAt = Date.now() + Math.min(15000, 1000 * (2 ** Math.min(state.attempts - 1, 4)));
          const attemptText = `retry ${state.attempts}`;
          await recordPollStatus(`Activity Feed: ${error.message || "Could not process card."} (${attemptText})`);
          debug("Card processing failed and will retry", { card: state.entry.cardName, id: state.entry.eventId || state.entry.deliveryId, attempt: state.attempts, error: error.message || String(error) });
          // Keep draining the queue so one unreadable card cannot hold every
          // later redeem behind its backoff timer.
        }
      }
      if (processedAny && eventStates.size === 0) await recordPollStatus("");
    } finally {
      processing = false;
    }
  };
  const checkCursor = (reason = "10-second timer") => {
    if (cursorTransitions.length === 0) {
      debug(`${reason}: no cursor change since the last response`, { cursor: lastObservedCursor || "not observed yet", bufferedCards: lastObservedCursor ? cursorEvents.get(lastObservedCursor)?.size || 0 : 0 });
      void pumpQueue();
      return;
    }
    while (cursorTransitions.length) {
      const { previous, current } = cursorTransitions.shift();
      debug(`Processing cursor transition (${reason})`, { previous, current, previousResponseCards: cursorEvents.get(previous)?.size || 0, currentResponseCards: cursorEvents.get(current)?.size || 0 });
      let candidatesFound = 0;
      // The event can be returned with the request using the old cursor or the
      // first request using the new cursor. Check both snapshots, deduping by
      // Streamloots' immutable event ID.
      for (const cursor of new Set([previous, current])) {
        const bucket = cursorEvents.get(cursor);
        if (!bucket) continue;
        for (const event of bucket.values()) {
          const pageSlug = location.pathname.split("/").filter(Boolean)[0] || "";
          if (event.pageSlug && event.pageSlug.toLocaleLowerCase() !== pageSlug.toLocaleLowerCase()) continue;
          const entry = entryFromApiEvent(event);
          candidatesFound++;
          if (!isWithinCardAgeLimit(entry)) {
            debug("Ignoring event older than the 10-minute window", { id: entry.eventId, card: entry.cardName, playedAt: entry.playedAt });
            rememberActivityCard(entry.eventId);
            continue;
          }
          if (hasSeenActivityCard(entry.eventId)) continue;
          queueEvent(entry, `streamloots-api:${entry.eventId}`);
        }
      }
      if (candidatesFound === 0)
        debug("Cursor changed, but neither response contained a CollectionCard/Redeemed event", { previous, current });
      if (previous !== current) cursorEvents.delete(previous);
    }
    void pumpQueue();
  };
  const scanDomFallback = () => {
    if (!baselineReady || apiPollSeen || (hookInstalledAt && Date.now() - hookInstalledAt < 20_000)) return;
    if (hookInstalledAt && !apiPollSeen && !fallbackNoticeLogged) {
      fallbackNoticeLogged = true;
      debug("No matching API poll observed after 20 seconds; using the Activity Feed DOM fallback.");
    }
    for (const entry of getActivityFeedRows()) {
      if (!isWithinCardAgeLimit(entry)) continue;
      const signature = `${entry.redeemer.toLocaleLowerCase()}|${entry.cardName}|${entry.relativeTime.toLocaleLowerCase()}`;
      if (seenRows.get(entry.element) === signature) continue;
      seenRows.set(entry.element, signature);
      entry.stableIdentity = false;
      entry.eventId = "";
      queueEvent(entry, `streamloots-dom:${feedSession}:${++rowSequence}`);
    }
    void pumpQueue();
  };
  const scan = () => {
    if (apiPollSeen) return;
    scanDomFallback();
  };
  const finishBaseline = () => {
    if (baselineReady) return;
    for (const entry of getActivityFeedRows()) {
      const signature = `${entry.redeemer.toLocaleLowerCase()}|${entry.cardName}|${entry.relativeTime.toLocaleLowerCase()}`;
      seenRows.set(entry.element, signature);
    }
    baselineReady = true;
    markFeedReady();
  };
  const scheduleBaseline = () => {
    if (baselineReady || getActivityFeedRows().length === 0) return;
    if (baselineTimer) clearTimeout(baselineTimer);
    baselineTimer = setTimeout(finishBaseline, 3000);
  };
  const startBaseline = () => {
    if (!baselineTimer && !baselineReady) scheduleBaseline();
  };
  const observerRoot = document.querySelector("main, [role='main']") || document.body;
  observer = new MutationObserver(records => {
    if (apiPollSeen) return;
    startBaseline();
    const relevantChange = records.some(record =>
      (record.type === "childList" && record.addedNodes.length > 0) || record.type === "characterData");
    if (!relevantChange) return;
    if (!baselineReady) scheduleBaseline();
    else if (!scanTimer) scanTimer = setTimeout(() => { scanTimer = null; scan(); }, 150);
  });
  observer.observe(observerRoot, { childList: true, subtree: true, characterData: true });
  startBaseline();
  const baselinePoll = setInterval(() => {
    if (baselineReady) clearInterval(baselinePoll);
    else startBaseline();
  }, 750);
  feedPoll = setInterval(scan, 3000);
  cursorCheckTimer = setInterval(() => checkCursor("10-second timer"), 10_000);
  window.addEventListener("pagehide", () => {
    clearInterval(baselinePoll);
    if (feedPoll) clearInterval(feedPoll);
    clearInterval(cursorCheckTimer);
    if (scanTimer) clearTimeout(scanTimer);
    window.removeEventListener("message", handleHookMessage);
  }, { once: true });
  void chrome.runtime.sendMessage({ type: "record-activity-feed-status", ready: false }).catch(() => {});
}

async function openGiftForm() {
  let form = findGiftForm();
  if (form) return form;

  const language = await getStreamlootsLanguage();
  const menuButton = findGiftAction();
  if (!menuButton) {
    throw new Error(language === "Spanish"
      ? "Abre la página de creador de Streamloots y asegúrate de que esté disponible la opción «Regalar cofres» (o «Regalar packs»)."
      : "Open the Streamloots creator page and make sure Gift packs is available.");
  }
  menuButton.click();
  form = await waitFor(findGiftForm);
  return form;
}

function canOpenGiftForm() {
  return Boolean(findGiftForm() || findGiftAction());
}

function setInputValue(input, value) {
  const setter = Object.getOwnPropertyDescriptor(
    Object.getPrototypeOf(input), "value"
  )?.set;
  if (setter) setter.call(input, value);
  else input.value = value;
  input.dispatchEvent(new Event("input", { bubbles: true }));
  input.dispatchEvent(new Event("change", { bubbles: true }));
}

async function chooseCollection(form, collection) {
  const picker = form.querySelector("#setId-public-sets-picker") || document.getElementById("setId-public-sets-picker");
  if (!picker) throw new Error("Could not find the collection selector.");
  const normalize = value => (value || "").normalize("NFKC").replace(/[’‘]/g, "'").replace(/\s+/g, " ").trim().toLocaleLowerCase();
  const expected = normalize(collection);
  const nativeSelect = picker.matches("select") ? picker : picker.querySelector("select");
  if (nativeSelect) {
    const nativeOption = [...nativeSelect.options].find(option => normalize(option.textContent) === expected);
    if (!nativeOption) {
      const options = [...nativeSelect.options].map(option => option.textContent.trim()).filter(Boolean);
      throw new Error(`Collection “${collection}” is not available in the selector. Choices: ${options.slice(0, 15).join(" | ") || "none"}`);
    }
    nativeSelect.value = nativeOption.value;
    nativeSelect.dispatchEvent(new Event("input", { bubbles: true }));
    nativeSelect.dispatchEvent(new Event("change", { bubbles: true }));
    await sleep(150);
    if (normalize(nativeSelect.selectedOptions[0]?.textContent) !== expected) {
      throw new Error(`Could not select collection “${collection}”.`);
    }
    return;
  }
  const selectedLines = () => (picker.innerText || picker.textContent || "")
    .split(/[\r\n]+/).map(normalize).filter(Boolean);
  const isSelected = () => selectedLines().some(line => line === expected);
  if (isSelected()) return;

  const menuRootSelector = [
    '[role="listbox"]', '[role="menu"]', '[role="dialog"]', '[data-radix-popper-content-wrapper]',
    '[class*="popover"]', '[class*="Popover"]', '[class*="dropdown"]', '[class*="Dropdown"]',
    '[class*="menu"]', '[class*="Menu"]'
  ].join(",");
  const menuItemSelector = [
    '[role="option"]', '[role="menuitem"]', '[role="radio"]', "button", "li", "[tabindex]",
    '[class*="option"]', '[class*="Option"]', '[class*="item"]', '[class*="Item"]'
  ].join(",");
  const portalRoots = new Set();
  const addMenuRootsFrom = node => {
    if (!(node instanceof Element)) return;
    const consider = root => {
      const lines = (root.innerText || root.textContent || "").split(/[\r\n]+/).map(normalize).filter(Boolean);
      if (lines.includes(expected)) portalRoots.add(root);
    };
    if (node.matches(menuRootSelector)) consider(node);
    for (const root of node.querySelectorAll(menuRootSelector)) consider(root);
  };
  const hasExpectedChoice = root => (root.innerText || root.textContent || "")
    .split(/[\r\n]+/).map(normalize).includes(expected);
  const getMenuRoots = () => {
    const controlledIds = `${picker.getAttribute("aria-controls") || ""} ${picker.getAttribute("aria-owns") || ""}`
      .split(/\s+/).filter(Boolean);
    const controlled = controlledIds.map(id => document.getElementById(id)).filter(root => root && visible(root));
    if (controlled.length) return controlled;

    const localRoots = [...form.querySelectorAll(menuRootSelector)]
      .filter(root => visible(root) && root !== picker && !picker.contains(root) && hasExpectedChoice(root));
    if (localRoots.length) return localRoots;

    // Some Streamloots menus render in a portal outside the form. A short lived
    // observer records only menu surfaces added or revealed after this picker
    // opens; it avoids searching the whole page for menu text.
    const pickerRect = picker.getBoundingClientRect();
    const nearbyPortalRoots = [...portalRoots].filter(root => root.isConnected && visible(root) && root !== picker && !picker.contains(root) && hasExpectedChoice(root));
    nearbyPortalRoots.sort((left, right) => {
      const distance = root => {
        const rect = root.getBoundingClientRect();
        const dx = Math.max(rect.left - pickerRect.right, pickerRect.left - rect.right, 0);
        const dy = Math.max(rect.top - pickerRect.bottom, pickerRect.top - rect.bottom, 0);
        return dx * dx + dy * dy;
      };
      return distance(left) - distance(right);
    });
    return nearbyPortalRoots.slice(0, 1);
  };
  const candidateItems = () => {
    let roots = getMenuRoots();
    if (!roots.length) {
      const localRoot = picker.parentElement?.parentElement;
      if (localRoot && visible(localRoot)) roots = [localRoot];
    }
    return roots.flatMap(root => [...root.querySelectorAll(menuItemSelector)]
      .filter(element => visible(element) && !picker.contains(element)));
  };
  const previouslyVisible = new Set(candidateItems());
  const portalObserver = new MutationObserver(records => {
    for (const record of records) {
      if (record.type === "childList") {
        for (const node of record.addedNodes) addMenuRootsFrom(node);
      }
      const changedElement = record.target instanceof Element ? record.target : record.target.parentElement;
      const changedRoot = changedElement?.closest(menuRootSelector);
      if (changedRoot) addMenuRootsFrom(changedRoot);
    }
  });
  portalObserver.observe(document.body, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: ["class", "role", "hidden", "aria-hidden", "style"]
  });
  picker.click();
  let option = null;
  try {
    option = await waitFor(() => {
      const newlyVisible = candidateItems().filter(element => !previouslyVisible.has(element));
      const matches = newlyVisible.filter(element => {
        const lines = (element.innerText || element.textContent || "").split(/[\r\n]+/).map(normalize).filter(Boolean);
        return lines.includes(expected) || normalize(element.getAttribute("aria-label")) === expected;
      });
      const depth = element => {
        let count = 0;
        for (let current = element; current; current = current.parentElement) count++;
        return count;
      };
      return matches.sort((a, b) => depth(b) - depth(a))[0] || null;
    }, 7000).catch(() => null);
  } finally {
    portalObserver.disconnect();
  }
  if (!option) {
    const visibleOptions = candidateItems()
      .map(element => (element.innerText || element.textContent || "").replace(/\s+/g, " ").trim())
      .filter(Boolean);
    throw new Error(`Could not find collection “${collection}” in the open selector. Visible choices: ${visibleOptions.slice(0, 12).join(" | ") || "none detected"}`);
  }
  activateMenuChoice(option);
  await sleep(250);
  if (!isSelected()) {
    option.focus?.();
    option.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", code: "Enter", bubbles: true, cancelable: true }));
    option.dispatchEvent(new KeyboardEvent("keyup", { key: "Enter", code: "Enter", bubbles: true, cancelable: true }));
  }
  await waitFor(() => isSelected() ? true : null, 5000)
    .catch(() => { throw new Error(`The selector did not confirm collection “${collection}”. Current selection: “${(picker.innerText || picker.textContent || "").replace(/\s+/g, " ").trim()}”. Clicked choice: <${option.tagName.toLowerCase()}> role=${option.getAttribute("role") || "none"} class=${String(option.className || "").slice(0, 100)}.`); });
}

async function sendGift(job) {
  const form = await openGiftForm();
  await chooseCollection(form, job.collection);

  const recipient = document.getElementById("gift-chests-username");
  const quantity = document.getElementById("gift-chests-quantity");
  if (!recipient || !quantity) throw new Error("Could not find gift recipient or pack count fields.");

  setInputValue(recipient, job.recipient);
  setInputValue(quantity, String(job.packs || 1));

  const cards = Math.min(3, Math.max(1, Number(job.cards) || 3));
  const cardChoice = document.getElementById(`giftChest-card-number-number-of-cards-${cards}`);
  if (!cardChoice) throw new Error(`Could not select ${cards} cards per pack.`);
  cardChoice.click();

  await sleep(300);
  if (recipient.value.trim() !== job.recipient || quantity.value !== String(job.packs || 1)) {
    throw new Error("Streamloots did not accept the recipient or pack count.");
  }

  const language = await getStreamlootsLanguage();
  let sendButton = exactLocalizedAction(form, "button, [role='button']",
    ["Send pack"], ["Enviar pack", "Enviar cofre", "Enviar", "Regalar"], language);
  if (!sendButton) {
    // Streamloots has used translated CTA copy; the form's submit control is
    // stable even when its visible label differs. Restrict fallback to submit.
    sendButton = form.querySelector("button[type='submit'], [role='dialog'] button[type='submit']");
  }
  if (!sendButton || sendButton.disabled) throw new Error(language === "Spanish" ? "El botón para enviar el regalo no está disponible." : "The Send pack button is unavailable.");
  const previousLines = new Set((document.body.innerText || "").split(/[\r\n]+/).map(line => line.replace(/\s+/g, " ").trim()).filter(Boolean));
  const previousNotices = new Set(visibleNotices());
  sendButton.click();

  const outcome = await waitFor(() => {
    if (!findGiftForm()) return "closed";
    if (hasNewSuccessMessage(previousLines, previousNotices)) return "confirmed";
    const updatedRecipient = document.getElementById("gift-chests-username");
    if (updatedRecipient && !updatedRecipient.value.trim()) return "form-reset";
    return null;
  }, 12000).catch(() => null);
  if (outcome === "closed") return { formClosed: true };

  const visibleText = findGiftForm()?.innerText || document.body.innerText || "";
  const notFound = /\b(?:user|username|recipient)\b.{0,60}\bnot found\b|\bnot found\b.{0,60}\b(?:user|username|recipient)\b|\b(?:user|username)\b.{0,40}\b(?:does not exist|doesn't exist|could not be found)\b|(?:usuario|nombre de usuario|destinatario).{0,60}(?:no encontrado|no existe|no se encontr[oó])|(?:no encontrado|no existe|no se encontr[oó]).{0,60}(?:usuario|destinatario)/i.test(visibleText);
  const relevantError = visibleText.match(/(?:limit|not found|invalid|unable|error|l[ií]mite|no encontrado|no existe|inv[aá]lido|no se pudo|error)[^\n]*/i)?.[0];
  const successful = outcome === "confirmed" || (outcome === "form-reset" && !relevantError);
  const formClosed = await closeGiftForm(form);
  if (notFound) {
    const error = new Error("Streamloots username was not found.");
    error.code = "not_found";
    throw error;
  }
  if (successful) return { formClosed };
  throw new Error(
    relevantError
      ? `Streamloots did not confirm the gift: ${relevantError}`
      : "Gift submission is uncertain. Check Streamloots before retrying."
  );
}

async function poll() {
  if (isWorking || !location.hostname.endsWith("streamloots.com")) return;
  // Do not claim a queue job on an ordinary Streamloots page. Leave it queued
  // until a page with the Gift packs action (including Activity Feed) is open.
  if (!canOpenGiftForm()) return;
  isWorking = true;
  try {
    const response = await chrome.runtime.sendMessage({ type: "claim-gift" });
    if (response?.error) {
      await recordPollStatus(response.error);
      return;
    }
    const job = response?.job;
    if (!job) {
      await recordPollStatus();
      return;
    }

    try {
      const giftResult = await sendGift(job);
      const result = await chrome.runtime.sendMessage({
        type: "complete-gift",
        payload: {
          job_id: job.job_id,
          result: "sent",
          detail: giftResult?.formClosed ? "Gift sent and gift form closed." : "Gift sent, but Streamloots kept the gift form open."
        }
      });
      await recordPollStatus(result?.error || "");
    } catch (error) {
      const result = await chrome.runtime.sendMessage({
        type: "complete-gift",
        payload: {
          job_id: job.job_id,
          result: error.code === "not_found" ? "not_found" : "needs_attention",
          detail: error.message
        }
      });
      await recordPollStatus(result?.error || error.message);
    }
  } catch (error) {
    await recordPollStatus(error.message || "Could not contact the local app.");
  } finally {
    isWorking = false;
  }
}

setInterval(poll, 2000);
initializeActivityFeedWatcher();
recordPollStatus();
poll();
