import { REST, Routes } from 'discord.js';
import { config, assertCoreConfig } from './config.js';
import { commands } from './commands.js';
import { log } from './logger.js';

// Registers slash commands GLOBALLY so every server the bot is in gets them.
// Run once after adding/changing commands:  npm run register
// Note: global commands can take up to ~1 hour to appear in all servers the first time.
assertCoreConfig();

const rest = new REST({ version: '10' }).setToken(config.token);
const body = commands.map((c) => c.data.toJSON());

try {
  log.info(`Registering ${body.length} global slash command(s)…`);
  await rest.put(Routes.applicationCommands(config.clientId), { body });
  log.info('Done. (Global commands may take up to ~1 hour to show everywhere the first time.)');
} catch (e) {
  log.error('Failed to register commands:', e);
  process.exit(1);
}
