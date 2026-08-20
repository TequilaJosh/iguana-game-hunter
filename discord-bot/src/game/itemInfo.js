import { ITEMS } from './content.js';
import { gearScore, sellValue } from './engine.js';

const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);
const STAT_NAME = { str: 'STR', mag: 'MAG', vit: 'VIT', spr: 'SPR', agi: 'AGI', lck: 'LCK', crit: 'Crit%' };

// Stable pick from a list, seeded by the item's id/name so an item's flavour never
// changes between page loads (no per-request randomness).
function seeded(id, list) {
  let h = 0;
  const s = String(id || '');
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
  return list[h % list.length];
}

// ── Funny flavour banks ──────────────────────────────────────────────────────
const FLAVOR = {
  sword: [
    'Pointy end goes in the other guy. You’ve got this.',
    'Perfectly balanced, as most cutlery is not.',
    'Comes with a lifetime warranty against pacifism.',
    'It’s just a very committed letter opener.',
  ],
  greatsword: [
    'Compensating for something? Absolutely. It’s working.',
    'Too big to parry, too heavy to drop. Commitment issues, meet steel.',
    'Requires two hands and a complete disregard for your back.',
    'Basically a door with ambitions.',
  ],
  axe: [
    'Great for monsters, firewood, and settling debates.',
    'Chops necks and, in a pinch, onions.',
    'The lumberjack starter pack, now with 100% more screaming.',
    'Swing first, apologise to the tree later.',
  ],
  spear: [
    'Personal space enforcement, professional grade.',
    'The original “keep away” tool.',
    'Reach advantage: because touching enemies is gross.',
    'A stick that went to finishing school.',
  ],
  dagger: [
    'Small, quiet, and full of poor decisions for your enemies.',
    'Ideal for backs, ribs, and dramatic reveals.',
    'Fits in a boot, a sleeve, or a grudge.',
    'Stabby. Efficient. Legally concerning.',
  ],
  bow: [
    'Violence, but make it long-distance and cardio-free.',
    'Solves problems from a safe, cowardly distance.',
    'String music no one enjoys but you.',
    'Point at problem. Release problem-solver.',
  ],
  knuckles: [
    'Diplomacy, delivered knuckle-first.',
    'Who needs a weapon when your fists have a resume?',
    'For when you want it to be personal.',
    'Punch now, philosophise never.',
  ],
  staff: [
    'A walking stick that occasionally explodes people.',
    'Nine parts wizard, one part fire hazard.',
    'Great for casting spells and looking wise on hikes.',
    'Do NOT use to check if the floor is lava. It knows.',
  ],
  rod: [
    'Concentrated magic on a stick. Very responsible.',
    'Smaller than a staff, twice the sass.',
    'Points at things until they regret existing.',
    'The magic wand’s intimidating older cousin.',
  ],
  mace: [
    'Subtlety is dead. You made sure of it.',
    'For enemies whose armour needs a firm talking-to.',
    'A hammer that skipped carpentry and went straight to crime.',
    'Blunt trauma, elegantly applied.',
  ],
  head: [
    'Protects the one part of you with ideas in it.',
    'Fashion-forward, concussion-backward.',
    'Keeps your brains where the manufacturer intended.',
    'Now enemies have to aim, the cowards.',
  ],
  body: [
    'Between you and a very bad afternoon.',
    'Machine washable? Absolutely not. Wear it anyway.',
    'Turns fatal blows into merely rude ones.',
    'The difference between a scar and a story.',
  ],
  shield: [
    'The “no” you can hold in your hand.',
    'Half armour, half debate-ender.',
    'Great for blocking swords, rain, and responsibilities.',
    'A door you brought to the fight on purpose.',
  ],
  feet: [
    'For running toward glory, or away from it. No judgement.',
    'Sole survivors of many bad decisions.',
    'Adds pep to your step and years to your fleeing.',
    'Because dying with cold feet is just embarrassing.',
  ],
  accessory: [
    'Does something small and mysterious. Trust it.',
    'The stat boost you can also wear to brunch.',
    'Legally distinct from a “lucky charm.” Legally.',
    'Tiny trinket, big opinions about your STR.',
  ],
  material: [
    'Looks like junk. Is, technically, junk. Craftable junk!',
    'One person’s trash is your crafting bench’s treasure.',
    'Smells faintly of adventure and mildew.',
    'Hoard it now, understand it later.',
    'A wizard will absolutely want this someday.',
  ],
  potion: [
    'Tastes like regret and cherries. Mostly cherries.',
    'Drink up — being dead is bad for your stats.',
    'Doctor-approved by absolutely no doctors.',
    'Chug it before the enemy reads this tooltip.',
  ],
  cure: [
    'Fixes what’s wrong with you. The physical part, anyway.',
    'Side effects may include: not being cursed.',
    'Emergency use only. Or Tuesdays.',
  ],
  utility: [
    'Not flashy, but it’ll save your bacon.',
    'The item you forget you have until you desperately need it.',
    'Cheat code in a bottle. We won’t tell.',
  ],
  bomb: [
    'Throw it, then look cool and don’t look back.',
    'Recess is over. Class is explosions.',
    'Handle with care, aim with malice.',
  ],
};

const EFFECT_DESC = {
  heal_pct: (it) => `Heals ${it.magnitude || 30}% of your HP`,
  heal_full: () => 'Fully restores your HP',
  mp_pct: (it) => `Restores ${it.magnitude || 30}% of your MP`,
  revive: () => 'Revives you if you’re knocked out',
  flee_guaranteed: () => 'Guarantees a clean getaway',
  stamina: (it) => `Restores ${it.magnitude || 5} stamina`,
  'damage:fire': () => 'Hurls fire damage at your foe',
  'damage:ice': () => 'Hurls ice damage at your foe',
  'damage:lightning': () => 'Hurls lightning damage at your foe',
  'cure:poison': () => 'Cures poison',
  'cure:blind': () => 'Cures blindness',
  'cure:silence': () => 'Cures silence',
  'cure:petrify': () => 'Cures petrification',
};

function flavorFor(it) {
  if (it.slot === 'material') return seeded(it.base || it.id || it.name, FLAVOR.material);
  if (it.effect) {
    if (String(it.effect).startsWith('cure:')) return seeded(it.id || it.name, FLAVOR.cure);
    if (String(it.effect).startsWith('damage:')) return seeded(it.id || it.name, FLAVOR.bomb);
    if (it.effect === 'heal_pct' || it.effect === 'heal_full' || it.effect === 'mp_pct') return seeded(it.id || it.name, FLAVOR.potion);
    return seeded(it.id || it.name, FLAVOR.utility);
  }
  if (it.slot === 'weapon') return seeded(it.id || it.name, FLAVOR[it.weapon_type] || FLAVOR.sword);
  if (FLAVOR[it.slot]) return seeded(it.id || it.name, FLAVOR[it.slot]);
  return 'Mysteriously useful. Or useless. Adventure to find out!';
}

function bonusStr(it) {
  return Object.entries(it.stat_bonus || {}).map(([k, v]) => `${v > 0 ? '+' : ''}${v} ${STAT_NAME[k] || k.toUpperCase()}`).join(' · ');
}

function statLine(it) {
  if (it.slot === 'weapon') {
    const parts = [`PWR ${it.power || 0}`];
    if (it.scales) parts.push(`scales ${STAT_NAME[it.scales] || it.scales.toUpperCase()}`);
    const b = bonusStr(it); if (b) parts.push(b);
    return parts.join(' · ');
  }
  if (['head', 'body', 'shield', 'feet'].includes(it.slot)) {
    const parts = [];
    if (it.defense) parts.push(`DEF ${it.defense}`);
    if (it.resist) parts.push(`RES ${it.resist}`);
    const b = bonusStr(it); if (b) parts.push(b);
    return parts.join(' · ') || 'Armour';
  }
  if (it.slot === 'accessory') return bonusStr(it) || 'Trinket';
  if (it.effect) return (EFFECT_DESC[it.effect] ? EFFECT_DESC[it.effect](it) : 'Consumable');
  if (it.slot === 'material') return `Tier ${it.tier || 1}${it.category ? ' ' + it.category : ''} crafting material · sells ~${sellValue(it)} 🪙`;
  return '';
}

// Compare a gear piece to what's in that slot right now.
function compareTo(it, char) {
  if (!GEAR_SLOTS.has(it.slot)) return null;
  const cur = char?.equipped?.[it.slot];
  const score = gearScore(it);
  if (!cur) return { dir: 'new', delta: score, text: 'nothing equipped there' };
  const d = score - gearScore(cur);
  return { dir: d > 0 ? 'up' : d < 0 ? 'down' : 'same', delta: d, text: cur.name };
}

/**
 * Full description for an inventory/shop item:
 *   { stats, flavor, compare:{dir,delta,text}|null }
 * Pass the item and the character (for the equipped comparison). Shop entries that
 * only carry an id are resolved to the full item automatically.
 */
export function describeItem(item, char) {
  const it = (item && (item.power != null || item.defense != null || item.effect != null || item.stat_bonus || item.slot === 'material'))
    ? item
    : (item && item.id && ITEMS[item.id]) || item || {};
  return { stats: statLine(it), flavor: flavorFor(it), compare: compareTo(it, char) };
}
