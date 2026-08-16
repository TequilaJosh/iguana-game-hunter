import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Gathering / Worker profession, ported from the TavernTales scaffold.
const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'game-data');
const load = (f) => JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));

const MATERIALS = Object.fromEntries(load('gather-materials.json').map((m) => [m.key, m]));
const AREAS = Object.fromEntries(load('gather-areas.json').map((a) => [a.key, a]));
const DROPS = load('gather-drops.json');

export const WORKER_COMMANDS = ['chop', 'mine', 'fish', 'forage', 'dig', 'scavenge'];
const COMMAND_LABEL = { chop: 'Chop', mine: 'Mine', fish: 'Fish', forage: 'Forage', dig: 'Dig', scavenge: 'Scavenge' };
const COOLDOWN_MS = 3 * 60 * 1000;
const DEFAULT_AREA = 'hollow_green';

// Worker XP curve (scaffold: 40 * level^1.6).
export const workerXpToNext = (level) => Math.round(40 * Math.pow(level, 1.6));

function ensureWorker(char) {
  char.professions = char.professions || {};
  if (!char.professions.worker) char.professions.worker = { level: 1, xp: 0 };
  return char.professions.worker;
}
export const getWorker = (char) => (char.professions && char.professions.worker) || { level: 1, xp: 0 };

function addWorkerXp(worker, amount) {
  worker.xp += amount;
  let gained = 0;
  while (worker.xp >= workerXpToNext(worker.level)) {
    worker.xp -= workerXpToNext(worker.level);
    worker.level += 1;
    gained += 1;
  }
  return gained;
}

// Materials go into the normal inventory as 'material' items so !sell junk works.
function addMaterial(char, key, mat, qty) {
  char.inventory = char.inventory || [];
  const ex = char.inventory.find((i) => i.base === key && i.slot === 'material');
  if (ex) { ex.qty = (ex.qty || 1) + qty; return; }
  char.inventory.push({ base: key, slot: 'material', name: mat.name, qty, stackable: true, value: (mat.tier || 1) * 6 });
}

function weightedPick(drops) {
  const total = drops.reduce((s, d) => s + d.weight, 0);
  let roll = Math.random() * total;
  for (const d of drops) { roll -= d.weight; if (roll <= 0) return d; }
  return drops[drops.length - 1];
}

/**
 * Run a gathering command end to end. Returns:
 *  { status: 'success'|'cooldown'|'nothing', material, qty, xp, workerLevel, levelsGained, remaining, area }
 */
export function gather(char, command) {
  const cmd = command.toLowerCase();
  if (!WORKER_COMMANDS.includes(cmd)) return { status: 'nothing' };

  char.gatherCd = char.gatherCd || {};
  const now = Date.now();
  const ready = char.gatherCd[cmd] || 0;
  if (ready > now) return { status: 'cooldown', remaining: ready - now };

  const area = AREAS[char.area] ? char.area : DEFAULT_AREA;
  const label = COMMAND_LABEL[cmd];
  const drops = DROPS.filter((d) => d.area === area && d.command === label);
  if (!drops.length) return { status: 'nothing', area: AREAS[area]?.name };

  const drop = weightedPick(drops);
  const mat = MATERIALS[drop.material];
  let qty = drop.minQuantity + Math.floor(Math.random() * (drop.maxQuantity - drop.minQuantity + 1));

  const worker = ensureWorker(char);
  if (worker.level >= 20) qty += 1; // Worker milestone: +1 base yield at 20+

  addMaterial(char, drop.material, mat, qty);
  const levelsGained = addWorkerXp(worker, drop.workerXp);
  char.gatherCd[cmd] = now + COOLDOWN_MS;

  return {
    status: 'success', material: mat.name, qty, xp: drop.workerXp,
    workerLevel: worker.level, levelsGained, area: AREAS[area]?.name,
  };
}
