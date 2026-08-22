import { CLASSES } from './content.js';

// Cosmetic options players can pick in their wardrobe. Stored on char.cosmetic as
// small integer indices; getLook() resolves them (with class-based defaults) into the
// actual colours/styles the sprite overlay draws.
export const SKINS = ['#f1c9a5', '#e6b892', '#c68642', '#8d5524', '#5c3a21', '#a7d3a0'];
export const HAIRS = ['#2b2b2b', '#6b4423', '#c9a227', '#b5651d', '#e8e8e8', '#8e44ad', '#c0392b', '#3a7bd5'];
export const OUTFITS = ['#4a6fa5', '#a54a4a', '#4aa55e', '#8e6fa5', '#b59a3a', '#40474f', '#c86fa0', '#2f8f8f'];
export const STYLES = ['tunic', 'robe', 'armor', 'cloak'];
export const WEAPONS = ['sword', 'greatsword', 'axe', 'mace', 'dagger', 'bow', 'staff', 'hammer'];

const ARR = { skin: SKINS, hair: HAIRS, outfit: OUTFITS, style: STYLES, weapon: WEAPONS };
const KEYS = Object.keys(ARR);

export function defaultCosmetic(char) {
  const cls = CLASSES[char?.cls] || {};
  const style = cls.armor_weight === 'heavy' ? 'armor' : cls.armor_weight === 'medium' ? 'tunic' : 'robe';
  const weapon = (cls.weapons && cls.weapons.find((w) => WEAPONS.includes(w))) || 'sword';
  // A stable default colour derived from the class name so untouched heroes still vary.
  let h = 0; const s = String(char?.cls || ''); for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
  return { skin: 0, hair: h % HAIRS.length, outfit: h % OUTFITS.length, style: Math.max(0, STYLES.indexOf(style)), weapon: Math.max(0, WEAPONS.indexOf(weapon)) };
}

// Resolve a character's cosmetic indices into concrete draw values for the overlay.
export function getLook(char) {
  const d = defaultCosmetic(char), c = char?.cosmetic || {};
  const idx = (k) => { const v = c[k]; return Number.isInteger(v) && v >= 0 && v < ARR[k].length ? v : d[k]; };
  return {
    name: char?.name || '?', cls: char?.cls || '',
    skin: SKINS[idx('skin')], hair: HAIRS[idx('hair')], outfit: OUTFITS[idx('outfit')],
    style: STYLES[idx('style')], weapon: WEAPONS[idx('weapon')],
  };
}

// The current indices (falling back to defaults) — for the wardrobe UI's selected state.
export function cosmeticIndices(char) {
  const d = defaultCosmetic(char), c = char?.cosmetic || {};
  const idx = (k) => { const v = c[k]; return Number.isInteger(v) && v >= 0 && v < ARR[k].length ? v : d[k]; };
  return { skin: idx('skin'), hair: idx('hair'), outfit: idx('outfit'), style: idx('style'), weapon: idx('weapon') };
}

// Apply a validated patch of cosmetic indices to a character.
export function setCosmetic(char, patch) {
  char.cosmetic = char.cosmetic || {};
  for (const k of KEYS) {
    if (patch[k] == null) continue;
    const v = parseInt(patch[k], 10);
    if (Number.isInteger(v) && v >= 0 && v < ARR[k].length) char.cosmetic[k] = v;
  }
  return char.cosmetic;
}

export const COSMETIC_META = { skins: SKINS, hairs: HAIRS, outfits: OUTFITS, styles: STYLES, weapons: WEAPONS };
