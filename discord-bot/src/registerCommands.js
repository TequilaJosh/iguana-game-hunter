import { REST, Routes } from 'discord.js';
import { config, assertCoreConfig } from './config.js';
import { commands } from './commands.js';
import { log } from './logger.js';

// Registers slash commands to your single guild (instant, unlike global commands).
// Run once after adding/changing commands:  npm run register
assertCoreConfig();

const rest = new REST({ version: '10' }).setToken(config.token);
const body = commands.map((c) => c.data.toJSON());

try {
  log.info(`Registering ${body.length} slash command(s) to guild ${config.guildId}…`);
  await rest.put(Routes.applicationGuildCommands(config.clientId, config.guildId), { body });
  log.info('Done. Commands are live in your server.');
} catch (e) {
  log.error('Failed to register commands:', e);
  process.exit(1);
}
