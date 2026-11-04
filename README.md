# Streamloots Gifter

**A Windows desktop app for automating Streamloots gifts triggered by Twitch channel point redeems and Streamloots card redeems.**

[Download the latest release](https://github.com/shuichi67/StreamlootsGifter/releases/latest) · [View all releases](https://github.com/shuichi67/StreamlootsGifter/releases)

Streamloots Gifter connects Twitch redeems and Streamloots card activity to configured gifts. It runs on the streamer’s PC and pairs with a browser extension to monitor the Streamloots Activity Feed and carry out gifts.

> This project is evolving. Features and setup may change, and more improvements may be added in future releases.

<!--
To add a screenshot, upload it to docs/images/dashboard.png,
then uncomment and update this line:
![Streamloots Gifter dashboard](docs/images/dashboard.png)
-->

## Features

- Map Twitch channel point rewards to a Streamloots collection and choose the chest and card amounts.
- Configure Streamloots card redeems to gift to the redeemer or to a recipient entered on the card.
- Set viewer aliases, such as mapping to a viewers nickname to their full Streamloots username.
- Review redemption activity and claim history, filter entries, and retry gifts that failed.
- Customize the dashboard layout and choose a theme.
- Pair the desktop app with the browser extension through a local Bridge connection.
- Check for and install app updates from Settings.
- The current build also retains the optional Free Chest Hour feature.
- Choose English or Spanish Streamloots labels in Settings; the browser extension recognizes both languages.

## Publishing updates

The in-app updater requires both the GitHub SHA-256 digest and a publisher signature. The release builder uses the ignored local file `.update-signing-private.pem` to sign the update ZIP. Keep that private key backed up securely and never commit or upload it. Upload both `StreamlootsGifter-Update-...zip` and its matching `.zip.sig` file to the same GitHub release. The app has the corresponding public key pinned in its updater and refuses unsigned packages.

## What you need

- Windows 10 or 11, 64-bit.
- A Twitch account for the streamer.
- A Streamloots creator account with access to the relevant collections and gifting controls.
- Google Chrome (recommended) or other Chromium-based browsers.
- A Twitch account for the streamer to authorize through Twitch’s official sign-in page.

## First-time setup

### 1. Download and extract the app

1. Open the [latest release](https://github.com/shuichi67/StreamlootsGifter/releases/latest).
2. Download `StreamlootsGifter-Windows-x64-...zip`.
3. Extract the ZIP to a folder the streamer can write to, such as `Documents\Streamloots Gifter`.
4. Keep the extracted folder in place. The app stores its local settings and history alongside the application.

### 2. Sign in to Twitch

Start `StreamlootsGifter.exe` and click **Continue with Twitch**. The app opens Twitch’s official activation page and shows a one-time code. Sign in on `twitch.tv` if asked, enter that code, and approve the requested access. Return to the app; it completes the connection automatically. You never enter your Twitch password or a Client Secret into Streamloots Gifter. On later launches, the app restores the saved session automatically.

The app saves the Twitch session encrypted for the current Windows account. Twitch may require a new sign-in after 30 days without use or if access is revoked.

### 3. Install the browser extension

1. In Chrome, open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked**.
4. Select the `streamloots_extension` folder inside the extracted app folder.
5. In the app’s Streamloots Bridge widget, choose **Copy key**. Open the extension popup, paste the key, and choose **Connect**.

The extension is loaded locally; it is not installed from the Chrome Web Store.

### 4. Open Streamloots and configure gifts

- Keep the Streamloots Activity Feed open in the browser while using card automation.
- In the app, configure the Twitch channel point rewards and Streamloots card names you want to automate.
- Choose each reward’s collection and the number of chests and cards to gift.
- Use **Viewer Aliases** in Settings if viewers commonly enter shortened or alternate versions of their usernames.
- Redeem a test reward and confirm the result in **Redemption Activity** or **Claim History**.
- Keep Streamloots Gifter running while the streamer is live and wants automation to process redeems.
