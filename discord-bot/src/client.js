import { Client, GatewayIntentBits, Partials } from 'discord.js';

/**
 * Intents:
 *  - Guilds         : always required.
 *  - GuildMembers   : welcome messages + auto-role (PRIVILEGED — enable
 *                     "Server Members Intent" in the Developer Portal → Bot).
 *  - GuildMessages + MessageContent : read "!" game commands (Tavern Tales).
 *                     MessageContent is PRIVILEGED — enable "Message Content Intent".
 */
export function createClient() {
  return new Client({
    intents: [
      GatewayIntentBits.Guilds,
      GatewayIntentBits.GuildMembers,
      GatewayIntentBits.GuildMessages,
      GatewayIntentBits.MessageContent,
      GatewayIntentBits.GuildVoiceStates, // voice TTS: join a channel & read chat aloud
    ],
    partials: [Partials.Channel],
  });
}
