import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { config } from '../config.js';
import { log } from '../logger.js';

// Links a service account ("twitch:cooluser") to a Discord user id, so a chatter
// plays the same character everywhere. Confirmed via a code DM'd to the Discord user.
const FILE = path.join(config.dataDir, 'links.json');
const CODE_TTL_MS = 10 * 60 * 1000;

let links = {}; // "platform:user" -> discordId
const pending = new Map(); // "platform:user" -> { code, discordId, ts }

function load() {
  try { links = JSON.parse(fs.readFileSync(FILE, 'utf8')) || {}; }
  catch { links = {}; }
  log.info(`Loaded ${Object.keys(links).length} account link(s) from ${FILE}`);
}
function save() {
  try { fs.mkdirSync(config.dataDir, { recursive: true }); fs.writeFileSync(FILE, JSON.stringify(links, null, 2)); }
  catch (e) { log.error('Failed to persist links:', e.message); }
}

export const linkKey = (platform, user) => `${(platform || '').toLowerCase()}:${(user || '').toLowerCase()}`;

export function getLinkedDiscordId(platform, user) {
  return links[linkKey(platform, user)] || null;
}

export function setLink(platform, user, discordId) {
  links[linkKey(platform, user)] = discordId;
  save();
}

export function createPendingCode(platform, user, discordId) {
  const code = String(crypto.randomInt(100000, 1000000));
  pending.set(linkKey(platform, user), { code, discordId, ts: Date.now() });
  return code;
}

export function confirmCode(platform, user, code) {
  const key = linkKey(platform, user);
  const e = pending.get(key);
  if (!e) return { error: 'no pending link — run !play <Discord @username> first' };
  if (Date.now() - e.ts > CODE_TTL_MS) { pending.delete(key); return { error: 'the code expired — run !play again' }; }
  if (e.code !== String(code).trim()) return { error: 'wrong code' };
  pending.delete(key);
  setLink(platform, user, e.discordId);
  return { discordId: e.discordId };
}

// Find a Discord member in a guild by @username / global name / display name.
export async function findDiscordMember(guild, name) {
  const q = String(name || '').replace(/^@/, '').trim();
  if (!guild || !q) return null;
  try {
    const results = await guild.members.fetch({ query: q, limit: 8 });
    const lc = q.toLowerCase();
    return (
      results.find((m) => m.user.username.toLowerCase() === lc) ||
      results.find((m) => (m.user.globalName || '').toLowerCase() === lc) ||
      results.find((m) => m.displayName.toLowerCase() === lc) ||
      results.first() ||
      null
    );
  } catch {
    return null;
  }
}

load();
