import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ITEMS, RARITIES } from './content.js';
import { getProf, addProfXp } from './professions.js';
import { countMat, hasMats, consumeMats, giveGear, giveStack } from './invutil.js';

const dir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'game-data');
const load = (f) => JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8'));

const RECIPES = load('recipes.json');
const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);

// Material display names come from either the item catalog (monster mats) or the
// gathering catalog (raw materials).
const MAT_NAMES = {};
for (const [id, it] of Object.entries(ITEMS)) MAT_NAMES[id] = it.name;
for (const m of load('gather-materials.json')) MAT_NAMES[m.key] = m.name;
export const matName = (key) => MAT_NAMES[key] || key;

export const recipeIsGear = (r) => GEAR_SLOTS.has(ITEMS[r.output]?.slot);
export const recipeName = (r) => ITEMS[r.output]?.name || r.output;
export const listRecipes = (prof) => RECIPES.filter((r) => r.prof === prof);
export const findRecipe = (prof, id) => RECIPES.find((r) => r.prof === prof && r.id === id);

// Higher crafting level → better odds of a superior roll on crafted gear.
function craftRarity(level) {
  const epic = Math.min(0.04, 0.003 * level);
  const rare = Math.min(0.18, 0.012 * level);
  const unc = Math.min(0.55, 0.04 * level);
  const r = Math.random();
  if (r < epic) return RARITIES[3];
  if (r < epic + rare) return RARITIES[2];
  if (r < epic + rare + unc) return RARITIES[1];
  return RARITIES[0];
}

// Short "have/need" string for a recipe's inputs, e.g. "Copper Ore 2/3, Softwood Log 1/1".
export function inputsLine(char, recipe) {
  return Object.entries(recipe.inputs)
    .map(([b, q]) => `${matName(b)} ${Math.min(countMat(char, b), q)}/${q}`)
    .join(', ');
}

/**
 * Attempt a craft/brew. Returns:
 *  { ok:false, reason:'level'|'mats' } or
 *  { ok:true, gear, rarity, item, qty, levelsGained, profLevel }
 */
export function craft(char, recipe) {
  const prof = recipe.prof;
  const level = getProf(char, prof).level;
  if (level < recipe.level) return { ok: false, reason: 'level', need: recipe.level };
  if (!hasMats(char, recipe.inputs)) return { ok: false, reason: 'mats' };

  consumeMats(char, recipe.inputs);

  let gear = null, rarity = null, qty = 1;
  if (recipeIsGear(recipe)) {
    rarity = craftRarity(level);
    gear = giveGear(char, recipe.output, rarity);
  } else {
    if (prof === 'alchemist' && level >= 10) qty = 2; // master alchemist: double batch
    giveStack(char, recipe.output, qty);
  }

  const levelsGained = addProfXp(char, prof, recipe.xp);
  return { ok: true, gear, rarity, item: ITEMS[recipe.output], qty, levelsGained, profLevel: getProf(char, prof).level };
}
