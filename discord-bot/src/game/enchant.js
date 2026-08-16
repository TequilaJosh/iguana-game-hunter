import { getProf, addProfXp } from './professions.js';
import { countMat, consumeMats } from './invutil.js';

const GEAR_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const REAGENT = 'quartz';           // enchanting reagent, mined as Quartz
const REAGENT_NAME = 'Quartz';

// Tier baked into a base id like "sword_t3" (fallback 1).
function itemTier(item) {
  const m = /_t(\d+)$/.exec(item.base || '');
  return m ? parseInt(m[1], 10) : 1;
}

// How high a piece can be enchanted, based on the hero's Enchanter level.
export function enchantCap(char) {
  return Math.min(10, 3 + Math.floor(getProf(char, 'enchanter').level / 4));
}

// Cost of the NEXT enchant on this item: gold + reagent quartz.
export function nextCost(item) {
  const lvl = item.enchant || 0;
  const tier = itemTier(item);
  return { gold: 30 * (lvl + 1) * tier, quartz: lvl + 1 };
}

// Every enchantable piece (equipped first, then bag gear), for numbering.
export function enchantList(char) {
  const out = [];
  const eq = char.equipped || {};
  for (const slot of GEAR_SLOTS) if (eq[slot]) out.push({ item: eq[slot], where: 'equipped', slot });
  for (const it of char.inventory || []) if (GEAR_SLOTS.includes(it.slot)) out.push({ item: it, where: 'bag', slot: it.slot });
  return out;
}

function applyEnchant(item, tier) {
  item.enchant = (item.enchant || 0) + 1;
  if (item.power != null) item.power += Math.max(1, Math.round(item.power * 0.10) + 1);
  if (item.defense != null) item.defense += Math.max(1, tier);
  if (item.resist != null) item.resist += Math.max(1, Math.round(tier * 0.8));
  item.stat_bonus = item.stat_bonus || {};
  const sk = Object.keys(item.stat_bonus)[0];
  if (sk) item.stat_bonus[sk] += 1;
  item.value = Math.round((item.value || 10) * 1.25);
  const baseName = item.name.replace(/ \+\d+$/, '');
  item.name = `${baseName} +${item.enchant}`;
}

/**
 * Enchant one entry from enchantList(). Returns:
 *  { ok:false, reason:'maxed'|'gold'|'mats', ... } or
 *  { ok:true, item, cost, levelsGained, profLevel }
 */
export function doEnchant(char, entry) {
  const item = entry.item;
  const cap = enchantCap(char);
  if ((item.enchant || 0) >= cap) return { ok: false, reason: 'maxed', cap };

  const cost = nextCost(item);
  if ((char.gold || 0) < cost.gold) return { ok: false, reason: 'gold', cost };
  if (countMat(char, REAGENT) < cost.quartz) return { ok: false, reason: 'mats', cost };

  char.gold -= cost.gold;
  consumeMats(char, { [REAGENT]: cost.quartz });
  applyEnchant(item, itemTier(item));

  const xp = 18 + (item.enchant) * 8;
  const levelsGained = addProfXp(char, 'enchanter', xp);
  return { ok: true, item, cost, xp, levelsGained, profLevel: getProf(char, 'enchanter').level };
}

export { REAGENT_NAME };
