import { runForChat } from './rpg.js';
import { getLinkedDiscordId, createPendingCode, confirmCode, findDiscordMember } from './links.js';
import { log } from '../logger.js';

// Commands that manage the account link (usable before a link exists).
async function handleLink(client, guildId, platform, user, args) {
  const name = args.join(' ').trim();
  if (!name) return 'Usage: !play <your Discord @username> — I\'ll DM you a code to link your accounts.';
  const guild = client.guilds.cache.get(guildId) || (await client.guilds.fetch(guildId).catch(() => null));
  if (!guild) return 'Could not reach the Discord server. Try again shortly.';

  const member = await findDiscordMember(guild, name);
  if (!member) return `Couldn't find "${name}" in this Discord server. Use your exact Discord @username and make sure you've joined the server.`;

  const code = createPendingCode(platform, user, member.id);
  try {
    await member.send(
      `🔗 A ${platform} user "**${user}**" wants to link to your Tavern Tales hero.\n` +
      `If that's you, go back to that chat and type:  **!confirm ${code}**\n` +
      `If it wasn't you, just ignore this message.`
    );
  } catch {
    return `Found ${member.displayName} on Discord, but couldn't DM them (their DMs may be closed). Enable "Direct Messages from server members" and try again.`;
  }
  return `Sent a code to ${member.displayName} on Discord. Check your DMs, then type !confirm <code> here to link.`;
}

function handleConfirm(platform, user, args) {
  const code = (args[0] || '').trim();
  if (!code) return 'Usage: !confirm <code> — the 6-digit code I DM\'d you on Discord.';
  const r = confirmCode(platform, user, code);
  if (r.error) return `That didn't work: ${r.error}. Type !play <Discord @username> to get a new code.`;
  return "✅ Linked! Your chat account now shares your Discord hero. Try !char or !adventure.";
}

/**
 * Entry point for chat platforms (via Game Hunter). Returns a one-line reply, or null.
 * platform/user identify the chatter (e.g. "twitch"/"cooluser"); text is the raw "!command".
 */
export async function handleGameMessage(client, guildId, platform, user, text) {
  try {
    const body = String(text || '').trim().replace(/^!/, '');
    const [cmdRaw, ...args] = body.split(/\s+/);
    const cmd = (cmdRaw || '').toLowerCase();

    if (cmd === 'play' || cmd === 'link') return await handleLink(client, guildId, platform, user, args);
    if (cmd === 'confirm') return handleConfirm(platform, user, args);

    const discordId = getLinkedDiscordId(platform, user);
    if (!discordId) {
      return 'Link your account first: type !play <your Discord @username> and I\'ll DM you a code.';
    }
    const reply = await runForChat({ discordId, username: user, content: '!' + body });
    return reply || null;
  } catch (e) {
    log.error('handleGameMessage failed:', e);
    return 'Something went wrong with that command.';
  }
}
