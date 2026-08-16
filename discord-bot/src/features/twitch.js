import { config } from '../config.js';
import { getGuild } from '../guildStore.js';
import { handleGameMessage } from '../game/bridge.js';
import { log } from '../logger.js';

// A minimal Twitch IRC client (over Node's built-in WebSocket — no dependency) that
// runs Tavern Tales in chat. It joins each server's configured channel when the
// streamer goes live, and parts when they go offline.

const IRC_URL = 'wss://irc-ws.chat.twitch.tv:443';
const POLL_MS = 60 * 1000;

let discordClient = null;
let ws = null;
let want = false;
let reconnectTimer = null;
let pollTimer = null;
let buffer = '';
const joined = new Set();          // channels we're currently in (lowercase, no #)
let chanToGuild = {};              // channel -> guildId (rebuilt each reconcile)

// app-token cache for Helix live polling
let appToken = null;
let appTokenExp = 0;

function normalizeChannel(s) {
  return String(s || '')
    .trim().toLowerCase()
    .replace(/^https?:\/\/(www\.)?twitch\.tv\//, '')
    .replace(/^#/, '')
    .replace(/[^a-z0-9_]/g, '');
}

function send(line) {
  try { if (ws && ws.readyState === 1) ws.send(line + '\r\n'); } catch { /* ignore */ }
}
function say(channel, message) {
  send(`PRIVMSG #${channel} :${message.slice(0, 480)}`);
}
function join(channel) { if (!joined.has(channel)) { send(`JOIN #${channel}`); joined.add(channel); log.info(`Twitch: joined #${channel}`); } }
function part(channel) { if (joined.has(channel)) { send(`PART #${channel}`); joined.delete(channel); log.info(`Twitch: left #${channel}`); } }

// Channels configured across all servers the bot is in: { channel: guildId }
function desiredChannels() {
  const map = {};
  if (!discordClient) return map;
  for (const g of discordClient.guilds.cache.values()) {
    const cfg = getGuild(g.id);
    const ch = normalizeChannel(cfg.twitchChannel);
    if (ch) map[ch] = g.id;
  }
  return map;
}

async function getAppToken() {
  const { clientId, clientSecret } = config.twitch;
  if (!clientId || !clientSecret) return null;
  if (appToken && Date.now() < appTokenExp - 60000) return appToken;
  try {
    const url = `https://id.twitch.tv/oauth2/token?client_id=${encodeURIComponent(clientId)}` +
      `&client_secret=${encodeURIComponent(clientSecret)}&grant_type=client_credentials`;
    const r = await fetch(url, { method: 'POST' });
    if (!r.ok) return null;
    const j = await r.json();
    appToken = j.access_token;
    appTokenExp = Date.now() + (j.expires_in || 3600) * 1000;
    return appToken;
  } catch { return null; }
}

// Which of `logins` are live right now. Returns null if live-detection isn't configured.
async function liveChannels(logins) {
  const tok = await getAppToken();
  if (!tok) return null;
  try {
    const live = new Set();
    for (let i = 0; i < logins.length; i += 100) {
      const q = logins.slice(i, i + 100).map((l) => `user_login=${encodeURIComponent(l)}`).join('&');
      const r = await fetch(`https://api.twitch.tv/helix/streams?${q}`, {
        headers: { 'Client-Id': config.twitch.clientId, Authorization: `Bearer ${tok}` },
      });
      if (!r.ok) return null;
      const j = await r.json();
      for (const s of j.data || []) live.add((s.user_login || '').toLowerCase());
    }
    return live;
  } catch { return null; }
}

// Join channels that should be active, part the rest.
async function reconcile() {
  if (!ws || ws.readyState !== 1) return;
  const desired = desiredChannels();
  chanToGuild = desired;
  const logins = Object.keys(desired);
  if (!logins.length) { for (const ch of [...joined]) part(ch); return; }

  const live = await liveChannels(logins);
  const target = live === null ? new Set(logins) : new Set([...live].filter((c) => desired[c]));

  for (const ch of target) join(ch);
  for (const ch of [...joined]) if (!target.has(ch)) part(ch);
}

function onLine(line) {
  if (!line) return;
  if (line.startsWith('PING')) { send('PONG :tmi.twitch.tv'); return; }

  // Strip IRCv3 tags (@...) if present.
  let l = line;
  if (l[0] === '@') { const sp = l.indexOf(' '); if (sp > 0) l = l.slice(sp + 1); }

  const m = l.match(/^:([^!]+)![^ ]+ PRIVMSG #(\S+) :(.*)$/);
  if (!m) return;
  const user = m[1];
  const channel = m[2].toLowerCase();
  const text = m[3].replace(/[\r\n]/g, '').trim();
  handleChat(channel, user, text).catch((e) => log.error('Twitch handleChat failed:', e));
}

async function handleChat(channel, user, text) {
  const gid = chanToGuild[channel];
  if (!gid || !text) return;
  // Only react to Tavern Tales commands, never ordinary chatter.
  const low = text.toLowerCase();
  const isCmd = low === 'tt' || low.startsWith('tt ') || low.startsWith('!');
  if (!isCmd) return;

  const reply = await handleGameMessage(discordClient, gid, 'twitch', user, text);
  if (reply) say(channel, `@${user} ${reply}`);
}

function onData(data) {
  buffer += typeof data === 'string' ? data : data.toString('utf8');
  let idx;
  while ((idx = buffer.indexOf('\r\n')) >= 0) {
    const line = buffer.slice(0, idx);
    buffer = buffer.slice(idx + 2);
    try { onLine(line); } catch (e) { log.error('Twitch onLine failed:', e); }
  }
}

function scheduleReconnect() {
  if (!want || reconnectTimer) return;
  reconnectTimer = setTimeout(() => { reconnectTimer = null; connect(); }, 4000);
}

function connect() {
  const { username, oauth } = config.twitch;
  const pass = oauth.startsWith('oauth:') ? oauth : `oauth:${oauth}`;
  try {
    ws = new WebSocket(IRC_URL);
  } catch (e) {
    log.error('Twitch connect failed:', e.message); scheduleReconnect(); return;
  }
  ws.addEventListener('open', () => {
    buffer = ''; joined.clear();
    send(`PASS ${pass}`);
    send(`NICK ${username.toLowerCase()}`);
    send('CAP REQ :twitch.tv/tags twitch.tv/commands');
    log.info('Twitch: connected to chat, reconciling channels…');
    reconcile();
  });
  ws.addEventListener('message', (ev) => onData(ev.data));
  ws.addEventListener('close', () => { joined.clear(); if (want) scheduleReconnect(); });
  ws.addEventListener('error', () => { try { ws.close(); } catch { /* ignore */ } });
}

/** Start the Twitch integration if a bot account is configured. */
export function startTwitch(client) {
  if (!config.twitch.oauth || !config.twitch.username) {
    log.info('Twitch chat integration disabled (set TWITCH_BOT_USERNAME + TWITCH_BOT_OAUTH to enable).');
    return;
  }
  discordClient = client;
  want = true;
  connect();
  pollTimer = setInterval(() => reconcile().catch(() => {}), POLL_MS);
  const mode = config.twitch.clientId && config.twitch.clientSecret ? 'join-on-live' : 'always-joined';
  log.info(`Twitch chat integration on (${mode}).`);
}
