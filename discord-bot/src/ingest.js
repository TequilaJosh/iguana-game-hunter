import express from 'express';
import { config } from './config.js';
import { findGuildByToken, getGuild } from './guildStore.js';
import { postClip } from './features/clips.js';
import { postRecap } from './features/recap.js';
import { guideHtml } from './features/guide.js';
import { mountAdmin } from './admin.js';
import { mountProfile } from './profile.js';
import { mountPlay } from './play.js';
import { mountParty } from './party.js';
import { handleGameMessage } from './game/bridge.js';
import { forceRaid } from './game/raids.js';
import { log } from './logger.js';

/**
 * HTTP endpoint Game Hunter posts clips to. Each server has its own ingest token
 * (from /setup ingest); the token both authenticates the request AND selects which
 * server's clips channel to post in.
 */
export function startIngestServer(client) {
  const app = express();
  app.use(express.json({ limit: '16kb' }));

  // `build` marks which code is live — bump it with notable deploys so /health confirms
  // the running version (helps catch "pushed but not deployed").
  app.get('/health', (_req, res) => res.json({ ok: true, build: 'gather5-combat-voice-2026-08-24', uptime: Math.round(process.uptime()) }));

  // Public Tavern Tales player guide (no auth) — a shareable HTML page.
  app.get('/guide', (_req, res) => res.type('html').send(guideHtml()));

  // Connection test for Game Hunter's "Test bot" button: validates the token, and
  // (if a clips channel is set) posts a short confirmation there so it's visible.
  app.post('/verify', async (req, res) => {
    const header = req.get('authorization') || '';
    const token = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';
    const guildId = findGuildByToken(token);
    if (!guildId) return res.status(401).json({ ok: false, error: 'unknown ingest token' });

    const cfg = getGuild(guildId);
    let posted = false;
    if (cfg.clipChannelId) {
      const ch = await client.channels.fetch(cfg.clipChannelId).catch(() => null);
      if (ch && ch.isTextBased()) {
        await ch.send('🔌 **Connection test** — Game Hunter is linked to this channel ✅').catch(() => {});
        posted = true;
      }
    }
    res.json({ ok: true, clipChannel: !!cfg.clipChannelId, posted });
  });

  app.post('/clip', async (req, res) => {
    const header = req.get('authorization') || '';
    const token = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';

    const guildId = findGuildByToken(token);
    if (!guildId) return res.status(401).json({ ok: false, error: 'unknown ingest token' });

    const { user, url, note, game, type } = req.body || {};
    if (!url && !user) return res.status(400).json({ ok: false, error: 'need at least "user" or "url"' });

    const ok = await postClip(client, guildId, { user, url, note, game, type }).catch((e) => {
      log.error('postClip failed:', e);
      return false;
    });
    res.status(ok ? 200 : 500).json({ ok });
  });

  // Stream recap sharing: Game Hunter posts its end-of-stream summary here.
  app.post('/recap', async (req, res) => {
    const header = req.get('authorization') || '';
    const token = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';
    const guildId = findGuildByToken(token);
    if (!guildId) return res.status(401).json({ ok: false, error: 'unknown ingest token' });

    const { recap } = req.body || {};
    if (!recap || !String(recap).trim()) return res.status(400).json({ ok: false, error: 'need "recap"' });

    const ok = await postRecap(client, guildId, String(recap)).catch((e) => {
      log.error('postRecap failed:', e);
      return false;
    });
    res.status(ok ? 200 : 500).json({ ok });
  });

  // Tavern Tales bridge: Game Hunter forwards chat "!" game commands here.
  app.post('/game', async (req, res) => {
    const header = req.get('authorization') || '';
    const token = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';
    const guildId = findGuildByToken(token);
    if (!guildId) return res.status(401).json({ ok: false, error: 'unknown ingest token' });

    const { platform, user, text } = req.body || {};
    if (!platform || !user || !text) return res.status(400).json({ ok: false, error: 'need platform, user, text' });

    const reply = await handleGameMessage(client, guildId, platform, user, text).catch((e) => {
      log.error('game bridge failed:', e);
      return 'Something went wrong.';
    });
    res.json({ ok: true, reply });
  });

  // Trigger a raid announcement on demand (optional lobbyMinutes).
  app.post('/raidnow', async (req, res) => {
    const header = req.get('authorization') || '';
    const token = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';
    const guildId = findGuildByToken(token);
    if (!guildId) return res.status(401).json({ ok: false, error: 'unknown ingest token' });
    const m = Number(req.body?.lobbyMinutes);
    const level = req.body?.level;
    const r = await forceRaid(guildId, client, Number.isFinite(m) && m > 0 ? m * 60000 : undefined, level)
      .catch((e) => ({ error: e.message }));
    res.json(r.error ? { ok: false, error: r.error } : { ok: true, boss: r.raid.boss.name, zone: r.zone.name });
  });

  mountAdmin(app);        // web player-editor at /admin (password-gated)
  mountProfile(app);      // public read-only hero profiles at /profile
  mountPlay(app, client); // playable browser client at /play (button-driven)
  mountParty(app);        // animated party sprite overlay at /party (+ /party/api feed)

  app.listen(config.port, () => log.info(`Ingest server listening on :${config.port}`));
}
