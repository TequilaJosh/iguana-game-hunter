import { Events } from 'discord.js';
import { config, assertCoreConfig } from './config.js';
import { createClient } from './client.js';
import { commandMap } from './commands.js';
import { onMemberJoin } from './features/welcome.js';
import { startIngestServer } from './ingest.js';
import { log } from './logger.js';

assertCoreConfig();

const client = createClient();

client.once(Events.ClientReady, (c) => {
  log.info(`Logged in as ${c.user.tag}`);
  startIngestServer(client); // start the Game Hunter → bot clip endpoint once connected
});

// Community: welcome new members (+ optional auto-role).
client.on(Events.GuildMemberAdd, (member) => onMemberJoin(member));

// Slash commands.
client.on(Events.InteractionCreate, async (interaction) => {
  if (!interaction.isChatInputCommand()) return;
  const cmd = commandMap.get(interaction.commandName);
  if (!cmd) return;
  try {
    await cmd.run(interaction);
  } catch (e) {
    log.error(`command "${interaction.commandName}" failed:`, e);
    const msg = { content: 'Something went wrong running that.', ephemeral: true };
    if (interaction.deferred || interaction.replied) interaction.followUp(msg).catch(() => {});
    else interaction.reply(msg).catch(() => {});
  }
});

client.on(Events.Error, (e) => log.error('client error:', e));
process.on('unhandledRejection', (e) => log.error('unhandledRejection:', e));

client.login(config.token);
