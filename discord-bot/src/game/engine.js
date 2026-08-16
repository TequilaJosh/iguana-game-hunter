import {
  RACES, CLASSES, ITEMS, RARITIES, AFFIXES, STAT_KEYS, ZONE_LIST, gearInTable, skillsForClass,
} from './content.js';

const EQUIP_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const GEAR_SLOTS = new Set(EQUIP_SLOTS);
const rnd = (a, b) => a + Math.random() * (b - a);
const randInt = (lo, hi) => lo + Math.floor(Math.random() * (hi - lo + 1));
export const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

// ── Character stats (ported from sim.build_player) ──────────────────────────
export function derive(char) {
  const c = CLASSES[char.cls];
  const r = RACES[char.race];
  const st = {};
  for (const k of STAT_KEYS) {
    st[k] = c.base_stats[k] + r.stat_mods[k] + Math.round(c.growth[k] * (char.level - 1));
  }
  let wpow = 0, def = 0, res = 0, critBonus = 0;
  const eq = char.equipped || {};
  for (const slot of EQUIP_SLOTS) {
    const it = eq[slot];
    if (!it) continue;
    if (it.power) wpow += it.power;
    if (it.defense) def += it.defense;
    if (it.resist) res += it.resist;
    for (const [k, v] of Object.entries(it.stat_bonus || {})) {
      if (STAT_KEYS.includes(k)) st[k] += v;
      else if (k === 'crit') critBonus += v;
    }
  }
  def += Math.round(st.vit * 0.8);
  res += Math.round(st.spr * 0.8);
  const maxhp = Math.round((60 + st.vit * 6.5 + char.level * 5) * (r.traits?.hp_mod ?? 1));
  const maxmp = Math.round((15 + st.spr * 3 + char.level * 2) * (r.traits?.mp_mod ?? 1));
  return { st, wpow, def, res, maxhp, maxmp, critBonus, scales: c.primary };
}

// ── Leveling ────────────────────────────────────────────────────────────────
export const xpToNext = (lv) => Math.round(30 * Math.pow(lv, 1.6));

export function grantXp(char, amount) {
  const xpMod = RACES[char.race].traits?.xp_mod ?? 1;
  char.xp = (char.xp || 0) + Math.round(amount * xpMod);
  const levelsGained = [];
  while (char.xp >= xpToNext(char.level)) {
    char.xp -= xpToNext(char.level);
    char.level += 1;
    levelsGained.push(char.level);
  }
  return levelsGained;
}

// ── Combat math (ported from sim.py) ────────────────────────────────────────
const DRK = 220, P_PHYS_STAT = 1.30, P_PHYS_WPN = 1.40, P_MAG = 1.90, M_PHYS = 2.10, M_MAG = 2.20;
const MAGIC_TYPES = new Set(['fire', 'ice', 'lightning', 'dark', 'holy', 'magic']);

export const dr = (d) => d / (d + DRK);
export const pHit = (aAgi, dAgi) => clamp(85 + (aAgi - dAgi) * 1.5, 35, 99);

export function playerAttack(pd, monster, skill = null) {
  const power = skill ? (skill.power || 1) : 1.0;
  const magic = !!skill && MAGIC_TYPES.has(skill.type);
  let raw, red;
  if (magic) {
    raw = pd.st.mag * P_MAG * power;
    red = dr(monster.stats.res);
  } else {
    const base = pd.st[pd.scales] ?? pd.st.str;
    raw = (base * P_PHYS_STAT + pd.wpow * P_PHYS_WPN) * power;
    red = dr(monster.stats.def);
  }
  let dmg = raw * (1 - red) * rnd(0.9, 1.1);
  const critChance = clamp(4 + pd.st.lck * 0.4 + (pd.critBonus || 0), 0, 60);
  const crit = Math.random() * 100 < critChance;
  if (crit) dmg *= 1.6;
  return { dmg: Math.max(1, Math.round(dmg)), crit, magic };
}

export function monsterAttack(monster, pd) {
  const useMag = monster.stats.mag > monster.stats.atk;
  const raw = (useMag ? monster.stats.mag * M_MAG : monster.stats.atk * M_PHYS) * rnd(0.9, 1.1);
  const red = dr(useMag ? pd.res : pd.def);
  return { dmg: Math.max(1, Math.round(raw * (1 - red))), magic: useMag };
}

// ── Loot ─────────────────────────────────────────────────────────────────────
export function rollRarity() {
  const total = RARITIES.reduce((s, r) => s + r.drop_weight, 0);
  let x = Math.random() * total;
  for (const r of RARITIES) {
    x -= r.drop_weight;
    if (x <= 0) return r;
  }
  return RARITIES[0];
}

// Build a rolled gear instance from a base item id + rarity (applies mult + affixes).
export function makeGear(baseId, rarity) {
  const base = ITEMS[baseId];
  if (!base) return null;
  const mult = rarity.stat_mult;
  const inst = {
    base: base.id, slot: base.slot, rarity: rarity.id,
    stat_bonus: { ...(base.stat_bonus || {}) }, value: Math.round((base.value || 0) * mult),
  };
  if (base.power != null) inst.power = Math.round(base.power * mult);
  if (base.defense != null) inst.defense = Math.round(base.defense * mult);
  if (base.resist != null) inst.resist = Math.round(base.resist * mult);
  if (base.scales) inst.scales = base.scales;
  if (base.weapon_type) inst.weapon_type = base.weapon_type;

  let prefix = '', suffix = '';
  const vi = clamp(rarity.affixes - 1, 0, 3);
  const usedP = new Set(), usedS = new Set();
  for (let i = 0; i < rarity.affixes; i++) {
    const usePrefix = i % 2 === 0;
    const pool = (usePrefix ? AFFIXES.prefix : AFFIXES.suffix) || [];
    const used = usePrefix ? usedP : usedS;
    const choices = pool.filter((a) => !used.has(a.id));
    if (!choices.length) continue;
    const a = choices[Math.floor(Math.random() * choices.length)];
    used.add(a.id);
    const val = a.values[Math.min(vi, a.values.length - 1)];
    inst.stat_bonus[a.stat] = (inst.stat_bonus[a.stat] || 0) + val;
    if (usePrefix && !prefix) prefix = a.name + ' ';
    else if (!usePrefix && !suffix) suffix = ' of ' + a.name;
  }
  inst.name = (prefix + base.name + suffix).trim();
  return inst;
}

// Rewards from a defeated monster: xp, gold, and item drops (+ a chance at zone gear).
export function rollLoot(monster, zone) {
  const items = [];
  for (const d of monster.drops || []) {
    if (Math.random() >= d.chance) continue;
    const base = ITEMS[d.item];
    if (!base) continue;
    const [lo, hi] = d.qty || [1, 1];
    const qty = randInt(lo, hi);
    if (GEAR_SLOTS.has(base.slot)) {
      for (let i = 0; i < qty; i++) items.push(makeGear(base.id, rollRarity()));
    } else {
      items.push(stackItem(base, qty));
    }
  }
  if (zone?.gear_table && Math.random() < 0.35) {
    const pool = gearInTable(zone.gear_table);
    if (pool.length) items.push(makeGear(pool[Math.floor(Math.random() * pool.length)].id, rollRarity()));
  }
  const xp = monster.rewards?.xp ?? 0;
  const gold = Math.round((monster.rewards?.gold ?? 0) * rnd(0.85, 1.15));
  return { items: items.filter(Boolean), xp, gold };
}

function stackItem(base, qty) {
  return {
    base: base.id, slot: base.slot, name: base.name, qty,
    stackable: !!base.stackable, effect: base.effect, magnitude: base.magnitude, value: base.value,
  };
}

// A rough score for auto-comparing gear (used by !equip hints).
export function gearScore(it) {
  let s = (it.power || 0) + (it.defense || 0) + (it.resist || 0);
  for (const v of Object.values(it.stat_bonus || {})) s += v;
  return s;
}

// The tier a character shops at — the best zone tier they can currently enter.
export function tierForLevel(level) {
  let t = 1;
  for (const z of ZONE_LIST) if (level >= z.level_required) t = z.tier;
  return t;
}

// What's for sale for this character: a tier-appropriate potion, their class weapon,
// and armor pieces (buy at item value; common rarity).
export function shopInventory(char) {
  const tier = tierForLevel(char.level);
  const cls = CLASSES[char.cls];
  const ids = [];
  for (let t = tier; t >= 1; t--) { if (ITEMS[`potion_t${t}`]) { ids.push(`potion_t${t}`); break; } }
  const wt = cls.weapons[0];
  if (ITEMS[`${wt}_t${tier}`]) ids.push(`${wt}_t${tier}`);
  for (const slot of ['head', 'body', 'shield', 'feet']) if (ITEMS[`${slot}_t${tier}`]) ids.push(`${slot}_t${tier}`);
  return ids.map((id) => ({ id, name: ITEMS[id].name, price: ITEMS[id].value || 0, slot: ITEMS[id].slot }));
}

export const sellValue = (it) => Math.max(1, Math.round((it.value || 10) * 0.4));

// Starting kit: tier-1 weapon (class's first weapon type) + basic armor + potions.
export function startingKit(char) {
  const c = CLASSES[char.cls];
  const common = RARITIES[0];
  const eq = {};
  const wpn = ITEMS[`${c.weapons[0]}_t1`];
  if (wpn) eq.weapon = makeGear(wpn.id, common);
  for (const slot of ['head', 'body', 'shield', 'feet']) {
    if (ITEMS[`${slot}_t1`]) eq[slot] = makeGear(`${slot}_t1`, common);
  }
  char.equipped = eq;
  char.inventory = [stackItem(ITEMS['potion_t1'], 3)].filter((x) => x.base);
}
