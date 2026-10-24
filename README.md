# Disctalk

This repo holds two things:

- **Disctalk** (`Disctalk/`, `Disctalk.Core/`): the original archiver console app.
- **Disctalk Client** (`DisctalkClient*`): a custom Discord client for your own server. It signs in with an
  **official bot account** (bot token, documented Gateway and REST API only). It is not a user-account client.

The rest of this README covers Disctalk Client.

| Project | What it is |
| --- | --- |
| `DisctalkClient.Core` | Bot connection ([NetCord](https://netcord.dev)), intents, in-memory state and caching, REST scrollback, typing and unread tracking, settings, bot/webhook send routing. No UI. |
| `DisctalkClient.UI` | Razor Class Library with **all** of the UI: channel list, message pane, member list, composer, settings, and the theme. |
| `DisctalkClient` | .NET MAUI Blazor Hybrid host for **Windows and Android**. Stores the token in SecureStorage. |
| `DisctalkClient.DevHost` | Blazor Server host for any OS (Linux included). `--demo` runs a fake Discord, so no token is needed. |
| `DisctalkClient.Tests` | xUnit v3 + bUnit tests. Discord is mocked; tests make no network calls. |

What it does: categories, channels with unread dots, a live message pane with infinite scrollback, Discord markdown,
embeds, images and attachments, custom emoji, reactions, replies, mentions, live edits and deletes, typing indicators,
and a member list grouped by hoisted role. You can send, reply, react, edit and delete, **as the bot** or **through a
webhook persona** (your display name and avatar). Timestamps show seconds (`h:mm:ss tt`). It has a settings page
(timestamp format, compact mode, theme, cat accent color) and a phone layout with slide-out drawers.

Library choice: NetCord was picked over Discord.Net because it is the more actively maintained of the two. It ships
releases weekly, is built for .NET 10, and has no dependencies. Discord.Net is stable but releases a few times a year.

---

## 1. Discord Developer Portal setup

1. Go to <https://discord.com/developers/applications> and click **New Application**. Name it (for example
   "Whiskers"), then **Create**.
2. Open the **Bot** page:
   - Under **Privileged Gateway Intents**, turn on:
     - **Server Members Intent** (member list, nicknames, role colors)
     - **Message Content Intent** (message text, embeds, attachments)
     - *Presence Intent* is **not** needed.
   - Turn **off** "Public Bot" if you want only yourself to be able to invite it.
   - Click **Reset Token** and copy the token. You'll paste it into the client once. Never commit it or share it.
3. Copy the **Application ID** from **General Information**.

The client also requests these non-privileged intents (no toggle needed): Guilds, Guild Messages,
Guild Message Reactions, Guild Message Typing, and Guild Emojis & Stickers (keeps the custom emoji picker current).

### Invite URL

Replace `YOUR_APP_ID` and open this link while signed in as a server admin:

```
https://discord.com/oauth2/authorize?client_id=YOUR_APP_ID&scope=bot&permissions=537259072
```

`537259072` grants:

| Permission | Why |
| --- | --- |
| View Channels, Read Message History | See channels and scroll back |
| Send Messages, Embed Links, Attach Files | Post messages, link previews, uploads |
| Add Reactions, Use External Emojis | React; use emoji from your other servers |
| Manage Messages | Delete other people's messages (optional; your own and your webhook's work without it) |
| Manage Webhooks | **Webhook send mode**: the bot creates one webhook named "Disctalk Client" per channel |

To let the client ping `@everyone`/`@here`, use `537390144` (adds Mention Everyone) and turn on the matching
setting in the client.

---

## 2. Running it

Requires the **.NET 10 SDK** (`global.json` sets the test runner; any 10.0.x SDK works).

### Windows or Android app (MAUI)

```powershell
dotnet workload install maui           # once
# Windows:
dotnet build DisctalkClient -f net10.0-windows10.0.19041.0
dotnet run --project DisctalkClient -f net10.0-windows10.0.19041.0
# Android (device or emulator attached):
dotnet build DisctalkClient -t:Run -f net10.0-android
```

You can also open `DisctalkClient.slnx` in Visual Studio 2026, pick the **DisctalkClient** project, choose Windows
Machine or an Android target, and press F5.

On first launch the client asks for the bot token, checks its shape, connects, and saves it to **SecureStorage**
(Android Keystore or Windows DataProtection; unpackaged Windows falls back to a DPAPI-encrypted file under
`%LOCALAPPDATA%\DisctalkClient`). It never asks again unless Discord rejects the token or you choose
**Settings → Forget token & disconnect**. The token is never logged: gateway and REST logs pass through
`SecretRedactor`, and error messages are scrubbed too.

On Linux you can only build the MAUI app for Android (`dotnet workload install maui-android`, then
`dotnet build -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=True` once). The Windows
target needs Windows.

### Any OS, in a browser (dev host)

```bash
# Fake server with simulated chatter, no token needed:
dotnet run --project DisctalkClient.DevHost --launch-profile Demo

# Your real server (the UI asks for the token if none is set):
export DISCTALK_BOT_TOKEN=...            # or: dotnet user-secrets set Discord:BotToken ... --project DisctalkClient.DevHost
dotnet run --project DisctalkClient.DevHost --launch-profile Live
```

Then open <http://localhost:5180>. The dev host renders exactly the same
`DisctalkApp` component as the MAUI app. It keeps the token in memory only and stores settings under your local
app-data folder, never in the repo.

### Tests

```bash
dotnet test --project DisctalkClient.Tests
```

On a machine without MAUI workloads, build everything else with `dotnet build DisctalkClient.NoMaui.slnf`.

---

## 3. Using it

- **Send mode toggle**: the pill next to the send button. **Bot** posts as the bot user. **Webhook** posts through
  the channel's webhook using the display name and avatar from Settings → Sending. Webhooks can't make real replies,
  so replies in webhook mode get a small `↪ Replying to @name · jump` header. Typing indicators are only sent in bot
  mode.
- **Keyboard**: Enter sends, Shift+Enter adds a newline, ↑ in an empty composer edits your last message, Esc cancels
  a reply or edit, and Shift+click on 🗑 deletes without confirming.
- **Emoji**: type `:blobcat:` or `:thumbsup:` and it's converted on send, or use the picker. Server emoji come first.
- **Phone**: ☰ opens channels and 👥 opens members. Tap the backdrop or pick a channel to close the drawer. Tap a
  message to show its actions.
- **Settings** (sliders icon, bottom left): timestamp format (presets or any .NET format string, with live preview),
  compact mode, theme (Dark / Midnight / Light), cat accent (Ginger, Siamese, Calico, Tabby, Tuxedo), and the
  webhook persona.

---

## 4. How to add a tweak

Everything you'd customize lives in `DisctalkClient.UI` (looks, behavior) or `DisctalkClient.Core` (data, rules). The
hosts are thin, so a tweak made once shows up on Windows, Android and the dev host.

The **first customization**, timestamps with seconds, is a good template. It touched four places:

1. **The setting**: `DisctalkClient.Core/Settings/ClientSettings.cs` has
   `DefaultTimestampFormat = "h:mm:ss tt"` and a `TimestampFormat` property. Settings are a record that is saved as
   JSON. A new property with a default just works: older saved settings pick up the default.
2. **Validation**: `SettingsValidator` rejects bad values, so the UI can't save a broken format, and
   `SettingsService` repairs a bad stored value on load.
3. **The logic**: `TimestampFormatter` is the only place times are turned into text (headers, compact rows, hover
   times, `(edited)` tooltips, `<t:…>` markdown). Each component injects it.
4. **The UI**: `DisctalkClient.UI/Components/SettingsPage.razor` has the input, presets and preview.
   `TimestampFormatterTests` and `SettingsServiceTests` pin the behavior.

### Example: highlight messages containing a keyword

1. Add the setting to `ClientSettings`:
   ```csharp
   public string HighlightWords { get; init; } = "";   // comma-separated
   ```
2. Add a field to the **Appearance** section of `SettingsPage.razor`:
   ```razor
   <input type="text" value="@S.HighlightWords"
          @onchange="e => Apply(s => s with { HighlightWords = e.Value?.ToString() ?? string.Empty })" />
   ```
3. Use it in `Components/MessageView.razor`. Extend `MentionsMe`, or add a class:
   ```csharp
   [Inject] SettingsService Settings { get; set; } = default!;
   bool Highlighted => Settings.Current.HighlightWords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
       .Any(w => Message.Content.Contains(w, StringComparison.OrdinalIgnoreCase));
   ```
   and put `@(Highlighted ? "mentioned" : "")` on the `.dt-message` div. `MessageView` skips re-rendering unchanged
   rows. `MessageList` bumps its `Version` when settings change, so the new setting applies right away.
4. Add a bUnit test next to `DisctalkClient.Tests/Rendering/MessageViewTests.cs` (render a message, change the
   setting, assert the class).
5. Check it in the browser: `dotnet run --project DisctalkClient.DevHost --launch-profile Demo`.

### Where things live

| You want to change… | Look in |
| --- | --- |
| Colors, spacing, theme, phone layout | `DisctalkClient.UI/wwwroot/disctalk.css` (CSS variables at the top; `.theme-*` blocks; `@media (max-width: 860px)`) |
| Accent colors | `ClientSettings.AccentHex` + the `CatAccent` enum |
| How a message looks | `Components/MessageView.razor`, `EmbedView.razor`, `AttachmentView.razor`, `ReactionBar.razor` |
| Markdown rules | `DisctalkClient.UI/Markdown/DiscordMarkdown.cs` (all output is HTML-escaped; keep it that way) |
| Composer / keyboard shortcuts | `Components/Composer.razor` + `wwwroot/disctalk.js` |
| What gets sent and how (bot vs webhook, mentions, reply header) | `DisctalkClient.Core/Sending/` |
| Gateway events, intents, mapping Discord → models | `DisctalkClient.Core/Discord/` |
| Caching, scrollback, unread, typing | `DisctalkClient.Core/State/` |

### Design notes

- **Only documented bot APIs.** `DisctalkClient.Core` talks to Discord through NetCord's `GatewayClient`/`RestClient`
  with a `BotToken`, plus the documented webhook endpoints. It does **not** use Disctalk.Core's user-client header
  profile.
- **Reuse of Disctalk.Core.** Webhook execution goes over a plain `HttpClient` through Disctalk.Core's
  `RateLimitHandler` (per-bucket limits, 429 retry). Its models and the user-session HTTP stack are archiver-specific
  and are not used.
- **Library-neutral core.** NetCord types stop at `DisctalkClient.Core/Discord/`. Everything else uses small records
  (`ChatMessage`, `ChannelInfo`…) and `GatewayEvent`s. That keeps the UI testable and is what lets the dev host's
  demo mode and the tests swap in a fake Discord.
- **Threading.** Gateway events arrive on background threads. `ChatClient` guards its state with a lock and raises
  `Changed`. Components re-render via `InvokeAsync(StateHasChanged)`.
