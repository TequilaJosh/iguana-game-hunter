import 'dotenv/config';

const get = (name) => (process.env[name] || '').trim();

export const config = {
  // Core Discord (the only required values — everything else is per-server via /setup).
  token: get('DISCORD_TOKEN'),
  clientId: get('CLIENT_ID'),

  // Ingest HTTP server.
  port: parseInt(process.env.PORT || '8080', 10),

  // Optional: the bot's public base URL, shown in /setup ingest so streamers know
  // exactly what to paste into Game Hunter (e.g. https://yourbot.up.railway.app).
  publicUrl: get('PUBLIC_URL'),

  // Where per-server config is persisted. On a host, point this at a persistent volume.
  dataDir: process.env.DATA_DIR || './data',
};

/** Throw early with a clear message if the essentials aren't set. */
export function assertCoreConfig() {
  const missing = ['token', 'clientId'].filter((k) => !config[k]);
  if (missing.length) {
    throw new Error(
      `Missing required config: ${missing.join(', ')}. ` +
        'Copy .env.example to .env and fill in DISCORD_TOKEN and CLIENT_ID.'
    );
  }
}
