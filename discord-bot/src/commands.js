import { SlashCommandBuilder, PermissionFlagsBits } from 'discord.js';
import { getRecentClips } from './features/clips.js';

/**
 * Slash commands. Add new ones here; `npm run register` pushes them to your guild.
 * Community-bot features grow by adding entries to this list (roles, giveaways, etc.).
 */
export const commands = [
  {
    data: new SlashCommandBuilder().setName('ping').setDescription('Check the bot is alive.'),
    async run(interaction) {
      const ms = Math.round(interaction.client.ws.ping);
      await interaction.reply({ content: `🦎 Pong! ${ms}ms`, ephemeral: true });
    },
  },

  {
    data: new SlashCommandBuilder().setName('latest').setDescription('Show the most recent clips.'),
    async run(interaction) {
      const clips = getRecentClips(5);
      if (clips.length === 0) {
        await interaction.reply({ content: 'No clips posted yet.', ephemeral: true });
        return;
      }
      const lines = clips.map(
        (c, i) => `**${i + 1}.** ${c.user || 'someone'}${c.url ? ` — ${c.url}` : ''}`
      );
      await interaction.reply({ content: '🎬 Recent clips:\n' + lines.join('\n') });
    },
  },

  // ── Moderation (permission-gated by Discord itself) ─────────────────────────
  {
    data: new SlashCommandBuilder()
      .setName('purge')
      .setDescription('Bulk-delete recent messages in this channel.')
      .addIntegerOption((o) =>
        o.setName('count').setDescription('How many (1–100)').setRequired(true).setMinValue(1).setMaxValue(100)
      )
      .setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages),
    async run(interaction) {
      const count = interaction.options.getInteger('count', true);
      const channel = interaction.channel;
      if (!channel || typeof channel.bulkDelete !== 'function') {
        await interaction.reply({ content: 'Can’t purge in this channel.', ephemeral: true });
        return;
      }
      const deleted = await channel.bulkDelete(count, true).catch(() => null);
      await interaction.reply({
        content: deleted
          ? `🧹 Deleted ${deleted.size} message(s).`
          : 'Failed — messages older than 14 days can’t be bulk-deleted.',
        ephemeral: true,
      });
    },
  },

  {
    data: new SlashCommandBuilder()
      .setName('slowmode')
      .setDescription('Set slow mode (seconds) for this channel.')
      .addIntegerOption((o) =>
        o.setName('seconds').setDescription('0–21600 (0 = off)').setRequired(true).setMinValue(0).setMaxValue(21600)
      )
      .setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels),
    async run(interaction) {
      const seconds = interaction.options.getInteger('seconds', true);
      await interaction.channel.setRateLimitPerUser(seconds).catch(() => null);
      await interaction.reply({ content: `🐢 Slow mode set to ${seconds}s.`, ephemeral: true });
    },
  },
];

export const commandMap = new Map(commands.map((c) => [c.data.name, c]));
