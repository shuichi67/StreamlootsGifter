# Streamloots Gifter Privacy Policy

**Last updated: November 9, 2026**

Streamloots Gifter is a browser extension used with the Streamloots Gifter desktop app. It detects configured Streamloots card redemptions and helps the desktop app carry out the corresponding chest gifts.

## Information the extension handles

When you use the extension on Streamloots, it reads redemption details needed for that feature. These can include the card name, redeemer's Streamloots username, recipient or other card-field values, event identifier, and redemption timestamp. It also checks whether relevant Streamloots pages are open so it can report whether redemption detection and gifting are ready. It does not build a general history of the websites you visit.

The extension stores the Bridge key you enter, recent processed redemption identifiers, and basic connection and feed status in the browser's local extension storage. The Bridge key authorizes the extension to communicate with the desktop app. The extension does not read clipboard contents; it can write the Bridge key to the clipboard when you use the copy feature.

## How information is used and shared

Redemption details are used only to match your configured card rules, prevent duplicate processing, and send the gift request to the desktop app. The extension sends matching details to the Streamloots Gifter app running on the same computer through its loopback address (`127.0.0.1`). The local Bridge key is used to authorize these requests.

When a gift is carried out, the desktop app uses the Streamloots page in your browser to submit the recipient and gift details to Streamloots. Streamloots processes that action under its own terms and privacy policy. The extension publisher does not operate a server that receives this data. The extension does not sell the data or send it to advertising, analytics, or other unrelated services.

## Storage and retention

The Bridge key and recent processed-event identifiers remain in the browser's local extension storage until removed. Use **Disconnect** to remove the saved Bridge key; uninstalling the extension or clearing its browser data removes its local extension storage. The desktop app separately keeps its claim and gifting history on your computer until you clear it in the app or remove the app's local data.

## Your choices

You choose whether to connect the extension by entering a Bridge key. You can disconnect it at any time from the extension popup. Generating a new Bridge key invalidates the previous key. You can also disable individual card rules in the desktop app.

## Contact

For privacy questions or requests, contact the project maintainer through the [Streamloots Gifter GitHub repository](https://github.com/shuichi67/StreamlootsGifter/issues).

## Limited Use

Information accessed from Streamloots is used only to provide the extension's disclosed redemption-detection and gifting functionality. It is not sold, used for advertising, or used for creditworthiness decisions. It is shared only with the local desktop companion app and, when a gift is submitted, with Streamloots as necessary to carry out that gift.
