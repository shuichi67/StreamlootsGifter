# Local free gifting setup

This project can send free packs through the Streamloots creator page using a local HTTP queue and a Chromium extension. It does not use Loot's Tools or a private Streamloots API.

## Setup

1. Start `main_compact.py`. It starts a bridge bound only to `127.0.0.1:8765` and shows a bridge key in the app.
2. In Brave, open `brave://extensions`, enable Developer mode, and choose **Load unpacked**.
3. Select this project's `streamloots_extension` folder.
4. Open the extension's **Details → Extension options**, paste the key shown by the app, and save it.
5. Open the logged-in Streamloots creator page and reload it. Keep that page open while the app is running.
6. In the Twitch reward settings, require text input for the viewer's Streamloots username. In the app, map that reward to a Streamloots collection.

## Free Chest Hour

1. Log in to Twitch again after the app requests the additional chat-reading permission.
2. In the **Free Chest Hour** section, add rules with a unique keyword, collection name, pack count, and cards per pack.
3. Announce the keywords, then click **Start 5-minute window**. The app shows the time remaining; **Cancel window** closes it early.
4. A chat message must consist of the exact keyword (case-insensitive). Each Twitch account can claim once during that window, even if it posts multiple configured keywords.

The chat sender's Twitch login is used as the Streamloots recipient for this mode. Viewers need matching usernames on both services for the gift to reach the intended account.

Each matching redemption queues one pack with three cards. The extension fills the recipient, collection, pack count, and card count, then submits the creator's free **Gift packs** form. Missing usernames or collection mappings are not submitted.

## Safeguards and limits

- The local server listens only on loopback and requires the per-install bridge key.
- Redemption IDs are deduplicated. A job is claimed once and is not automatically retried after an uncertain result, to avoid duplicate gifts.
- Queue state and the bridge key are stored locally in `gift_queue.json` and `.streamloots_bridge_key`; both are ignored by Git.
- Keep the creator page open in a supported Chromium browser with the extension enabled. The extension only acts on Streamloots pages.
