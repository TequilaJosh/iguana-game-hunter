// Shared profession progression for Tavern Tales.
// Worker (gathering) lives in gather.js but uses the same char.professions shape,
// so it shows up here too.
export const PROFESSIONS = {
  worker:    { name: 'Worker',    emoji: '⛏️', blurb: 'Gathers raw materials from the wild.' },
  crafter:   { name: 'Crafter',   emoji: '🔨', blurb: 'Forges gear from gathered materials.' },
  alchemist: { name: 'Alchemist', emoji: '⚗️', blurb: 'Brews potions and tinctures.' },
  enchanter: { name: 'Enchanter', emoji: '✨', blurb: 'Empowers gear with lasting magic.' },
  merchant:  { name: 'Merchant',  emoji: '💰', blurb: 'Haggles for better prices, buying and selling.' },
  lootboxer: { name: 'Lootboxer', emoji: '🎁', blurb: 'Cracks open mystery boxes for a living.' },
};

// Same curve the Worker profession uses (40 * level^1.6).
export const profXpToNext = (level) => Math.round(40 * Math.pow(level, 1.6));

export function ensureProf(char, key) {
  char.professions = char.professions || {};
  if (!char.professions[key]) char.professions[key] = { level: 1, xp: 0 };
  return char.professions[key];
}

export const getProf = (char, key) => (char.professions && char.professions[key]) || { level: 1, xp: 0 };

// Add XP to a profession, returning how many levels were gained.
export function addProfXp(char, key, amount) {
  const p = ensureProf(char, key);
  p.xp += amount;
  let gained = 0;
  while (p.xp >= profXpToNext(p.level)) {
    p.xp -= profXpToNext(p.level);
    p.level += 1;
    gained += 1;
  }
  return gained;
}
