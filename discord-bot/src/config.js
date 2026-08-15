import 'dotenv/config';

const get = (name) => (process.env[name] || '').trim();

export const config = {
  // Core Discord
  token: get('DISCORD_TOKEN'),
  clientId: get('CLIENT_ID'),
  guildId: get('GUILD_ID'),

  // Channels / roles
  clipChannelId: get('CLIP_CHANNEL_ID'),
  welcomeChannelId: get('WELCOME_CHANNEL_ID'),
  autoroleId: get('AUTOROLE_ID'),
  logChannelId: get('LOG_CHANNEL_ID'),

  // Clip ingest (Game Hunter → bot)
  ingestToken: get('INGEST_TOKEN'),
  port: parseInt(process.env.PORT || '8080', 10),
};

/** Throw early with a clear message if the essentials aren't set. */
export function assertCoreConfig() {
  const missing = ['token', 'clientId', 'guildId'].filter((k) => !config[k]);
  if (missing.length) {
    throw new Error(
      `Missing required config: ${missing.join(', ')}. ` +
        'Copy .env.example to .env and fill it in.'
    );
  }
}
