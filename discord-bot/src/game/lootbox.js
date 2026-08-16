import { ITEMS, RARITIES } from './content.js';
import { getProf, addProfXp } from './professions.js';
import { giveGear, giveStack } from './invutil.js';
import { tierForLevel } from './engine.js';

const rnd = (a, b) => a + Math.random() * (b - a);
const randInt = (lo, hi) => lo + Math.floor(Math.random() * (hi - lo + 1));

// Precompute gear/material pools by tier from the item catalog.
const GEAR_BY_TIER = {};
const MATERIALS = [];
for (const [id, it] of Object.entries(ITEMS)) {
  if (['weapon', 'head', 'body', 'shield', 'feet', 'accessory'].includes(it.slot)) {
    (GEAR_BY_TIER[it.tier || 1] ||= []).push(id);
  } else if (it.slot === 'material') {
    MATERIALS.push({ id, tier: matTier(id) });
  }
}
function matTier(id) {
  const rank = id.split('_')[0];
  return { crude: 1, common: 2, fine: 3, superior: 4, pristine: 5, radiant: 6, mythic: 7, divine: 8 }[rank] || 1;
}
function pick(arr) { return arr[Math.floor(Math.random() * arr.length)]; }

export const boxPrice = (char) => 120 + tierForLevel(char.level) * 80;
export const getBoxes = (char) => char.boxes || 0;

function randomGear(tier) {
  for (let t = tier; t >= 1; t--) {
    const pool = [...(GEAR_BY_TIER[t] || []), ...(t < tier ? [] : GEAR_BY_TIER[t - 1] || [])];
    if (pool.length) return pick(pool);
  }
  return pick(GEAR_BY_TIER[1]);
}
function randomMat(tier) {
  const pool = MATERIALS.filter((m) => m.tier <= tier && m.tier >= Math.max(1, tier - 1));
  return (pool.length ? pick(pool) : pick(MATERIALS)).id;
}

// Lootbox rolls skew richer than normal drops, more so at higher Lootboxer level.
function boxRarity(lvl) {
  const epic = Math.min(0.12, 0.006 * lvl + 0.01);
  const rare = Math.min(0.30, 0.02 * lvl + 0.05);
  const unc = Math.min(0.55, 0.03 * lvl + 0.25);
  const r = Math.random();
  if (r < epic) return RARITIES[3];
  if (r < epic + rare) return RARITIES[2];
  if (r < epic + rare + unc) return RARITIES[1];
  return RARITIES[0];
}

// Open one box. Mutates char (adds reward + Lootboxer XP). Returns a reward summary.
export function openBox(char) {
  const tier = tierForLevel(char.level);
  const lvl = getProf(char, 'lootboxer').level;
  const r = Math.random();
  let reward;
  if (r < 0.38) {
    const gold = Math.round((80 + tier * 60) * rnd(0.7, 1.5) * (1 + lvl * 0.03));
    char.gold = (char.gold || 0) + gold;
    reward = { type: 'gold', gold };
  } else if (r < 0.68) {
    const rarity = boxRarity(lvl);
    const g = giveGear(char, randomGear(tier), rarity);
    reward = { type: 'gear', name: g?.name, rarity: rarity.id };
  } else if (r < 0.90) {
    const id = randomMat(tier);
    const qty = randInt(2, 5);
    giveStack(char, id, qty);
    reward = { type: 'mat', name: ITEMS[id].name, qty };
  } else {
    const id = `potion_t${Math.min(tier, 4)}`;
    giveStack(char, ITEMS[id] ? id : 'potion_t1', 1);
    reward = { type: 'potion', name: ITEMS[ITEMS[id] ? id : 'potion_t1'].name };
  }
  const levelsGained = addProfXp(char, 'lootboxer', 12);
  return { reward, levelsGained, profLevel: getProf(char, 'lootboxer').level };
}
