# Decisions log

Calls made during the unattended overnight session (2026-10-30) that switched Disctalk Client from the
`catbot` bot account to the owner's own user account. Newest last. Each entry says what was decided and why,
so any of them can be reversed deliberately.

## Ground rules taken from the request

- Work directly on `master`, commit + push after each meaningful step, no new branches.
- No token ever goes in the repo. User token and bot token live in MAUI `SecureStorage` (DPAPI file fallback on
  unpackaged Windows), entered on first run.
- User-account mode is the default; bot (`catbot`) mode stays available as an option on the sign-in screen.
- Latest .NET (10, the current GA release; .NET 11 is not GA yet) and current C# (`LangVersion latest`).

## Decisions

1. **Auth reuses the archiver stack verbatim.** The archiver (`Disctalk/` + `Disctalk.Core/Http`) sends the raw user
   token in `Authorization` (no `Bot ` prefix), the browser header profile from `headerprofile.json`, a base64
   `X-Super-Properties` built from the same profile, a build number scraped from discord.com (cached 12h), and routes
   every call through `RateLimitHandler`. User-mode REST in the client is built with `DiscordHttp.CreateClient(...)`
   from `Disctalk.Core`, so both apps present exactly the same identity and share the same rate-limit logic. No
   header values were duplicated or invented in the client.
2. **No live Discord traffic from this session.** Per the owner's mid-session caveat ("do not send messages to any
   of my connected servers, except server 950297385559523338"), and because no token exists in this container
   anyway, nothing here talked to Discord. Every gateway/REST path is exercised against fakes and recorded JSON
   shapes in the unit tests. First real sign-in happens on your machine.
3. **Accounts are a first-class choice, not a fork.** `AccountKind { User, Bot }` flows from the sign-in screen →
   `ITokenStore` (one SecureStorage key per kind; the bot keeps its old key so an existing catbot token still works)
   → `IDiscordConnectionFactory.Create(kind, token)`. `ClientSettings.Account` remembers the last choice; default
   is `User`. Settings → Account has "Switch to the bot / my account". `IBotRestApi` was renamed `IDiscordRestApi`.
4. **User gateway is hand-written, not a library.** NetCord (and Discord.Net) only speak the bot protocol. The user
   session (`UserAccount/UserGatewayConnection`) uses gateway v9 with `zlib-stream` like the web client, IDENTIFYs
   with the profile's super properties, `presence.status = "unknown"` (keeps whatever status you set elsewhere),
   and `capabilities` from `headerprofile.json` → `gateway.capabilities`, defaulting to 16 (DEDUPE_USER_OBJECTS).
   The parser accepts both READY shapes so you can paste the web client's real capabilities value into the profile.
5. **Politeness.** REST: the archiver's `RateLimitHandler` (per-bucket, 429 Retry-After, 250 ms minimum spacing).
   Gateway: heartbeat with jittered first beat, zombie detection, RESUME on drops, exponential backoff 1 s→60 s with
   jitter, 1–5 s wait after invalid session, ≥5 s between IDENTIFYs, no reconnect at all on 4004/401x close codes.
   No polling anywhere: live data only arrives over the gateway; history loads only when you scroll up.
6. **Token check before saving.** User sign-in first calls `GET /users/@me` (the web client does the same at
   startup). A 401 fails the sign-in so a bad token is never stored. A network error there is not fatal; the
   gateway loop retries with backoff.
7. **DMs live under a "Direct Messages" pseudo guild (id 0)** at the top of the server picker, sorted by latest
   activity, with the other person's avatar. Their member list shows only that conversation's people. Webhook
   sending is hidden in DMs (webhooks don't exist there); DMs always send as you.
8. **Threads are listed under their parent channel** (active threads from READY / THREAD_* events; archived ones drop
   out like in Discord). A message that started a thread gets a chip; clicking it opens the thread even when the
   gateway never listed it (archived or not joined), by synthesizing the channel from the message's thread info.
9. **Read state:** Discord's own markers from READY seed the unread dots (never moving a local marker backwards).
   The client does **not** send read acks back to Discord, to keep the request count minimal. So channels you read
   here still show unread in the official app until you open them there. Easy to add later (`POST .../ack`).
10. **Member list in user mode** comes from op 37 subscriptions (first 100 of the open channel's list) plus the
    `member` object on every incoming message. There's no full member crawl: that would be a lot of traffic for a
    user account.
11. **Timestamps with seconds (backlog #1)** were already done before this session (`h:mm:ss tt` default, setting
    with presets, all render paths through `TimestampFormatter`). Verified, kept, and re-checked by the tests.
12. **Archive search reads the archiver's MySQL directly** (`messages` ⟕ `channels`, `usersServerInfo` for nick /
    global-name matching) with MySqlConnector, the archiver's own driver. Discord-style syntax: words (all must
    match), `"phrases"`, `from:` (username/nick/display-name prefix or id), `in:` (channel name or id),
    `after:`/`before:`/`during:` (UTC dates), `has:link`. Every value is a parameter; `%`/`_` are escaped.
    Plain `LIKE '%…%'` rather than FULLTEXT: the archiver's schema has no full-text index and I won't alter your
    database from a client. If searches get slow on a big archive, adding a FULLTEXT index is the next step.
13. **Dates filter on snowflake ids, not the `timestamp` column.** The archiver writes `timestamp` from a Newtonsoft
    `DateTime` whose time zone depends on the machine; message ids encode exact UTC and are the primary key.
14. **Connection details are a secret** (they include the DB password): stored in SecureStorage like the tokens,
    pre-filled from the archiver's `MYSQLHOST`/`MYSQLUSER`/`MYSQLPASSWORD`/`MYSQLDATABASE` (+ optional `MYSQLPORT`)
    environment variables when present (Windows/dev host). Read-only use; `AllowLoadLocalInfile` off.
15. **Jump to message pages back at most 5 × 50 messages** (polite: a click never becomes a crawl). Older hits stay
    readable in the results, rendered from the archived JSON. No "around" windowing, which would mean a cache with
    gaps.
16. **MySqlConnector bumped 2.5.0 → 2.6.2 in the archiver too** (NuGet flagged GHSA-473q-m89c-ghf8 on 2.5.0). The
    archiver still builds; no code changes needed.
17. **Integration test against a real server.** `MySqlArchiveIntegrationTests` creates archiver-shaped tables and
    runs the real SQL. It ran green here against MariaDB; it's skipped unless `DISCTALK_TEST_MYSQL` is set.
