import { Client, GatewayIntentBits, Partials } from 'discord.js';
// (intents/partials configured below)

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
      GatewayIntentBits.GuildMessageReactions, // recipe pager: page via number reactions
    ],
    partials: [Partials.Channel, Partials.Message, Partials.Reaction],
  });
}
