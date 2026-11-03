# Streamloots Gifter

**A Windows desktop app for automating Streamloots gifts triggered by Twitch channel point redeems and Streamloots card redeems.**

[Download the latest release](https://github.com/shuichi67/StreamlootsGifter/releases/tag/v0.5.0) · [View all releases](https://github.com/shuichi67/StreamlootsGifter/releases)

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

## What you need

- Windows 10 or 11, 64-bit.
- A Twitch account for the streamer.
- A Streamloots creator account with access to the relevant collections and gifting controls.
- Google Chrome (recommended) or other Chromium-based browsers.
- A Twitch Developer application Client ID and Client Secret.

## First-time setup

### 1. Download and extract the app

1. Open the [latest release](https://github.com/shuichi67/StreamlootsGifter/releases/latest).
2. Download `StreamlootsGifter-Windows-x64-...zip`.
3. Extract the ZIP to a folder the streamer can write to, such as `Documents\Streamloots Gifter`.
4. Keep the extracted folder in place. The app stores its local settings and history alongside the application.

### 2. Create Twitch application credentials

1. Open the [Twitch Developer Console](https://dev.twitch.tv/console/apps) and sign in.
2. Choose **Applications** and register a new application.
3. Enter a name, such as `Streamloots Gifter - YourChannel`.
4. Add this exact OAuth Redirect URL:
   `http://localhost:17563`
5. Choose the closest matching category, complete verification, and create the application.
6. Open the application’s **Manage** page. Copy its **Client ID**, then create and copy a **Client Secret**.

### 3. Add credentials to `.env`

In the extracted app folder, open `.env` with Notepad and fill in these values:

```text
TWITCH_CLIENT_ID=your_client_id
TWITCH_CLIENT_SECRET=your_client_secret
```

Do not add quotation marks or spaces around the `=` signs. Save the file, then start `StreamlootsGifter.exe`.

### 4. Sign in to Twitch

Click **Continue with Twitch**, sign in as the streamer, and approve the requested access. The first successful login may offer a short in-app introduction.

### 5. Install the browser extension

1. In Chrome, open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked**.
4. Select the `streamloots_extension` folder inside the extracted app folder.
5. Open the Streamloots Gifter extension and connect it to the app using **Get key & connect**. If needed, copy the Bridge key from the app’s Streamloots Bridge widget and enter it manually.

The extension is loaded locally; it is not installed from the Chrome Web Store.

### 6. Open Streamloots and configure gifts

- Keep the Streamloots Activity Feed open in the browser while using card automation.
- In the app, configure the Twitch channel point rewards and Streamloots card names you want to automate.
- Choose each reward’s collection and the number of chests and cards to gift.
- Use **Viewer Aliases** in Settings if viewers commonly enter shortened or alternate versions of their usernames.
- Redeem a test reward and confirm the result in **Redemption Activity** or **Claim History**.
- Keep Streamloots Gifter running while the streamer is live and wants automation to process redeems.
