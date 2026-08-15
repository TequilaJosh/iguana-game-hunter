import { Client, GatewayIntentBits, Partials } from 'discord.js';

/**
 * Intents kept minimal on purpose:
 *  - Guilds         : always required.
 *  - GuildMembers   : welcome messages + auto-role (PRIVILEGED — enable
 *                     "Server Members Intent" in the Developer Portal → Bot).
 * Moderation uses slash commands, so the privileged Message Content intent is NOT needed.
 */
export function createClient() {
  return new Client({
    intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMembers],
    partials: [Partials.Channel],
  });
}
