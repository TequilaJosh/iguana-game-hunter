import { EmbedBuilder } from 'discord.js';
import { getGuild } from '../guildStore.js';
import { log } from '../logger.js';

/**
 * Post a stream recap (sent by Game Hunter → /recap) as an embed in the server's
 * recap channel (falling back to the tavern, then the clips channel).
 */
export async function postRecap(client, guildId, recapText) {
  const cfg = getGuild(guildId);
  const channelId = cfg.recapChannelId || cfg.tavernChannelId || cfg.clipChannelId;
  if (!channelId) return false;

  const ch = await client.channels.fetch(channelId).catch(() => null);
  if (!ch || !ch.isTextBased()) return false;

  const text = String(recapText || '').trim().slice(0, 4000);
  if (!text) return false;

  const embed = new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle('🦎 Stream Recap')
    .setDescription(text)
    .setTimestamp();

  const sent = await ch.send({ embeds: [embed] }).catch((e) => { log.error('postRecap send failed:', e?.message); return null; });
  return !!sent;
}
