import { Events, PermissionFlagsBits } from 'discord.js';
import { config, assertCoreConfig } from './config.js';
import { createClient } from './client.js';
import { commandMap } from './commands.js';
import { onMemberJoin } from './features/welcome.js';
import { startIngestServer } from './ingest.js';
import { isRpgCommand, handleRpg } from './game/rpg.js';
import { startRaidScheduler } from './game/raids.js';
import { handleGhCommand, startUpdateWatcher } from './features/updates.js';
import { log } from './logger.js';

assertCoreConfig();

const client = createClient();

client.once(Events.ClientReady, (c) => {
  log.info(`Logged in as ${c.user.tag} — serving ${c.guilds.cache.size} server(s)`);
  startIngestServer(client);
  startRaidScheduler(client); // announces raids every 6–12h per server
  startUpdateWatcher(client); // announces new Game Hunter releases to opted-in servers
});

// A new server added the bot: point their admins at /setup.
client.on(Events.GuildCreate, (guild) => {
  log.info(`Added to server "${guild.name}" (${guild.id})`);
  const ch = guild.systemChannel;
  const canSend = ch && guild.members.me && ch.permissionsFor(guild.members.me)?.has(PermissionFlagsBits.SendMessages);
  if (canSend) {
    ch.send(
      '🦎 Thanks for adding me! An admin can run **/setup view** to configure things — ' +
        'pick a clips channel (`/setup clips`), a welcome channel (`/setup welcome`), and get the ' +
        'Game Hunter ingest token (`/setup ingest`).'
    ).catch(() => {});
  }
});

// Community: welcome new members (+ optional auto-role), per server.
client.on(Events.GuildMemberAdd, (member) => onMemberJoin(member));

// Text commands (needs the Message Content intent): "!gh" admin setup + Tavern Tales.
client.on(Events.MessageCreate, (msg) => {
  if (msg.author.bot || !msg.content) return;
  if (/^!gh(\s|$)/i.test(msg.content.trim())) {
    handleGhCommand(msg).catch((e) => log.error('!gh command failed:', e));
    return;
  }
  if (!isRpgCommand(msg.content)) return;
  handleRpg(msg).catch((e) => log.error('rpg command failed:', e));
});

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
