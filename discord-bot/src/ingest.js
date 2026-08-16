import express from 'express';
import { config } from './config.js';
import { findGuildByToken } from './guildStore.js';
import { postClip } from './features/clips.js';
import { log } from './logger.js';

/**
 * HTTP endpoint Game Hunter posts clips to. Each server has its own ingest token
 * (from /setup ingest); the token both authenticates the request AND selects which
 * server's clips channel to post in.
 */
export function startIngestServer(client) {
  const app = express();
  app.use(express.json({ limit: '16kb' }));

  app.get('/health', (_req, res) => res.json({ ok: true, uptime: Math.round(process.uptime()) }));

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

  app.listen(config.port, () => log.info(`Ingest server listening on :${config.port}`));
}
