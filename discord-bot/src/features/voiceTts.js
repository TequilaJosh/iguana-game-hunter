import {
  joinVoiceChannel,
  createAudioPlayer,
  createAudioResource,
  StreamType,
  AudioPlayerStatus,
  VoiceConnectionStatus,
  entersState,
  getVoiceConnection,
} from '@discordjs/voice';
import { Readable } from 'node:stream';
import { PermissionFlagsBits } from 'discord.js';
import { log } from '../logger.js';

// ── Voice TTS ───────────────────────────────────────────────────────────────
// TavernTalesBot joins a Discord voice channel and reads your stream chat aloud,
// so viewers/co-hosts in the call hear chat without any virtual-audio-cable setup.
//
// Speech uses Google Translate's free TTS endpoint (no API key). MP3 is decoded
// to Opus by ffmpeg (installed in the container). One utterance plays at a time,
// per guild, with a small queue so a burst of chat doesn't overlap.

// guildId -> { player, channelId, queue: [], speaking, readName, maxChars }
const sessions = new Map();

const GTTS_MAX = 200;          // Google TTS per-request character cap
const QUEUE_CAP = 8;           // drop chat if we're this far behind (avoid runaway lag)

function chunkText(text, size) {
  const words = text.split(/\s+/);
  const out = [];
  let cur = '';
  for (const w of words) {
    if ((cur + ' ' + w).trim().length > size) {
      if (cur) out.push(cur.trim());
      // A single word longer than the cap: hard-split it.
      if (w.length > size) {
        for (let i = 0; i < w.length; i += size) out.push(w.slice(i, i + size));
        cur = '';
      } else cur = w;
    } else {
      cur = (cur + ' ' + w).trim();
    }
  }
  if (cur) out.push(cur.trim());
  return out;
}

async function synthToBuffer(text) {
  const parts = chunkText(text, GTTS_MAX);
  const buffers = [];
  for (const part of parts) {
    const url =
      'https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=en&q=' +
      encodeURIComponent(part);
    const r = await fetch(url, {
      headers: {
        // A browser-ish UA is required or the endpoint 403s.
        'User-Agent':
          'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36',
      },
    });
    if (!r.ok) throw new Error(`TTS fetch ${r.status}`);
    buffers.push(Buffer.from(await r.arrayBuffer()));
  }
  // MP3 frames concatenate cleanly — ffmpeg decodes the joined stream as one clip.
  return Buffer.concat(buffers);
}

function pump(guildId) {
  const s = sessions.get(guildId);
  if (!s || s.speaking) return;
  const next = s.queue.shift();
  if (!next) return;
  s.speaking = true;
  synthToBuffer(next)
    .then((buf) => {
      const cur = sessions.get(guildId);
      if (!cur || cur.player !== s.player) return; // session changed/ended mid-synth
      const resource = createAudioResource(Readable.from(buf), { inputType: StreamType.Arbitrary });
      s.player.play(resource);
    })
    .catch((e) => {
      log.error('voiceTts synth failed:', e.message);
      s.speaking = false;
      pump(guildId); // skip the bad line, keep going
    });
}

/** Join (or move to) a voice channel and start reading stream chat there. */
export async function joinVoice(guild, voiceChannel) {
  const me = guild.members.me;
  const perms = voiceChannel.permissionsFor(me);
  if (!perms?.has(PermissionFlagsBits.Connect) || !perms?.has(PermissionFlagsBits.Speak)) {
    return { ok: false, reason: "I need permission to Connect and Speak in that voice channel." };
  }

  const connection = joinVoiceChannel({
    channelId: voiceChannel.id,
    guildId: guild.id,
    adapterCreator: guild.voiceAdapterCreator,
    selfDeaf: true,
    selfMute: false,
  });

  try {
    await entersState(connection, VoiceConnectionStatus.Ready, 20_000);
  } catch {
    try { connection.destroy(); } catch { /* ignore */ }
    return { ok: false, reason: "I couldn't connect to that voice channel — try again." };
  }

  // Auto-recover from a Discord voice-region move; give up if it can't resume.
  connection.on(VoiceConnectionStatus.Disconnected, async () => {
    try {
      await Promise.race([
        entersState(connection, VoiceConnectionStatus.Signalling, 5_000),
        entersState(connection, VoiceConnectionStatus.Connecting, 5_000),
      ]);
    } catch {
      leaveVoice(guild.id);
    }
  });

  const player = createAudioPlayer();
  connection.subscribe(player);
  player.on(AudioPlayerStatus.Idle, () => {
    const s = sessions.get(guild.id);
    if (s) { s.speaking = false; pump(guild.id); }
  });
  player.on('error', (e) => {
    log.error('voiceTts player error:', e.message);
    const s = sessions.get(guild.id);
    if (s) { s.speaking = false; pump(guild.id); }
  });

  sessions.set(guild.id, {
    player,
    channelId: voiceChannel.id,
    queue: [],
    speaking: false,
    readName: false,
    maxChars: 300,
  });
  log.info(`voiceTts: joined voice ${voiceChannel.name} in guild ${guild.id}`);
  return { ok: true, channel: voiceChannel };
}

/** Leave the voice channel and stop reading chat. */
export function leaveVoice(guildId) {
  const s = sessions.get(guildId);
  if (s) { try { s.player.stop(true); } catch { /* ignore */ } }
  sessions.delete(guildId);
  const conn = getVoiceConnection(guildId);
  if (conn) { try { conn.destroy(); } catch { /* ignore */ } }
  return !!s || !!conn;
}

export function isReading(guildId) {
  return sessions.has(guildId);
}

const LINK_RX = /https?:\/\/\S+/gi;

/**
 * Speak one stream-chat line, if this guild has an active voice session.
 * Cheap no-op when the bot isn't in a voice channel for the guild.
 */
export function speakStreamChat(guildId, user, text) {
  const s = sessions.get(guildId);
  if (!s) return;
  let msg = String(text || '').trim();
  if (!msg) return;

  // Strip links; skip messages that were nothing but a link.
  const stripped = msg.replace(LINK_RX, ' ').replace(/\s{2,}/g, ' ').trim();
  if (!stripped) return;
  msg = stripped;

  if (msg.length > s.maxChars) {
    const cut = msg.lastIndexOf(' ', s.maxChars - 1);
    msg = msg.slice(0, cut > s.maxChars / 2 ? cut : s.maxChars).trim();
  }

  const spoken = s.readName && user ? `${user} says: ${msg}` : msg;
  if (s.queue.length >= QUEUE_CAP) s.queue.shift(); // drop oldest when flooded
  s.queue.push(spoken);
  pump(guildId);
}

/**
 * Handle the Discord "tt vc ..." control command. Returns true if it consumed
 * the message. The streamer runs this in a text channel while sitting in the
 * voice channel they want the bot to join.
 */
export async function handleVoiceCommand(msg) {
  const content = (msg.content || '').trim();
  const m = /^tt\s+vc(?:\s+(\w+))?/i.exec(content);
  if (!m) return false;
  if (!msg.guild) { await msg.reply('Use `tt vc` in a server.').catch(() => {}); return true; }

  const sub = (m[1] || 'join').toLowerCase();

  if (sub === 'leave' || sub === 'stop' || sub === 'off') {
    const was = leaveVoice(msg.guild.id);
    await msg.reply(was ? '👋 Left the voice channel — no longer reading chat.' : "I wasn't in a voice channel.").catch(() => {});
    return true;
  }

  if (sub === 'test') {
    if (!isReading(msg.guild.id)) { await msg.reply('Join a voice channel first with `tt vc`.').catch(() => {}); return true; }
    speakStreamChat(msg.guild.id, msg.member?.displayName || 'Tester', 'Text to speech is working. Your chat will sound like this.');
    await msg.reply('🔊 Playing a test line in the voice channel.').catch(() => {});
    return true;
  }

  // join (default)
  const vc = msg.member?.voice?.channel;
  if (!vc) { await msg.reply('Join a voice channel first, then run `tt vc` and I\'ll hop in.').catch(() => {}); return true; }
  const res = await joinVoice(msg.guild, vc);
  if (!res.ok) { await msg.reply(`⚠️ ${res.reason}`).catch(() => {}); return true; }
  await msg.reply(
    `🔊 Joined **${res.channel.name}** — I'll read your stream chat aloud here. ` +
    'Stop with `tt vc leave`, test with `tt vc test`.'
  ).catch(() => {});
  return true;
}
