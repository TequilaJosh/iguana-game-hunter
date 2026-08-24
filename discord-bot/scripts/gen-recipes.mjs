// One-shot recipe generator for Tavern Tales.
// Produces game-data/recipes.json: crafter recipes for every gear item (t1–t8) and
// a fuller alchemist book, with XP scaled up so crafting is worth the mats.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'game-data');
const content = JSON.parse(fs.readFileSync(path.join(dir, 'content.json'), 'utf8'));
const existing = JSON.parse(fs.readFileSync(path.join(dir, 'recipes.json'), 'utf8'));

const TIER_PREFIX = ['crude', 'common', 'fine', 'superior', 'pristine', 'radiant', 'mythic', 'divine'];
const profXpToNext = (level) => Math.round(40 * Math.pow(level, 1.6));
// Crafting level gate per gear tier.
const LEVEL_REQ = [1, 4, 8, 13, 18, 24, 30, 37];
// XP is a healthy slice of the level you're gated at → high tiers pay a lot.
const xpFor = (level, weight = 1) => Math.max(20, Math.round(0.13 * profXpToNext(level) * weight));

const tierOf = (id) => { const m = /_t(\d)$/.exec(id); return m ? Number(m[1]) : 1; };
const pref = (tier) => TIER_PREFIX[tier - 1];
const mat = (tier, type) => `${pref(tier)}_${type}`;

// Low-tier (1–2) inputs use gathered mats; tier 3+ use monster mats of that tier.
const GATHER_ORE = { 1: 'copper_ore', 2: 'iron_ore' };
const GATHER_WOOD = { 1: 'softwood_log', 2: 'oak_log' };

function weaponPhysInputs(tier) {
  if (tier <= 2) return { [GATHER_ORE[tier]]: 4, [GATHER_WOOD[tier]]: 1, bone_shard: 1 };
  return { [mat(tier, 'scrap_plate')]: 3, [mat(tier, 'giant_sinew')]: 2 };
}
function weaponMagInputs(tier) {
  if (tier <= 2) return { [GATHER_WOOD[tier]]: 2, quartz: 2 };
  return { [mat(tier, 'mote')]: 2, [mat(tier, 'fae_dust')]: 2 };
}
function armorLightInputs(tier) {
  if (tier <= 2) return { [GATHER_ORE[tier]]: 2, cloth: 1, tattered_hide: 1 };
  return { [mat(tier, 'pelt')]: 2, [mat(tier, 'tattered_cloth')]: 2 };
}
function armorHeavyInputs(tier) {
  if (tier <= 2) return { [GATHER_ORE[tier]]: 4, cloth: 2 };
  return { [mat(tier, 'pelt')]: 3, [mat(tier, 'scrap_plate')]: 2 };
}
function shieldInputs(tier) {
  if (tier <= 2) return { [GATHER_WOOD[tier]]: 2, [GATHER_ORE[tier]]: 2 };
  return { [mat(tier, 'scrap_plate')]: 3, [mat(tier, 'dragon_scale')]: 1 };
}
function ringInputs(tier, focusType) {
  // focusType: the "gem" mat that flavors the ring's stat.
  if (tier <= 2) return { quartz: 2, [GATHER_ORE[tier]]: 1 };
  return { [mat(tier, focusType)]: 2, [mat(tier, 'strange_eye')]: 1 };
}

function inputsFor(item) {
  const t = tierOf(item.id);
  const s = item.slot;
  const id = item.id;
  if (s === 'weapon') return (id.startsWith('staff') || id.startsWith('rod')) ? weaponMagInputs(t) : weaponPhysInputs(t);
  if (s === 'body') return armorHeavyInputs(t);
  if (s === 'head' || s === 'feet') return armorLightInputs(t);
  if (s === 'shield') return shieldInputs(t);
  if (s === 'accessory') {
    if (id.startsWith('ring_focus') || id.startsWith('ring_spirit')) return ringInputs(t, 'mote');
    if (id.startsWith('ring_swiftness')) return ringInputs(t, 'feather');
    if (id.startsWith('charm_fortune')) return ringInputs(t, 'fae_dust');
    return ringInputs(t, 'scrap_plate'); // power / vigor
  }
  return null;
}

const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);
const items = content.items;
const byId = Object.fromEntries(items.map((i) => [i.id, i]));

// Keep every existing recipe, but refresh its XP to the boosted curve.
const out = [];
const seen = new Set();
for (const r of existing) {
  const it = byId[r.output];
  const weight = (it && (it.slot === 'body' || r.output.startsWith('greatsword'))) ? 1.25 : 1;
  out.push({ ...r, xp: xpFor(r.level, weight) });
  seen.add(`${r.prof}:${r.output}`);
}

// Add a crafter recipe for every gear item that doesn't already have one.
for (const it of items) {
  if (!GEAR_SLOTS.has(it.slot)) continue;
  if (seen.has(`crafter:${it.id}`)) continue;
  const t = tierOf(it.id);
  const level = LEVEL_REQ[t - 1] + (it.slot === 'body' ? 1 : 0);
  const weight = (it.slot === 'body' || it.id.startsWith('greatsword')) ? 1.25 : 1;
  const inputs = inputsFor(it);
  if (!inputs) continue;
  out.push({ id: it.id, prof: 'crafter', output: it.id, level, xp: xpFor(level, weight), inputs });
  seen.add(`crafter:${it.id}`);
}

// Fuller alchemist book: add the missing consumables.
const alch = [
  { output: 'potion_t3', level: 10, inputs: { [mat(3, 'seed')]: 2, [mat(3, 'gel')]: 1 } },
  { output: 'potion_t4', level: 16, inputs: { [mat(4, 'seed')]: 2, [mat(4, 'gel')]: 1 } },
  { output: 'ether_t3', level: 11, inputs: { [mat(3, 'mote')]: 2, greenleaf: 2 } },
  { output: 'revive_charm', level: 9, inputs: { [mat(3, 'bone_shard')]: 2, quartz: 2 } },
  { output: 'soft', level: 3, inputs: { clay: 2, greenleaf: 1 } },
];
for (const a of alch) {
  if (seen.has(`alchemist:${a.output}`)) continue;
  if (!byId[a.output]) continue; // skip if the item doesn't exist in this build
  out.push({ id: a.output, prof: 'alchemist', output: a.output, level: a.level, xp: xpFor(a.level), inputs: a.inputs });
  seen.add(`alchemist:${a.output}`);
}

// Stable sort: profession, then tier, then slot/name.
const slotOrder = { weapon: 0, body: 1, head: 2, shield: 3, feet: 4, accessory: 5, consumable: 6 };
out.sort((a, b) => {
  if (a.prof !== b.prof) return a.prof < b.prof ? -1 : 1;
  const ta = tierOf(a.output), tb = tierOf(b.output);
  if (ta !== tb) return ta - tb;
  const sa = slotOrder[byId[a.output]?.slot] ?? 9, sb = slotOrder[byId[b.output]?.slot] ?? 9;
  if (sa !== sb) return sa - sb;
  return a.output < b.output ? -1 : 1;
});

fs.writeFileSync(path.join(dir, 'recipes.json'), JSON.stringify(out, null, 2) + '\n');
console.log(`Wrote ${out.length} recipes (was ${existing.length}).`);
const byProf = out.reduce((m, r) => ((m[r.prof] = (m[r.prof] || 0) + 1), m), {});
console.log('by prof:', byProf);
console.log('xp range:', Math.min(...out.map((r) => r.xp)), '-', Math.max(...out.map((r) => r.xp)));
