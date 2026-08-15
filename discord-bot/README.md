# LazerGuanas Bot

Companion Discord bot for **LazerGuanas Game Hunter**. It reposts clips that Game
Hunter sends it, and provides a foundation for community features (welcome
messages, auto-role, moderation slash commands).

- **Clip reposting** — Game Hunter POSTs clip events to the bot's `/clip` endpoint; the bot posts them in your clips channel.
- **Welcome + auto-role** — greets new members, optionally assigns a starter role.
- **Slash commands** — `/ping`, `/latest`, `/purge`, `/slowmode` (permission-gated). Add more in `src/commands.js`.

---

## 1. Create the bot (once)

1. Go to the **[Discord Developer Portal](https://discord.com/developers/applications)** → **New Application**.
2. **Bot** tab → **Reset Token** → copy it (this is your `DISCORD_TOKEN` — keep it secret).
3. Still on the **Bot** tab, enable **Server Members Intent** (needed for welcome/auto-role).
4. **General Information** → copy the **Application ID** (this is your `CLIENT_ID`).
5. Invite the bot: **OAuth2 → URL Generator** → scopes **`bot`** + **`applications.commands`**, then bot permissions: *Send Messages, Embed Links, Manage Messages, Manage Channels, Manage Roles*. Open the generated URL and add it to your server.

Get IDs by enabling **Developer Mode** in Discord (Settings → Advanced), then right-click a
server/channel/role → **Copy ID**.

## 2. Configure

```bash
cp .env.example .env
```

Fill in `.env`: `DISCORD_TOKEN`, `CLIENT_ID`, `GUILD_ID`, `CLIP_CHANNEL_ID`,
`WELCOME_CHANNEL_ID` (optional), `AUTOROLE_ID` (optional), and an `INGEST_TOKEN`
(generate one: `node -e "console.log(require('crypto').randomBytes(24).toString('hex'))"`).

## 3. Run

```bash
npm install
npm run register   # push slash commands to your server (run again after adding commands)
npm start
```

You should see `Logged in as …` and `Ingest server listening on :8080`.

## 4. Connect Game Hunter

In Game Hunter → **Settings → Chat → ⚙ Features → Discord**:

- **Bot ingest URL**: `https://<your-host>/clip` (or `http://localhost:8080/clip` while testing locally).
- **Ingest token**: the same `INGEST_TOKEN` from your `.env`.

When it's set, Game Hunter sends `!clip` and Twitch clip links to the bot instead of a
webhook. (Leave it blank to keep using a plain webhook.)

### Ingest API

`POST /clip` — header `Authorization: Bearer <INGEST_TOKEN>`, JSON body:

```json
{ "user": "TequilaJosh", "url": "https://clips.twitch.tv/…", "note": "insane play", "type": "clip" }
```

`type` is `"clip"` (has a `url`) or `"highlight"` (note-only). `GET /health` returns `{ ok: true }`.

## 5. Deploy 24/7

The bot is a standard Node service and ships with a `Dockerfile`.

```bash
docker build -t lazerguanas-bot .
docker run -d --env-file .env -p 8080:8080 --name lazerguanas-bot lazerguanas-bot
```

On **Railway / Fly.io / Render / a VPS**: set the env vars from `.env` in the host's
dashboard (don't commit `.env`), expose the web port, and make sure the host's public URL
is what you put in Game Hunter's **Bot ingest URL** (with the `/clip` path). Most hosts set
`PORT` for you — the bot honors it.

## Extending (community-bot roadmap)

Each feature is a small module under `src/features/` plus (optionally) a slash command in
`src/commands.js`:

- **Roles**: reaction/button role menus.
- **Moderation**: `/kick`, `/ban`, `/timeout`, word filter, join logs to `LOG_CHANNEL_ID`.
- **Engagement**: `/poll`, giveaways, stream go-live announcements.

Add the module, wire its event in `src/index.js` (or command in `src/commands.js`), run
`npm run register` if you added a command, and redeploy.
