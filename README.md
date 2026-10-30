# Disctalk

This repo holds two things:

- **Disctalk** (`Disctalk/`, `Disctalk.Core/`): the archiver console app. It signs in as your user account with a
  browser header profile and copies messages into MySQL (run every 30 minutes).
- **Disctalk Client** (`DisctalkClient*`): a custom Discord client for Windows and Android. By default it signs in as
  **your own user account**, with the archiver's identity and rate limiting. **Bot mode** (catbot) is still there as
  an option on the sign-in screen.

> Third-party clients that sign in as a user account are against Discord's Terms of Service, and Discord can act on
> the account. This is your account and your call. The client tries to behave like the web client and stay quiet:
> see [Being polite](#being-polite).

The rest of this README covers Disctalk Client. Calls made while building user mode are logged in
[`DECISIONS.md`](DECISIONS.md).

| Project | What it is |
| --- | --- |
| `DisctalkClient.Core` | Connections (user gateway + REST in `UserAccount/`, NetCord for bots in `Discord/`), state and caching, scrollback, unread/typing, sending, settings, archive search (`Archive/`). No UI. |
| `DisctalkClient.UI` | Razor Class Library with **all** of the UI: channel list, DMs, threads, message pane, member list, composer, search, settings, and the theme. |
| `DisctalkClient` | .NET MAUI Blazor Hybrid host for **Windows and Android**. Keeps tokens and the archive password in SecureStorage. |
| `DisctalkClient.DevHost` | Blazor Server host for any OS (Linux included). `--demo` runs a fake Discord, so no token is needed. |
| `DisctalkClient.Tests` | xUnit v3 + bUnit tests. Discord is faked; tests make no network calls. One opt-in test runs the search SQL against a real MySQL. |

What it does: all your servers and DMs (group DMs too), categories, channels and active threads with unread dots,
a live message pane with infinite scrollback, Discord markdown, embeds, inline images/video and attachments, custom
emoji and stickers, reactions, replies, forwards, thread chips, mentions, live edits and deletes, typing indicators,
and a member list. You can send, reply, react, edit and delete as yourself (or through a webhook persona in servers
where you have Manage Webhooks). Timestamps show seconds (`h:mm:ss tt`, configurable). Search covers the archive
database your archiver fills. It reconnects and resumes on its own after network drops.

---

## 1. Running it

Requires the **.NET 10 SDK** (10.0.x).

### Windows

```powershell
dotnet workload install maui-windows      # once
dotnet run --project DisctalkClient -f net10.0-windows10.0.19041.0
```

Or open `DisctalkClient.slnx` in Visual Studio 2026, set **DisctalkClient** as the startup project, pick
**Windows Machine**, and press F5. To make a folder you can copy anywhere:

```powershell
dotnet publish DisctalkClient -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None -p:RuntimeIdentifierOverride=win-x64 -o publish\windows
publish\windows\DisctalkClient.exe
```

### Android

Windows or Linux, with a phone (USB debugging on) or an emulator attached:

```bash
dotnet workload install maui-android      # once
# once, if you don't have the Android SDK from Visual Studio / Android Studio:
dotnet build DisctalkClient -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=True
# build, install and launch on the attached device:
dotnet build DisctalkClient -t:Run -f net10.0-android
```

To get an APK to sideload instead: `dotnet publish DisctalkClient -f net10.0-android -c Release -o publish/android`,
then copy `publish/android/com.adubry75.disctalkclient-Signed.apk` to the phone and open it (allow installs from
that app when Android asks). It's signed with the local debug key, which is fine for your own phone.

**CI builds both.** Every push to `master` runs `.github/workflows/build.yml`: tests (including the search SQL against
MySQL), the Android APK, and the Windows app. Download them from the run's **Artifacts** on GitHub
(`DisctalkClient-android`, `DisctalkClient-windows-x64`).

### First run: sign in

The sign-in screen has two choices:

- **My account** (default). Paste your **user token**. It's the same token the archiver reads from `MY_BOT_TOKEN`.
  To get it fresh: sign in at discord.com in a desktop browser, press F12 → **Network**, click any request to
  `discord.com/api/...`, and copy the **Authorization** request header (quotes are fine; they're stripped).
- **Bot**. Paste catbot's token from the Developer Portal (see [Bot mode](#bot-mode-catbot) below).

The client checks the token once (`GET /users/@me`, same as the web client does at startup). A rejected token is
never saved. A good one goes into **SecureStorage**: Android Keystore, or Windows DataProtection. Unpackaged Windows
falls back to DPAPI-encrypted files under `%LOCALAPPDATA%\DisctalkClient`. Each account kind has its own entry, so
you can switch with **Settings → Account → Switch to the bot / my account** without re-entering anything. Tokens are
never logged or committed: logs and error messages pass through `SecretRedactor`, and `.gitignore` blocks
`*.token`, `.env` and `secrets.json`.

If Discord resets your token (password change, "log out all devices"), the gateway closes with 4004 and the client
returns to the sign-in screen with an explanation. It doesn't retry.

### Archive search

Click 🔍 in the channel header. The first time, open **Settings → Archive search** and enter the database the
archiver writes to (same values as its `MYSQLHOST`, `MYSQLUSER`, `MYSQLPASSWORD` and `MYSQLDATABASE`; on Windows
and in the dev host those environment variables pre-fill it). **Test & save** connects once and stores everything in
SecureStorage. On Android the phone has to reach the database: same Wi-Fi as the MySQL machine (bind MySQL to the
LAN address and allow port 3306), or a VPN like Tailscale.

Search syntax: plain words (all must match), `"exact phrase"`, `from:name` (username, display name or nickname
prefix, or a user id), `in:channel` (name or id), `after:2025-01-31`, `before:…`, `during:…` (UTC days), `has:link`.
**Only <server>** limits it to the server you're in. Results show channel, author and the time with seconds. Click one
to jump to it in the channel (pages back up to 250 messages, then flashes it). Older results stay readable in the
panel, rendered from the archived JSON.

### Being polite

- REST goes through the archiver's `RateLimitHandler`: per-route buckets, `Retry-After` on 429, a 250 ms minimum gap
  between requests. Captcha requests are surfaced ("do this once in the official app") rather than retried.
- No polling. Live updates come only from the gateway. History is fetched only when you open a channel or scroll up
  (50 messages per page).
- Gateway: heartbeats on Discord's interval, RESUME after drops (no full re-login), exponential backoff with jitter
  (1 s, 2 s, 4 s … up to 60 s), at least 5 s between logins, and no reconnect at all on fatal close codes.
- Presence is sent as `unknown`, so the client doesn't change the status you set elsewhere.
- Typing and member-list subscriptions are sent only for the channel you have open. Read markers aren't synced back
  to Discord (fewer requests). The official app still shows unread for what you read here.

### Any OS, in a browser (dev host)

```bash
# Fake server with simulated chatter, no token needed:
dotnet run --project DisctalkClient.DevHost --launch-profile Demo

# Real account (the UI asks if none is set):
export DISCTALK_USER_TOKEN=...       # or: dotnet user-secrets set Discord:UserToken ... --project DisctalkClient.DevHost
export DISCTALK_BOT_TOKEN=...        # optional, for bot mode (Discord:BotToken)
dotnet run --project DisctalkClient.DevHost --launch-profile Live
```

Then open <http://localhost:5180>. The dev host renders the same `DisctalkApp` component as the MAUI app, keeps
tokens in memory only, and picks up the `MYSQL*` variables for search.

### Tests

```bash
dotnet test --solution DisctalkClient.NoMaui.slnf      # client: no MAUI workloads needed
dotnet test --project Disctalk.Tests                   # archiver's HTTP stack

# Optional: run the archive search SQL against a throwaway MySQL/MariaDB database (it creates its own tables):
DISCTALK_TEST_MYSQL="Server=127.0.0.1;Database=disctalk_test;User ID=me;Password=..." dotnet test --project DisctalkClient.Tests
```

### Header profile

User mode presents the same browser identity as the archiver: `headerprofile.json` at the repo root (embedded at
build time; a `headerprofile.json` next to the exe or `DISCTALK_HEADER_PROFILE` overrides it). The gateway IDENTIFY
uses its `superProperties`, and `client_build_number` refreshes from discord.com every 12 hours. To match the web
client exactly, you can add the web client's gateway capabilities to the profile:

```json
"gateway": { "capabilities": 30717 }
```

Without it the client asks for a compact READY (capabilities `16`). The parser handles both shapes.

---

## 2. Bot mode (catbot)

Pick **Bot** on the sign-in screen. Bot mode uses NetCord and only the documented bot API.

1. <https://discord.com/developers/applications> → your application → **Bot**.
2. Under **Privileged Gateway Intents** turn on **Server Members Intent** and **Message Content Intent**.
3. **Reset Token**, copy it, and paste it into the client once.
4. Invite it (replace `YOUR_APP_ID`):
   `https://discord.com/oauth2/authorize?client_id=YOUR_APP_ID&scope=bot&permissions=537259072`
   (View Channels, Read History, Send, Embed Links, Attach Files, Add Reactions, External Emojis, Manage Messages,
   Manage Webhooks. Use `537390144` to also allow @everyone.)

---

## 3. Using it

- **Servers and DMs**: the picker at the top left. **✉ Direct Messages** comes first, sorted by latest activity.
  Threads are listed under their channel. A 🧵 chip under a message opens its thread.
- **Send mode toggle**: the pill next to the send button. **Me** (or **Bot** in bot mode) posts as the signed-in
  account. **Webhook** posts through the channel's webhook using the display name and avatar from Settings → Sending
  (needs Manage Webhooks; not available in DMs). Webhooks can't make real replies, so replies in webhook mode get a
  small `↪ Replying to @name · jump` header. Typing indicators are only sent in the direct mode.
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
| User-mode gateway, REST, JSON mapping, reconnect policy | `DisctalkClient.Core/UserAccount/` |
| Bot-mode gateway events, intents, mapping Discord → models | `DisctalkClient.Core/Discord/` |
| Archive search (query syntax, SQL, MySQL) | `DisctalkClient.Core/Archive/`, `UI/Components/SearchPanel.razor` |
| Caching, scrollback, unread, typing | `DisctalkClient.Core/State/` |

### Design notes

- **Two connections, one interface.** `IDiscordConnectionFactory.Create(kind, token)` returns a
  `UserGatewayConnection` (user mode: hand-written gateway v9 + zlib-stream, REST through Disctalk.Core's
  `DiscordHttp` pipeline with the header profile, super properties and `RateLimitHandler`) or a `NetCordConnection`
  (bot mode: documented bot API only). Everything above that is shared.
- **Reuse of Disctalk.Core.** User-mode REST and the gateway IDENTIFY use the archiver's `HeaderProfile`,
  `BuildNumberProvider` and `RateLimitHandler` as-is, so both apps present one identity. Webhook execution also goes
  through `RateLimitHandler`. The archiver's Newtonsoft models aren't used; user-mode JSON is mapped by
  `UserAccount/DiscordJson.cs` with System.Text.Json.
- **Library-neutral core.** NetCord types stop at `DisctalkClient.Core/Discord/`, raw JSON at `UserAccount/`. Everything else uses small records
  (`ChatMessage`, `ChannelInfo`…) and `GatewayEvent`s. That keeps the UI testable and is what lets the dev host's
  demo mode and the tests swap in a fake Discord.
- **Threading.** Gateway events arrive on background threads. `ChatClient` guards its state with a lock and raises
  `Changed`. Components re-render via `InvokeAsync(StateHasChanged)`.
