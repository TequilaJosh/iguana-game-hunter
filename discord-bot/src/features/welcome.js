import { config } from '../config.js';
import { log } from '../logger.js';

/** Greet a new member and (optionally) give them a starter role. */
export async function onMemberJoin(member) {
  try {
    if (config.autoroleId) {
      const role =
        member.guild.roles.cache.get(config.autoroleId) ||
        (await member.guild.roles.fetch(config.autoroleId).catch(() => null));
      if (role) {
        await member.roles.add(role).catch((e) => log.warn('Auto-role failed:', e.message));
      } else {
        log.warn(`AUTOROLE_ID ${config.autoroleId} not found in guild.`);
      }
    }

    if (config.welcomeChannelId) {
      const ch = await member.client.channels.fetch(config.welcomeChannelId).catch(() => null);
      if (ch && ch.isTextBased()) {
        await ch.send(`🦎 Welcome to the swamp, <@${member.id}>! Glad you slithered in.`);
      }
    }
  } catch (e) {
    log.error('welcome error:', e);
  }
}
