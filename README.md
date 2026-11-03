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
- Set viewer aliases, such as mapping `libra` to a viewer’s full Streamloots username.
- Review redemption activity and claim history, filter entries, and retry gifts that need attention.
- Customize the dashboard layout and choose a theme.
- Pair the desktop app with the browser extension through a local Bridge connection.
- Check for and install app updates from Settings.
- The current build also retains the optional Free Chest Hour feature.

## What you need

- Windows 10 or 11, 64-bit.
- A Twitch account for the streamer.
- A Streamloots creator account with access to the relevant collections and gifting controls.
- Google Chrome (recommended) or a supported Chromium-based browser.
- A Twitch Developer application Client ID and Client Secret.

The download is self-contained; you do not need to install the .NET SDK.

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

In the extracted app folder, open `.env` with Notepad. Fill in the values:

```env
TWITCH_CLIENT_ID=your_client_id
TWITCH_CLIENT_SECRET=your_client_secret
