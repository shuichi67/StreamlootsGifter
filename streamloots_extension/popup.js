const statusDot = document.getElementById("status-dot");
const statusTitle = document.getElementById("status-title");
const statusDetail = document.getElementById("status-detail");
const toast = document.getElementById("toast");
const manualKeyInput = document.getElementById("manual-key");
const manualConnectButton = document.getElementById("connect-manual");
const disconnectButton = document.getElementById("disconnect");
const rotateButton = document.getElementById("rotate");

function setStatus(title, detail, state = "waiting") {
  statusTitle.textContent = title;
  statusDetail.textContent = detail;
  statusDot.className = `dot ${state === "connected" ? "connected" : state === "error" ? "error" : ""}`;
}

async function send(type, payload = {}) {
  const result = await chrome.runtime.sendMessage({ type, ...payload });
  if (!result) throw new Error("The extension could not reach its background service.");
  if (result.error) throw new Error(result.error);
  return result;
}

async function copyKey(token) {
  try {
    await navigator.clipboard.writeText(token);
    toast.textContent = "Connected. Bridge key copied to clipboard.";
  } catch {
    toast.textContent = "Connected. The key is saved in the extension.";
  }
}

async function refreshStatus() {
  try {
    const result = await send("bridge-status");
    const lastCard = result.activityFeedLastCard;
    const lastCardDetail = lastCard
      ? `Last seen: ${lastCard.redeemer || "viewer"} · ${lastCard.card_name || "card"}${lastCard.ignored ? " (no rule; ignored)" : lastCard.forwarded ? " (handled by app)" : " (not yet sent)"}.`
      : "No new card has been seen yet. Make a new test redeem while this feed stays open.";
    disconnectButton.disabled = !result.connected && result.reason === "disconnected";
    rotateButton.disabled = !result.connected;
    if (result.connected) {
      if (result.activityFeedOpen && !result.activityFeedReady) {
        setStatus("Reload the Activity Feed tab", "The extension was updated. Refresh the feed page to start card detection.");
      } else if (result.contentScriptReady && result.gifterLastError) {
        setStatus("Gifter needs attention", result.gifterLastError, "error");
      } else if (result.activityFeedReady && result.giftPageReady) {
        setStatus("Watching Activity Feed · gifts ready", `${lastCardDetail} This tab can also process queued gifts.`, "connected");
      } else if (result.activityFeedReady) {
        setStatus("Watching Activity Feed", `${lastCardDetail} Keep a Streamloots page with the Gift packs option open to process queued gifts.`, "connected");
      } else if (result.contentScriptReady) {
        setStatus("Creator page connected", "Keep the Streamloots Activity Feed open in this browser to detect card redeems.", "connected");
      } else if (result.streamlootsOpen) {
        setStatus("Reload the Streamloots tab", "The extension was updated. Refresh the page to start its gifter.");
      } else {
        setStatus("App connected · waiting for Streamloots", "Open or refresh the Streamloots creator page to process queued gifts.");
      }
    } else if (result.reason === "disconnected") {
      setStatus("Not connected", "Get a key from the running Streamloots Gifter app.");
    } else if (result.reason === "key-invalid") {
      setStatus("Key needs refreshing", "Copy the current Bridge key from the app and connect again.", "error");
      disconnectButton.disabled = false;
    } else {
      setStatus("App not reachable", "Open Streamloots Gifter, then try again.", "error");
      disconnectButton.disabled = false;
    }
  } catch (error) {
    setStatus("Connection check failed", error.message, "error");
  }
}

setInterval(refreshStatus, 2500);

async function runButton(button, action) {
  button.disabled = true;
  toast.textContent = "";
  try { await action(); }
  catch (error) { setStatus("Could not connect", error.message, "error"); }
  finally { await refreshStatus(); }
}

manualConnectButton.addEventListener("click", () => runButton(manualConnectButton, async () => {
  const key = manualKeyInput.value.trim();
  if (!key) throw new Error("Paste the Bridge key copied from Streamloots Gifter.");
  setStatus("Checking key…", "Validating the key with the local app.");
  await send("connect-with-key", { token: key });
  toast.textContent = "Connected with the entered Bridge key.";
  manualKeyInput.value = "";
}));

disconnectButton.addEventListener("click", () => runButton(disconnectButton, async () => {
  await send("disconnect-bridge");
  toast.textContent = "Extension disconnected. The saved key was removed.";
}));

rotateButton.addEventListener("click", () => runButton(rotateButton, async () => {
  if (!window.confirm("Generate a new Bridge key? Other extensions using the current key will disconnect.")) return;
  setStatus("Generating a new key…", "Replacing the current Bridge key.");
  const { token } = await send("rotate-bridge-key");
  await copyKey(token);
}));

refreshStatus();
