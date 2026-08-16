import { ITEMS } from './content.js';
import { addItem } from './fights.js';
import { makeGear } from './engine.js';

// Read the display name for a material/item key (gathered materials aren't in ITEMS;
// their name lives on the inventory stack, so callers pass a fallback).
export function keyName(key, fallback) {
  return ITEMS[key]?.name || fallback || key;
}

// Total quantity of a material/stackable held, matched by its inventory `base` key.
export function countMat(char, base) {
  return (char.inventory || [])
    .filter((i) => i.base === base)
    .reduce((s, i) => s + (i.qty || 1), 0);
}

// Do we have everything in `needs` ({ base: qty })?
export function hasMats(char, needs) {
  return Object.entries(needs).every(([b, q]) => countMat(char, b) >= q);
}

// Consume `needs` ({ base: qty }) from the bag. Returns false (and consumes nothing)
// if any requirement is short.
export function consumeMats(char, needs) {
  if (!hasMats(char, needs)) return false;
  char.inventory = char.inventory || [];
  for (const [base, qty] of Object.entries(needs)) {
    let left = qty;
    for (const it of char.inventory) {
      if (it.base !== base || left <= 0) continue;
      const take = Math.min(it.qty || 1, left);
      it.qty = (it.qty || 1) - take;
      left -= take;
    }
  }
  // Drop emptied stacks; keep gear (qty === undefined) untouched.
  char.inventory = char.inventory.filter((i) => i.qty == null || i.qty > 0);
  return true;
}

// Give the player a produced item. Gear is rolled from an ITEMS base at a rarity;
// consumables/materials stack.
export function giveGear(char, baseId, rarity) {
  const g = makeGear(baseId, rarity);
  if (!g) return null;
  char.inventory = char.inventory || [];
  char.inventory.push(g);
  return g;
}

export function giveStack(char, baseId, qty = 1) {
  const base = ITEMS[baseId];
  if (!base) return null;
  addItem(char, {
    base: base.id, slot: base.slot, name: base.name, qty,
    stackable: base.stackable !== false, effect: base.effect, magnitude: base.magnitude, value: base.value,
  });
  return base;
}
