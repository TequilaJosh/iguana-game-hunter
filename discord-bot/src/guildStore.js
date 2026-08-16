import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { config } from './config.js';
import { log } from './logger.js';

// Per-server config, persisted as JSON. Shape:
//   { [guildId]: { clipChannelId, welcomeChannelId, autoroleId, logChannelId, ingestToken } }
const FILE = path.join(config.dataDir, 'guilds.json');

let store = {};
let tokenIndex = {}; // ingestToken -> guildId (rebuilt from store)

function reindex() {
  tokenIndex = {};
  for (const [gid, cfg] of Object.entries(store)) {
    if (cfg && cfg.ingestToken) tokenIndex[cfg.ingestToken] = gid;
  }
}

function load() {
  try {
    store = JSON.parse(fs.readFileSync(FILE, 'utf8')) || {};
  } catch {
    store = {};
  }
  reindex();
  log.info(`Loaded config for ${Object.keys(store).length} server(s) from ${FILE}`);
}

function save() {
  try {
    fs.mkdirSync(config.dataDir, { recursive: true });
    fs.writeFileSync(FILE, JSON.stringify(store, null, 2));
  } catch (e) {
    log.error('Failed to persist guild store:', e.message);
  }
}

export function getGuild(id) {
  return store[id] || {};
}

export function setGuild(id, patch) {
  store[id] = { ...(store[id] || {}), ...patch };
  reindex();
  save();
  return store[id];
}

/** Get this server's ingest token, creating one on first use. */
export function ensureIngestToken(id) {
  const cur = store[id]?.ingestToken;
  if (cur) return cur;
  const token = crypto.randomBytes(24).toString('hex');
  setGuild(id, { ingestToken: token });
  return token;
}

export function findGuildByToken(token) {
  return token ? tokenIndex[token] || null : null;
}

load();
