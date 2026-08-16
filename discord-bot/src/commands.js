import { SlashCommandBuilder, PermissionFlagsBits, ChannelType, EmbedBuilder } from 'discord.js';
import { getRecentClips } from './features/clips.js';
import { getGuild, setGuild, ensureIngestToken } from './guildStore.js';
import { config } from './config.js';

const EPHEMERAL = { ephemeral: true };

export const commands = [
  {
    data: new SlashCommandBuilder().setName('ping').setDescription('Check the bot is alive.'),
    async run(interaction) {
      await interaction.reply({ content: `🦎 Pong! ${Math.round(interaction.client.ws.ping)}ms`, ...EPHEMERAL });
    },
  },

  {
    data: new SlashCommandBuilder().setName('latest').setDescription('Show this server’s most recent clips.'),
    async run(interaction) {
      if (!interaction.guildId) return interaction.reply({ content: 'Use this in a server.', ...EPHEMERAL });
      const clips = getRecentClips(interaction.guildId, 5);
      if (clips.length === 0) return interaction.reply({ content: 'No clips posted yet.', ...EPHEMERAL });
      const lines = clips.map((c, i) => `**${i + 1}.** ${c.user || 'someone'}${c.url ? ` — ${c.url}` : ''}`);
      await interaction.reply({ content: '🎬 Recent clips:\n' + lines.join('\n') });
    },
  },

  // ── Per-server setup (admins) ───────────────────────────────────────────────
  {
    data: new SlashCommandBuilder()
      .setName('setup')
      .setDescription('Configure the bot for THIS server (admins only).')
      .setDefaultMemberPermissions(PermissionFlagsBits.ManageGuild)
      .addSubcommand((s) => s.setName('view').setDescription('Show this server’s current settings.'))
      .addSubcommand((s) =>
        s.setName('clips').setDescription('Set the channel clips are posted to.').addChannelOption((o) =>
          o.setName('channel').setDescription('Clips channel').addChannelTypes(ChannelType.GuildText).setRequired(true)
        )
      )
      .addSubcommand((s) =>
        s.setName('welcome').setDescription('Set the welcome channel.').addChannelOption((o) =>
          o.setName('channel').setDescription('Welcome channel').addChannelTypes(ChannelType.GuildText).setRequired(true)
        )
      )
      .addSubcommand((s) =>
        s.setName('autorole').setDescription('Auto-give a role to new members.').addRoleOption((o) =>
          o.setName('role').setDescription('Role for new members').setRequired(true)
        )
      )
      .addSubcommand((s) =>
        s.setName('tavern').setDescription('Set the channel for Tavern Tales raid announcements.').addChannelOption((o) =>
          o.setName('channel').setDescription('Raid announcement channel').addChannelTypes(ChannelType.GuildText).setRequired(true)
        )
      )
      .addSubcommand((s) =>
        s.setName('twitch').setDescription('Run Tavern Tales in your Twitch chat (the bot joins when you go live).').addStringOption((o) =>
          o.setName('channel').setDescription('Your Twitch channel/login (no #)').setRequired(true)
        )
      )
      .addSubcommand((s) =>
        s.setName('ingest').setDescription('Get this server’s clip ingest token for Game Hunter.')
      )
      .addSubcommand((s) =>
        s.setName('disable').setDescription('Turn a feature off.').addStringOption((o) =>
          o.setName('feature').setDescription('Which feature').setRequired(true).addChoices(
            { name: 'welcome', value: 'welcome' },
            { name: 'autorole', value: 'autorole' },
            { name: 'twitch', value: 'twitch' }
          )
        )
      ),
    async run(interaction) {
      if (!interaction.guildId) return interaction.reply({ content: 'Use this in a server.', ...EPHEMERAL });
      const gid = interaction.guildId;
      const sub = interaction.options.getSubcommand();

      if (sub === 'clips') {
        const ch = interaction.options.getChannel('channel', true);
        setGuild(gid, { clipChannelId: ch.id });
        return interaction.reply({ content: `✅ Clips will post in <#${ch.id}>.`, ...EPHEMERAL });
      }
      if (sub === 'welcome') {
        const ch = interaction.options.getChannel('channel', true);
        setGuild(gid, { welcomeChannelId: ch.id });
        return interaction.reply({ content: `✅ Welcome messages will post in <#${ch.id}>.`, ...EPHEMERAL });
      }
      if (sub === 'autorole') {
        const role = interaction.options.getRole('role', true);
        setGuild(gid, { autoroleId: role.id });
        return interaction.reply({ content: `✅ New members will get <@&${role.id}>.`, ...EPHEMERAL });
      }
      if (sub === 'tavern') {
        const ch = interaction.options.getChannel('channel', true);
        setGuild(gid, { tavernChannelId: ch.id });
        return interaction.reply({ content: `✅ Tavern Tales raids will be announced in <#${ch.id}>.`, ...EPHEMERAL });
      }
      if (sub === 'twitch') {
        const raw = interaction.options.getString('channel', true);
        const chan = raw.trim().toLowerCase()
          .replace(/^https?:\/\/(www\.)?twitch\.tv\//, '').replace(/^#/, '').replace(/[^a-z0-9_]/g, '');
        if (!chan) return interaction.reply({ content: 'That doesn’t look like a Twitch channel. Just the login, e.g. `shroud`.', ...EPHEMERAL });
        setGuild(gid, { twitchChannel: chan });
        const live = config.twitch?.clientId && config.twitch?.clientSecret ? 'when you go live' : 'now';
        return interaction.reply({
          content: `✅ Tavern Tales will run in **twitch.tv/${chan}** (${live}). Viewers play with \`tt\` commands, same as Discord.` +
            (config.twitch?.oauth ? '' : '\n⚠️ The bot owner hasn’t set up the Twitch bot account yet, so this won’t connect until they do.'),
          ...EPHEMERAL,
        });
      }
      if (sub === 'disable') {
        const f = interaction.options.getString('feature', true);
        const patch = f === 'welcome' ? { welcomeChannelId: '' } : f === 'twitch' ? { twitchChannel: '' } : { autoroleId: '' };
        setGuild(gid, patch);
        return interaction.reply({ content: `✅ Turned off ${f}.`, ...EPHEMERAL });
      }
      if (sub === 'ingest') {
        const token = ensureIngestToken(gid);
        const url = (config.publicUrl || 'https://YOUR-BOT-HOST').replace(/\/$/, '') + '/clip';
        return interaction.reply({
          content:
            '🔗 **Game Hunter → Features → Discord**\n' +
            `• **Bot ingest URL:** \`${url}\`\n` +
            `• **Ingest token:** \`${token}\`\n\n` +
            'Keep this token private — anyone with it can post to your clips channel. ' +
            'Re-run this command any time to see it again.',
          ...EPHEMERAL,
        });
      }

      // view
      const cfg = getGuild(gid);
      const embed = new EmbedBuilder()
        .setColor(0x7cc44a)
        .setTitle('This server’s settings')
        .setDescription(
          [
            `**Clips channel:** ${cfg.clipChannelId ? `<#${cfg.clipChannelId}>` : '—  (set with /setup clips)'}`,
            `**Welcome channel:** ${cfg.welcomeChannelId ? `<#${cfg.welcomeChannelId}>` : '—'}`,
            `**Raid channel:** ${cfg.tavernChannelId ? `<#${cfg.tavernChannelId}>` : '— (defaults to clips; set with /setup tavern)'}`,
            `**Auto-role:** ${cfg.autoroleId ? `<@&${cfg.autoroleId}>` : '—'}`,
            `**Twitch channel:** ${cfg.twitchChannel ? `twitch.tv/${cfg.twitchChannel}` : '— (set with /setup twitch)'}`,
            `**Clip ingest token:** ${cfg.ingestToken ? 'set ✔ (see /setup ingest)' : '— (get one with /setup ingest)'}`,
          ].join('\n')
        );
      return interaction.reply({ embeds: [embed], ...EPHEMERAL });
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
        return interaction.reply({ content: 'Can’t purge in this channel.', ...EPHEMERAL });
      }
      const deleted = await channel.bulkDelete(count, true).catch(() => null);
      await interaction.reply({
        content: deleted
          ? `🧹 Deleted ${deleted.size} message(s).`
          : 'Failed — messages older than 14 days can’t be bulk-deleted.',
        ...EPHEMERAL,
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
      await interaction.reply({ content: `🐢 Slow mode set to ${seconds}s.`, ...EPHEMERAL });
    },
  },
];

export const commandMap = new Map(commands.map((c) => [c.data.name, c]));
