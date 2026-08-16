import fs from 'node:fs';
import path from 'node:path';
import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { config } from '../config.js';
import { getGuild, setGuild } from '../guildStore.js';
import { log } from '../logger.js';

// Which GitHub repo publishes Game Hunter releases (the app + this bot share it).
const REPO = process.env.UPDATE_REPO || 'TequilaJosh/iguana-game-hunter';
const STATE_FILE = path.join(config.dataDir, 'updates.json');
const POLL_MS = 30 * 60 * 1000;   // check for a new release every 30 min

function loadState() {
  try { return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')) || {}; } catch { return {}; }
}
function saveState(s) {
  try { fs.mkdirSync(config.dataDir, { recursive: true }); fs.writeFileSync(STATE_FILE, JSON.stringify(s, null, 2)); }
  catch (e) { log.error('update state save failed:', e.message); }
}

function isAdmin(msg) {
  if (!msg.guild) return false;
  if (msg.guild.ownerId === msg.author.id) return true;
  return !!msg.member?.permissions?.has(PermissionFlagsBits.ManageGuild);
}

/**
 * Handle "!gh …" admin commands. Returns true if the message was a !gh command
 * (so the caller can stop processing it). Only admins can change settings.
 */
export async function handleGhCommand(msg) {
  const content = (msg.content || '').trim();
  if (!/^!gh(\s|$)/i.test(content)) return false;

  if (!msg.guild) { await msg.reply('Run `!gh` commands in a server channel.').catch(() => {}); return true; }
  if (!isAdmin(msg)) { await msg.reply('Only server admins (Manage Server) can configure Game Hunter.').catch(() => {}); return true; }

  const parts = content.split(/\s+/).slice(1);            // drop "!gh"
  const sub = (parts[0] || '').toLowerCase();
  const arg = (parts[1] || '').toLowerCase();

  if (sub === 'setup' && (arg === 'updatechannel' || arg === 'updates')) {
    setGuild(msg.guild.id, { updateChannelId: msg.channel.id });
    await msg.reply(`✅ Game Hunter **update announcements** will post in <#${msg.channel.id}>. Turn off with \`!gh setup updateoff\`.`).catch(() => {});
    return true;
  }
  if (sub === 'setup' && (arg === 'updateoff' || arg === 'noupdates')) {
    setGuild(msg.guild.id, { updateChannelId: null });
    await msg.reply('✅ Turned off Game Hunter update announcements for this server.').catch(() => {});
    return true;
  }
  if (sub === 'setup' && (arg === 'recapchannel' || arg === 'recap')) {
    setGuild(msg.guild.id, { recapChannelId: msg.channel.id });
    await msg.reply(`✅ Stream **recaps** shared from Game Hunter will post in <#${msg.channel.id}>.`).catch(() => {});
    return true;
  }

  // Fire a sample update announcement into this server's update channel, to test the setup.
  if (sub === 'test' && (arg === 'update' || arg === 'updates')) {
    const cfg = getGuild(msg.guild.id);
    if (!cfg.updateChannelId) {
      await msg.reply('No update channel set. Run `!gh setup updatechannel` in the channel you want first.').catch(() => {});
      return true;
    }
    const ch = await msg.client.channels.fetch(cfg.updateChannelId).catch(() => null);
    if (!ch || !ch.isTextBased()) {
      await msg.reply("I can't reach the configured update channel — set it again with `!gh setup updatechannel`.").catch(() => {});
      return true;
    }
    try {
      const rel = await fetchLatest();
      await ch.send({ content: '🧪 *(test announcement)*', embeds: [releaseEmbed(rel)] });
      await msg.reply(`✅ Sent a test update announcement to <#${cfg.updateChannelId}>.`).catch(() => {});
    } catch (e) {
      await msg.reply(`Could not fetch the latest release to announce: ${e.message}`).catch(() => {});
    }
    return true;
  }

  await msg.reply(
    '**Game Hunter admin setup**\n' +
    '`!gh setup updatechannel` — announce new Game Hunter releases in this channel\n' +
    '`!gh setup updateoff` — stop update announcements\n' +
    '`!gh setup recapchannel` — post shared stream recaps in this channel\n' +
    '`!gh test update` — post a sample release announcement now (to test the channel)'
  ).catch(() => {});
  return true;
}

async function fetchLatest() {
  const r = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
    headers: { 'User-Agent': 'GameHunterBot', Accept: 'application/vnd.github+json' },
  });
  if (!r.ok) throw new Error(`GitHub ${r.status}`);
  return r.json();
}

function releaseEmbed(rel) {
  const tag = rel.tag_name || rel.name || 'new version';
  const notes = String(rel.body || '').trim().slice(0, 1500);
  const url = rel.html_url || `https://github.com/${REPO}/releases`;
  return new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle(`🦎 Game Hunter ${tag} is out`)
    .setURL(url)
    .setDescription(notes || 'A new version of Game Hunter is available.')
    .setFooter({ text: 'Game Hunter installs updates automatically on launch.' })
    .setTimestamp();
}

async function announce(client, rel) {
  const embed = releaseEmbed(rel);

  let posted = 0;
  for (const guild of client.guilds.cache.values()) {
    const cfg = getGuild(guild.id);
    if (!cfg.updateChannelId) continue;
    const ch = await client.channels.fetch(cfg.updateChannelId).catch(() => null);
    if (ch && ch.isTextBased()) { await ch.send({ embeds: [embed] }).catch(() => {}); posted++; }
  }
  if (posted) log.info(`Announced Game Hunter ${tag} to ${posted} server(s).`);
}

async function checkOnce(client) {
  try {
    const rel = await fetchLatest();
    const tag = rel.tag_name || rel.name;
    if (!tag) return;
    const state = loadState();
    if (state.lastTag === tag) return;          // already announced this one
    const firstRun = !state.lastTag;
    saveState({ lastTag: tag });
    if (firstRun) { log.info(`Update watcher baseline: latest is ${tag}.`); return; } // don't announce the current version on first boot
    await announce(client, rel);
  } catch (e) {
    log.error('update check failed:', e.message);
  }
}

export function startUpdateWatcher(client) {
  setTimeout(() => checkOnce(client), 15000);   // baseline shortly after boot
  setInterval(() => checkOnce(client), POLL_MS);
}
