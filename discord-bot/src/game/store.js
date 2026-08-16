import fs from 'node:fs';
import path from 'node:path';
import { config } from '../config.js';
import { log } from '../logger.js';

const FILE = path.join(config.dataDir, 'players.json');

export const MAX_STAMINA = 20;
const STAM_REGEN_MS = 6 * 60 * 1000; // 1 stamina every 6 minutes

let store = {}; // userId -> character

function load() {
  try {
    store = JSON.parse(fs.readFileSync(FILE, 'utf8')) || {};
  } catch {
    store = {};
  }
  log.info(`Loaded ${Object.keys(store).length} RPG character(s) from ${FILE}`);
}

function save() {
  try {
    fs.mkdirSync(config.dataDir, { recursive: true });
    fs.writeFileSync(FILE, JSON.stringify(store, null, 2));
  } catch (e) {
    log.error('Failed to persist players:', e.message);
  }
}

// Bring a character's stamina up to date based on elapsed time.
function regen(char) {
  const now = Date.now();
  if (char.stamina == null) { char.stamina = MAX_STAMINA; char.stamTs = now; return; }
  if (char.stamTs == null) char.stamTs = now;
  if (char.stamina >= MAX_STAMINA) { char.stamTs = now; return; }
  const gained = Math.floor((now - char.stamTs) / STAM_REGEN_MS);
  if (gained > 0) {
    char.stamina = Math.min(MAX_STAMINA, char.stamina + gained);
    char.stamTs = char.stamina >= MAX_STAMINA ? now : char.stamTs + gained * STAM_REGEN_MS;
  }
}

export function getPlayer(userId) {
  const c = store[userId];
  if (c) regen(c);
  return c || null;
}

export function savePlayer(userId, char) {
  store[userId] = char;
  save();
}

export function deletePlayer(userId) {
  delete store[userId];
  save();
}

export function allPlayers() {
  return store;
}

// Minutes:seconds until the next stamina point (null if full).
export function nextStaminaMs(char) {
  regen(char);
  if (char.stamina >= MAX_STAMINA) return null;
  return STAM_REGEN_MS - (Date.now() - char.stamTs);
}

load();
