# LazerGuanas Bot

A **multi-server** companion Discord bot for **LazerGuanas Game Hunter**. Any server can
add it and configure itself with `/setup` — clips, welcome messages, auto-role, and its own
Game Hunter clip-ingest token.

- **Clip reposting** — each server gets its own ingest token; Game Hunter posts clips to `/clip` and they land in that server's clips channel.
- **Welcome + auto-role** — per server, set with `/setup`.
- **Slash commands** — `/setup`, `/ping`, `/latest`, `/purge`, `/slowmode`.

You (the bot owner) run **one** instance. Server admins configure their own server in Discord —
you don't touch their settings.

---

## 1. Create the bot (once, by you)

1. **[Discord Developer Portal](https://discord.com/developers/applications)** → **New Application**.
2. **Bot** tab → **Reset Token** → copy (`DISCORD_TOKEN`, keep secret). Enable **Server Members Intent** (for welcome/auto-role).
3. **General Information** → copy **Application ID** (`CLIENT_ID`).
4. **Bot** tab → make sure **Public Bot** is **ON** so other servers can add it.

Invite link (share this with server owners; replace `CLIENT_ID`):
```
https://discord.com/oauth2/authorize?client_id=CLIENT_ID&permissions=268463120&scope=bot%20applications.commands
```

## 2. Configure & run (you, once)

```bash
cp .env.example .env      # fill in DISCORD_TOKEN, CLIENT_ID, and PUBLIC_URL (your host URL)
npm install
npm run register          # global slash commands (first time can take ~1h to appear everywhere)
npm start
```

## 3. Deploy 24/7 (you)

Standard Node service with a `Dockerfile`.

```bash
docker build -t lazerguanas-bot .
docker run -d --env-file .env -p 8080:8080 -v lg-bot-data:/app/data --name lazerguanas-bot lazerguanas-bot
```

On **Railway / Render / Fly.io / a VPS**: set the env vars in the host dashboard, expose the web
port, set `PUBLIC_URL` to the host's public URL, and — **important** — attach a **persistent
volume** mounted at `DATA_DIR` (e.g. `/app/data`). That file holds every server's settings and
ingest tokens; without a persistent volume they reset on each redeploy.

---

## 4. How each server sets itself up (server admins)

After an admin adds the bot with the invite link, in their server:

1. `/setup clips #clips` — where clips get posted.
2. `/setup welcome #general` — optional welcome messages.
3. `/setup autorole @Member` — optional role for new members.
4. `/setup ingest` — shows that server's **Bot ingest URL** + **Ingest token** (only the admin sees it).
5. Paste those two into **Game Hunter → Settings → Chat → ⚙ Features → Discord**.

`/setup view` shows the current settings anytime. Each server is independent — its token only
posts to its own clips channel.

### Ingest API

`POST /clip` — header `Authorization: Bearer <that server's token>`, JSON body:

```json
{ "user": "TequilaJosh", "url": "https://clips.twitch.tv/…", "note": "insane play", "type": "clip" }
```

The token identifies the server, so the clip routes to the right place. `GET /health` returns `{ ok: true }`.

---

## Extending (roadmap)

Features live in `src/features/` (+ a command in `src/commands.js`). Per-server settings go
through `src/guildStore.js`. Ideas: reaction/button roles, `/kick` `/ban` `/timeout`, join logs,
go-live announcements, giveaways.
