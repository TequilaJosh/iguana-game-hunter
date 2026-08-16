import { getGuild } from '../guildStore.js';
import { log } from '../logger.js';

/** Greet a new member and (optionally) give them a starter role — per this server's config. */
export async function onMemberJoin(member) {
  try {
    const cfg = getGuild(member.guild.id);

    if (cfg.autoroleId) {
      const role =
        member.guild.roles.cache.get(cfg.autoroleId) ||
        (await member.guild.roles.fetch(cfg.autoroleId).catch(() => null));
      if (role) await member.roles.add(role).catch((e) => log.warn('Auto-role failed:', e.message));
    }

    if (cfg.welcomeChannelId) {
      const ch = await member.client.channels.fetch(cfg.welcomeChannelId).catch(() => null);
      if (ch && ch.isTextBased()) {
        await ch.send(`🦎 Welcome, <@${member.id}>! Glad you're here.`);
      }
    }
  } catch (e) {
    log.error('welcome error:', e);
  }
}
