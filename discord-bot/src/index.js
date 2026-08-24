import { Events, PermissionFlagsBits } from 'discord.js';
import { config, assertCoreConfig } from './config.js';
import { createClient } from './client.js';
import { commandMap } from './commands.js';
import { onMemberJoin } from './features/welcome.js';
import { startIngestServer } from './ingest.js';
import { isRpgCommand, handleRpg } from './game/rpg.js';
import { startRaidScheduler } from './game/raids.js';
import { handleGhCommand, startUpdateWatcher } from './features/updates.js';
import { startTwitch } from './features/twitch.js';
import { handleVoiceCommand } from './features/voiceTts.js';
import { log } from './logger.js';

assertCoreConfig();

const client = createClient();

// OAuth2 invite link with the permissions the bot needs (incl. Connect/Speak for
// voice TTS). Used by the "tt invite" command so a user can add it to a new server.
const INVITE_PERMS = (
  PermissionFlagsBits.ViewChannel |
  PermissionFlagsBits.SendMessages |
  PermissionFlagsBits.EmbedLinks |
  PermissionFlagsBits.ReadMessageHistory |
  PermissionFlagsBits.Connect |
  PermissionFlagsBits.Speak |
  PermissionFlagsBits.UseExternalEmojis
).toString();
const inviteUrl = () =>
  `https://discord.com/oauth2/authorize?client_id=${config.clientId}&permissions=${INVITE_PERMS}&scope=bot%20applications.commands`;

client.once(Events.ClientReady, (c) => {
  log.info(`Logged in as ${c.user.tag} — serving ${c.guilds.cache.size} server(s)`);
  startIngestServer(client);
  startRaidScheduler(client); // announces raids every 6–12h per server
  startUpdateWatcher(client); // announces new Game Hunter releases to opted-in servers
  startTwitch(client);        // Tavern Tales directly in Twitch chat (joins channels on live)
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
  // Voice TTS control: "tt vc [join|leave|test]" — handled before RPG dispatch.
  if (/^tt\s+vc(\s|$)/i.test(msg.content.trim())) {
    handleVoiceCommand(msg).catch((e) => log.error('tt vc command failed:', e));
    return;
  }
  // "tt invite" — hand out the link to add the bot to another (e.g. private) server.
  if (/^tt\s+invite(\s|$)/i.test(msg.content.trim())) {
    msg.reply(
      `➕ **Add me to another server** (great for a private friends' server — voice TTS works there, unlike group DMs):\n${inviteUrl()}\n` +
      "Open it, pick your server, then in a voice channel there run `tt vc <your_twitch_channel>`."
    ).catch(() => {});
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
