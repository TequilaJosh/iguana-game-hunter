import { EmbedBuilder } from 'discord.js';
import { getGuild } from '../guildStore.js';
import { log } from '../logger.js';

// Recent clips per server, powering /latest.
const recentByGuild = new Map();

export function getRecentClips(guildId, n = 5) {
  const arr = recentByGuild.get(guildId) || [];
  return arr.slice(-n).reverse();
}

/**
 * Post a clip (or note-only highlight) to a specific server's clips channel.
 * payload: { user, url?, note?, game?, type? }
 */
export async function postClip(client, guildId, payload) {
  const { user, url, note, game, type } = payload || {};
  const cfg = getGuild(guildId);
  if (!cfg.clipChannelId) {
    log.warn(`Guild ${guildId} has no clips channel set (/setup clips).`);
    return false;
  }
  const channel = await client.channels.fetch(cfg.clipChannelId).catch(() => null);
  if (!channel || typeof channel.isTextBased !== 'function' || !channel.isTextBased()) {
    log.warn(`Guild ${guildId}: clips channel missing or not a text channel.`);
    return false;
  }

  let message;
  if (url) {
    const who = user || 'Someone';
    const bits = [game ? `in ${game}` : '', note ? `— ${note}` : ''].filter(Boolean).join(' ');
    message = { content: `🎬 **${who}** shared a clip${bits ? ' ' + bits : ''}\n${url}` };
  } else {
    const embed = new EmbedBuilder()
      .setColor(0x7cc44a)
      .setTitle('🎬 Highlight marked')
      .setTimestamp(new Date());
    const lines = [];
    if (user) lines.push(`**By:** ${user}`);
    if (game) lines.push(`**Game:** ${game}`);
    if (note) lines.push(`**Note:** ${note}`);
    embed.setDescription(lines.join('\n') || 'A moment was clipped.');
    message = { embeds: [embed] };
  }

  await channel.send(message);

  const arr = recentByGuild.get(guildId) || [];
  arr.push({ user, url, note, game, type, at: new Date().toISOString() });
  if (arr.length > 50) arr.shift();
  recentByGuild.set(guildId, arr);

  log.info(`Guild ${guildId}: posted ${url ? 'clip' : 'highlight'} from ${user || 'unknown'}`);
  return true;
}
