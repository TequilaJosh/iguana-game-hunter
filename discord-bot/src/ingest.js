import crypto from 'node:crypto';
import express from 'express';
import { config } from './config.js';
import { postClip } from './features/clips.js';
import { log } from './logger.js';

// Constant-time token comparison so the auth check can't be timing-probed.
function tokenMatches(provided) {
  const a = Buffer.from(provided || '');
  const b = Buffer.from(config.ingestToken || '');
  return a.length === b.length && a.length > 0 && crypto.timingSafeEqual(a, b);
}

/**
 * HTTP endpoint Game Hunter posts clips to. Auth via the shared INGEST_TOKEN
 * (Authorization: Bearer <token>, or X-Ingest-Token header).
 */
export function startIngestServer(client) {
  const app = express();
  app.use(express.json({ limit: '16kb' }));

  app.get('/health', (_req, res) => res.json({ ok: true, uptime: Math.round(process.uptime()) }));

  app.post('/clip', async (req, res) => {
    if (!config.ingestToken) return res.status(503).json({ ok: false, error: 'ingest disabled (no INGEST_TOKEN)' });

    const header = req.get('authorization') || '';
    const provided = header.replace(/^Bearer\s+/i, '') || req.get('x-ingest-token') || '';
    if (!tokenMatches(provided)) return res.status(401).json({ ok: false, error: 'unauthorized' });

    const { user, url, note, game, type } = req.body || {};
    if (!url && !user) return res.status(400).json({ ok: false, error: 'need at least "user" or "url"' });

    const ok = await postClip(client, { user, url, note, game, type }).catch((e) => {
      log.error('postClip failed:', e);
      return false;
    });
    res.status(ok ? 200 : 500).json({ ok });
  });

  app.listen(config.port, () => log.info(`Ingest server listening on :${config.port}`));
}
