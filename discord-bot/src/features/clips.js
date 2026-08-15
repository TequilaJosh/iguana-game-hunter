import { EmbedBuilder } from 'discord.js';
import { config } from '../config.js';
import { log } from '../logger.js';

// Small in-memory ring of recent clips, powering the /latest command.
const recent = [];

export function getRecentClips(n = 5) {
  return recent.slice(-n).reverse();
}

/**
 * Post a clip (or a note-only highlight) to the configured clips channel.
 * payload: { user, url?, note?, game?, type? }  (type: "clip" | "highlight")
 */
export async function postClip(client, payload) {
  const { user, url, note, game, type } = payload || {};

  if (!config.clipChannelId) {
    log.warn('CLIP_CHANNEL_ID is not set — dropping clip.');
    return false;
  }
  const channel = await client.channels.fetch(config.clipChannelId).catch(() => null);
  if (!channel || typeof channel.isTextBased !== 'function' || !channel.isTextBased()) {
    log.warn('Clip channel not found or not a text channel.');
    return false;
  }

  let message;
  if (url) {
    // Posting the raw URL lets Discord render its native clip preview.
    const who = user || 'Someone';
    const bits = [game ? `in ${game}` : '', note ? `— ${note}` : ''].filter(Boolean).join(' ');
    const head = `🎬 **${who}** shared a clip${bits ? ' ' + bits : ''}`;
    message = { content: `${head}\n${url}` };
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

  recent.push({ user, url, note, game, type, at: new Date().toISOString() });
  if (recent.length > 50) recent.shift();
  log.info(`Posted ${url ? 'clip' : 'highlight'} from ${user || 'unknown'}`);
  return true;
}
