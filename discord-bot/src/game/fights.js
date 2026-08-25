import { MONSTERS, ZONES, ZONE_LIST } from './content.js';
import {
  derive, playerAttack, monsterAttack, pHit, rollLoot, grantXp, clamp,
} from './engine.js';

// Active fights, one per user (in-memory; a bot restart abandons in-progress fights).
const fights = new Map();

export const getFight = (uid) => fights.get(uid) || null;
export const hasFight = (uid) => fights.has(uid);
export const endFight = (uid) => fights.delete(uid);

const BUFF_TURNS = 3;
const MAGIC_TYPES = new Set(['fire', 'ice', 'lightning', 'dark', 'holy', 'magic']);

export function startFight(userId, char, monsterId, zoneId) {
  const m = MONSTERS[monsterId];
  const pd = derive(char);
  const fight = {
    userId, zoneId, monsterId,
    monster: m,
    mhp: m.stats.hp, mmaxhp: m.stats.hp,
    pd,
    php: clamp(char.hp ?? pd.maxhp, 1, pd.maxhp),
    pmp: clamp(char.mp ?? pd.maxmp, 0, pd.maxmp),
    turn: 1,
    ps: {}, // player statuses: atkUp, defUp, regen{turns,amt}
    ms: {}, // monster statuses: stun, dot{dmg,turns}
    log: [],
  };
  fights.set(userId, fight);
  return fight;
}

// Add a rolled item to a character's inventory (stacking consumables/materials).
export function addItem(char, item) {
  char.inventory = char.inventory || [];
  if (item.stackable) {
    const ex = char.inventory.find((i) => i.base === item.base && i.stackable);
    if (ex) { ex.qty = (ex.qty || 1) + (item.qty || 1); return; }
  }
  char.inventory.push(item);
}

// Parse an effect string like "damage_cut:40:2" or "attack_up:3:hp_cost:8" into
// { name, nums:[...], flags:[...] }. Numbers keep a leading +/-.
function parseEffect(eff) {
  const parts = String(eff || 'none').split(':');
  const nums = [], flags = [];
  for (const p of parts.slice(1)) (/^[+-]?\d+$/.test(p) ? nums : flags).push(/^[+-]?\d+$/.test(p) ? Number(p) : p);
  return { name: parts[0], nums, flags };
}

// Player damage with every active multiplier folded in (buffs, marked target,
// weakened target, elemental weapon affix, monster-inflicted weaken on you).
function computeDamage(fight, skill, mods = {}) {
  const r = playerAttack(fight.pd, fight.monster, skill, mods);
  let mult = 1;
  if (fight.ps.atkUp > 0) mult *= 1.3;
  if (fight.ps.dmgUp > 0) mult *= 1.35;              // beast_lore etc.
  if (fight.ps.atkDown > 0) mult *= 0.75;            // monster weakened you
  if (fight.ms.marked > 0) mult *= 1.30;             // hunter's mark on the foe
  if (fight.ms.weaken > 0) mult *= 1.15;             // all_stats_down on the foe
  if (mult !== 1) r.dmg = Math.max(1, Math.round(r.dmg * mult));
  if (fight.pd.elemDmg) r.dmg += fight.pd.elemDmg;   // elemental weapon affix (flat)
  return r;
}

// Apply player damage to the monster (handles elemental absorb → monster heals),
// then Vampiric lifesteal, marked-wake for sleep, and returns the number dealt.
function dealToMonster(fight, r, log) {
  if (r.absorbed) {
    const h = Math.min(fight.mmaxhp, fight.mhp + r.dmg) - fight.mhp;
    fight.mhp += h;
    log.push(`🌀 The ${fight.monster.name} absorbs the ${r.element} and heals **${h}**!`);
    return 0;
  }
  fight.mhp -= r.dmg;
  if (fight.ms.sleep > 0) { fight.ms.sleep = 0; log.push(`⏰ The ${fight.monster.name} jolts awake!`); }
  applyLifesteal(fight, r.dmg, log);
  return r.dmg;
}

// Heal the attacker from a Vampiric weapon (pd.lifesteal = % of damage dealt).
function applyLifesteal(fight, dmg, log) {
  const pct = fight.pd.lifesteal || 0;
  if (pct <= 0 || dmg <= 0) return;
  const h = Math.min(fight.pd.maxhp, fight.php + Math.round(dmg * pct / 100)) - fight.php;
  if (h > 0) { fight.php += h; log.push(`🩸 Your weapon drains **${h}** HP.`); }
}

function cleansePlayer(fight, log) {
  let any = false;
  for (const k of ['dot', 'stun', 'atkDown', 'slowed']) if (fight.ps[k]) { fight.ps[k] = 0; any = true; }
  if (any) log.push('✨ Ailments cleansed.');
}

// Self-buffs and self-utilities.
function applyBuff(fight, sk, eff, log) {
  const n = eff.name, v = eff.nums[0] || BUFF_TURNS;
  if (n === 'attack_up') {
    fight.ps.atkUp = v;
    const hpCost = eff.flags.includes('hp_cost') ? eff.nums[1] || 0 : 0;   // blood_rage
    if (hpCost) { const d = Math.round(fight.pd.maxhp * hpCost / 100); fight.php = Math.max(1, fight.php - d); log.push(`💪 ${sk.name} — you rage, at the cost of **${d}** HP!`); }
    else log.push(`💪 ${sk.name} — your attacks are boosted!`);
  } else if (n === 'defense_up' || n === 'damage_cut' || n === 'resist_up') { fight.ps.defUp = v; log.push(`🛡️ ${sk.name} — you brace for impact!`); }
  else if (n === 'all_stats_up') { fight.ps.atkUp = v; fight.ps.defUp = v; log.push(`🌟 ${sk.name} — everything sharpens!`); }
  else if (n === 'regen') { fight.ps.regen = { turns: v, amt: Math.round(fight.pd.maxhp * 0.08) }; log.push(`💚 ${sk.name} — you begin to regenerate.`); }
  else if (n === 'damage_up_vs_family') { fight.ps.dmgUp = eff.nums[1] || 3; log.push(`📖 ${sk.name} — you exploit the foe's weakness!`); }
  else if (n === 'evade_up') { fight.ps.evade = v; log.push(`💨 ${sk.name} — you turn hard to hit!`); }
  else if (n === 'immune') { fight.ps.immune = v; log.push(`🛡️ ${sk.name} — you're briefly invulnerable!`); }
  else if (n === 'survive_lethal') { fight.ps.survive = 1; log.push(`❤️‍🔥 ${sk.name} — you refuse to fall!`); }
  else if (n === 'counter') { fight.ps.counter = { chance: eff.nums[0] || 70, turns: eff.nums[1] || 3 }; log.push(`🥋 ${sk.name} — you ready a counter!`); }
  else if (n === 'mp_absorb') { fight.ps.mpShield = { pct: eff.nums[0] || 50, turns: eff.nums[1] || 3 }; log.push(`🔵 ${sk.name} — a shield drinks incoming blows.`); }
  else if (n === 'auto_revive') { fight.ps.autoRevive = eff.nums[0] || 50; log.push(`✝️ ${sk.name} — a revival ward settles over you.`); }
  else if (n === 'loot_up') { fight.lootMult = 1 + (eff.nums[0] || 50) / 100; log.push(`🍀 ${sk.name} — fortune favors your spoils!`); }
  else if (n === 'cleanse_all') { cleansePlayer(fight, log); log.push(`✨ ${sk.name} washes away what ails you.`); }
  else log.push(`✨ ${sk.name} takes effect.`);
}

// Debuffs applied to the monster.
function applyDebuff(fight, sk, eff, log) {
  const m = fight.monster, n = eff.name, v = eff.nums[0] || 0;
  if (n === 'marked') { fight.ms.marked = v || 3; log.push(`🎯 The ${m.name} is marked — it takes extra damage!`); }
  else if (n === 'slow') {
    if (v >= 10) { if (Math.random() * 100 < v) { fight.ms.slow = 2; log.push(`🐌 The ${m.name} is slowed!`); } else log.push(`…the ${m.name} shrugs off the slow.`); }
    else { fight.ms.slow = v || 3; log.push(`🐌 The ${m.name} is slowed!`); }
  } else if (n === 'all_stats_down') { fight.ms.weaken = v || 3; log.push(`💀 The ${m.name} is cursed — weaker and easier to wound!`); }
  else if (n === 'sleep') { if (Math.random() * 100 < (v || 40)) { fight.ms.sleep = 2; log.push(`😴 The ${m.name} falls asleep!`); } else log.push(`…the ${m.name} resists sleep.`); }
  else log.push(`✨ ${sk.name} takes hold.`);
}

function applyUtility(fight, sk, eff, log) {
  const n = eff.name;
  if (n === 'steal_gold') { const g = 5 + Math.floor(Math.random() * (10 + fight.monster.tier * 8)); fight.bonusGold = (fight.bonusGold || 0) + g; log.push(`💰 You pickpocket **${g}** gold!`); }
  else if (n === 'cleanse_all') cleansePlayer(fight, log);
  else if (n === 'auto_revive') { fight.ps.autoRevive = eff.nums[0] || 50; log.push(`✝️ ${sk.name} — a revival ward settles over you.`); }
  else log.push(`✨ ${sk.name} takes effect.`);
}

function applySkill(fight, sk, log) {
  const m = fight.monster;
  const eff = parseEffect(sk.effect);

  // Heals (may carry a rider effect: regen / cleanse_all / mp_free).
  if (sk.type === 'heal') {
    const hr = fight.pd.traits.heal_received || 1;
    const amt = Math.round((fight.pd.st.mag * 1.6 * (sk.power || 1) + fight.pd.maxhp * 0.10) * hr);
    const healed = Math.min(fight.pd.maxhp, fight.php + amt) - fight.php;
    fight.php += healed;
    log.push(`✨ ${sk.name} restores **${healed}** HP.`);
    if (eff.name === 'regen') fight.ps.regen = { turns: eff.nums[0] || BUFF_TURNS, amt: Math.round(fight.pd.maxhp * 0.08) };
    if (eff.name === 'cleanse_all') cleansePlayer(fight, log);
    if (eff.name === 'mp_free') { fight.ps.mpFree = 1; log.push('🔷 Your next skill is free.'); }
    return;
  }
  if (sk.type === 'buff') { applyBuff(fight, sk, eff, log); return; }
  if (sk.type === 'debuff') { applyDebuff(fight, sk, eff, log); return; }
  if (sk.type === 'utility') { applyUtility(fight, sk, eff, log); return; }
  if (sk.type === 'summon') {
    const base = playerAttack(fight.pd, m, sk).dmg;
    fight.ps.summon = { dmg: Math.max(1, Math.round(base)), turns: eff.nums[0] || 3 };
    log.push(`💀 ${sk.name} — a thrall rises to fight for you!`);
    return;
  }

  // Damage skill: build per-hit mods from the effect, then strike (maybe many times).
  const mods = {};
  if (eff.name === 'defense_ignore') mods.defIgnorePct = eff.nums[0] || 0;
  if (eff.name === 'crit_bonus') mods.critBonusPct = eff.nums[0] || 0;
  if (eff.name === 'guaranteed_crit') mods.guaranteedCrit = true;
  if (eff.name === 'undead_bonus' && m.family === 'undead') mods.dmgMult = 1 + (eff.nums[0] || 0) / 100;

  const hits = eff.name === 'hits' ? Math.max(1, eff.nums[0] || 1) : 1;
  // accuracy:±N — this skill can miss (normal attacks/skills never do).
  if (eff.name === 'accuracy') {
    const chance = clamp(pHit(fight.pd.st.agi, m.stats.agi) + (eff.nums[0] || 0), 5, 100);
    if (Math.random() * 100 >= chance) { log.push(`💨 ${sk.name} misses!`); return; }
  }

  let total = 0, crit = false;
  for (let h = 0; h < hits; h++) {
    if (fight.mhp <= 0) break;
    const r = computeDamage(fight, sk, mods);
    if (eff.name === 'execute_below' && (fight.mhp / fight.mmaxhp) * 100 <= (eff.nums[0] || 0)) { r.dmg = fight.mhp; log.push('☠️ Execution!'); }
    total += dealToMonster(fight, r, log);
    if (r.crit) crit = true;
  }
  log.push(`✨ ${sk.name} hits the ${m.name} for **${total}**${hits > 1 ? ` (${hits} hits)` : ''}${crit ? ' 💥 CRIT!' : ''}.`);

  // On-hit riders.
  if (eff.name === 'stun' && Math.random() * 100 < (eff.nums[0] || 0)) { fight.ms.stun = 1; log.push(`💫 The ${m.name} is stunned!`); }
  if (eff.name === 'poison' || eff.name === 'bleed') { fight.ms.dot = { dmg: Math.max(3, Math.round(fight.mmaxhp * 0.04)), turns: 3 }; log.push(`🧪 The ${m.name} is afflicted!`); }
  if (eff.name === 'blind' && Math.random() * 100 < (eff.nums[0] || 0)) { fight.ms.blind = 2; log.push(`🌫️ The ${m.name} is blinded!`); }
  if (eff.name === 'slow') applyDebuff(fight, sk, eff, log);
  if (eff.name === 'accuracy_down') { fight.ms.accDown = eff.nums[0] || 20; fight.ms.accDownTurns = 2; log.push(`🎶 The ${m.name}'s aim falters!`); }
  if (eff.name === 'marked') { fight.ms.marked = eff.nums[0] || 3; log.push(`🎯 The ${m.name} is marked!`); }
  if (eff.name === 'doom') { fight.ms.doom = eff.nums[0] || 3; log.push(`⏳ Doom! The ${m.name} will perish in ${fight.ms.doom} turns.`); }
  if (eff.name === 'all_stats_down') { fight.ms.weaken = eff.nums[0] || 3; log.push(`💀 The ${m.name} is weakened!`); }
  if (eff.name === 'self_damage') { const d = Math.round(fight.pd.maxhp * (eff.nums[0] || 0) / 100); fight.php = Math.max(1, fight.php - d); log.push(`🩹 The reckless blow costs you **${d}** HP.`); }
  if (eff.name === 'lifesteal') { const h = Math.round(total * (eff.nums[0] ? eff.nums[0] / 100 : 0.3)); fight.php = Math.min(fight.pd.maxhp, fight.php + h); log.push(`🩸 You drain **${h}** HP.`); }
}

// ── Monster abilities ────────────────────────────────────────────────────────
// Monster skill ids have no data definitions, so classify by keyword into a few
// behaviours. Returns true if a skill was used this action.
const MS_HEAVY = ['brutal_swing', 'power_strike', 'slam', 'boulder_toss', 'tail_sweep', 'wing_buffet', 'talon_rake', 'pounce', 'tidal_crash', 'earthshaker', 'dive_bomb', 'tentacle_lash', 'guard_break', 'rend', 'surge', 'undertow'];
const MS_DOT = ['venom_sting', 'spore_cloud', 'soul_burn', 'grave_chill', 'hex', 'engulf', 'web_snare'];
const MS_DISABLE = ['dread_gaze', 'warp_gaze', 'mind_spike', 'gale_screech', 'ink_spray', 'glamour', 'howl', 'entangle'];
const MS_LEECH = ['life_leech', 'curse_touch', 'soul_burn'];
const MS_SELFBUFF = ['fortify', 'chitin_guard', 'aura_shift', 'battle_cry', 'mirror_step', 'split'];
const MS_NUKE = ['dragon_breath', 'elemental_blast', 'overload_beam', 'hellfire', 'meteor'];

function monsterSkill(fight, log) {
  const m = fight.monster, pd = fight.pd;
  const skills = m.skills || [];
  if (!skills.length || Math.random() > 0.35) return false;   // 35% chance to use a skill
  const id = skills[Math.floor(Math.random() * skills.length)];
  const nm = id.replace(/_/g, ' ');

  if (MS_SELFBUFF.includes(id)) {
    if (id === 'split' || id === 'mirror_step') { const h = Math.round(m.stats.hp * 0.12); fight.mhp = Math.min(fight.mmaxhp, fight.mhp + h); log.push(`✨ The ${m.name} uses ${nm} and recovers **${h}** HP.`); }
    else { fight.ms.mdefUp = 3; log.push(`🛡️ The ${m.name} uses ${nm} and hardens its defenses!`); }
    return true;
  }
  // Everything else deals damage; classify the magnitude / rider.
  const nuke = MS_NUKE.includes(id), heavy = MS_HEAVY.includes(id);
  const a = monsterAttack(m, pd);
  let dmg = Math.round(a.dmg * (nuke ? 1.8 : heavy ? 1.5 : 1.0));
  dmg = mitigateIncoming(fight, dmg);
  fight.php -= dmg;
  log.push(`💥 The ${m.name} uses **${nm}** for **${dmg}**${nuke ? ' ✨' : ''}!`);
  if (MS_LEECH.includes(id)) { const h = Math.round(dmg * 0.5); fight.mhp = Math.min(fight.mmaxhp, fight.mhp + h); log.push(`🩸 The ${m.name} drains **${h}** HP.`); }
  if (MS_DOT.includes(id)) { fight.ps.dot = { dmg: Math.max(3, Math.round(fight.pd.maxhp * 0.03)), turns: 3 }; log.push('🧪 You are afflicted!'); }
  if (MS_DISABLE.includes(id) && Math.random() < 0.5) { fight.ps.stun = 1; log.push('💫 You are dazed and will lose your next turn!'); }
  return true;
}

// Reduce a hit about to land on the player by their active mitigations.
function mitigateIncoming(fight, dmg) {
  if (fight.ps.immune > 0) return 0;
  if (fight.ps.defUp > 0) dmg = Math.round(dmg * 0.7);
  // Race elemental resist / weakness vs this monster's element.
  var el = fight.monster.element, tr = fight.pd.traits || {};
  if (el && el !== 'none') {
    if (tr[el + '_resist']) dmg = Math.round(dmg * (1 - tr[el + '_resist'] / 100));
    if (tr[el + '_weak']) dmg = Math.round(dmg * (1 + tr[el + '_weak'] / 100));
  }
  if (fight.ps.mpShield && fight.ps.mpShield.turns > 0 && fight.pmp > 0) {
    const absorb = Math.min(fight.pmp, Math.round(dmg * fight.ps.mpShield.pct / 100));
    fight.pmp -= absorb; dmg -= absorb;
  }
  return Math.max(0, dmg);
}

function monsterTurn(fight, log) {
  const m = fight.monster, pd = fight.pd;
  if (fight.ms.sleep > 0) { fight.ms.sleep--; log.push(`😴 The ${m.name} is fast asleep.`); return; }
  if (fight.ms.stun) { fight.ms.stun = 0; log.push(`💫 The ${m.name} is stunned and can't act.`); return; }
  if (fight.ms.blind > 0) { fight.ms.blind--; log.push(`🌫️ The ${m.name} flails blindly and misses!`); return; }

  if (monsterSkill(fight, log)) return;   // used a special this turn

  let actions = m.actions_per_turn || 1;
  if (fight.ms.slow > 0) actions = 1;      // slowed → single action
  for (let i = 0; i < actions; i++) {
    if (fight.php <= 0) break;
    let hit = pHit(m.stats.agi, pd.st.agi);
    if (fight.ps.evade > 0) hit -= 35;
    if (fight.ms.accDownTurns > 0) hit -= fight.ms.accDown;
    if (Math.random() * 100 < clamp(hit, 5, 99)) {
      const a = monsterAttack(m, pd);
      let dmg = a.dmg;
      if (fight.ms.slow > 0) dmg = Math.round(dmg * 0.7);
      if (fight.ms.weaken > 0) dmg = Math.round(dmg * 0.75);
      dmg = mitigateIncoming(fight, dmg);
      fight.php -= dmg;
      log.push(`👹 The ${m.name} hits you for **${dmg}**${a.magic ? ' ✨' : ''}.`);
      // Counter-stance: strike back.
      if (fight.ps.counter && fight.ps.counter.turns > 0 && Math.random() * 100 < fight.ps.counter.chance) {
        const c = computeDamage(fight, null);
        const dealt = dealToMonster(fight, c, log);
        log.push(`🥋 You counter for **${dealt}**!`);
      }
    } else {
      log.push(`💨 The ${m.name}'s attack misses!`);
    }
  }
}

function tickStatuses(fight, log) {
  const m = fight.monster;
  // Monster damage-over-time (poison/bleed you inflicted).
  if (fight.ms.dot) {
    fight.mhp -= fight.ms.dot.dmg;
    log.push(`🧪 The ${m.name} suffers **${fight.ms.dot.dmg}** from its affliction.`);
    if (--fight.ms.dot.turns <= 0) fight.ms.dot = null;
  }
  // Doom: monster dies when the countdown ends.
  if (fight.ms.doom > 0 && --fight.ms.doom === 0) { log.push(`⏳ Doom claims the ${m.name}!`); fight.mhp = 0; }
  // Summoned thrall attacks.
  if (fight.ps.summon && fight.ps.summon.turns > 0) {
    fight.mhp -= fight.ps.summon.dmg;
    log.push(`💀 Your thrall claws the ${m.name} for **${fight.ps.summon.dmg}**.`);
    if (--fight.ps.summon.turns <= 0) { fight.ps.summon = null; log.push('💀 Your thrall crumbles to dust.'); }
  }
  // Player damage-over-time (monster inflicted).
  if (fight.ps.dot && fight.php > 0) {
    fight.php -= fight.ps.dot.dmg;
    log.push(`🧪 You suffer **${fight.ps.dot.dmg}** from poison.`);
    if (--fight.ps.dot.turns <= 0) fight.ps.dot = null;
  }
  // Player regen (skill buff).
  if (fight.ps.regen) {
    const h = Math.min(fight.pd.maxhp, fight.php + fight.ps.regen.amt) - fight.php;
    fight.php += h;
    if (h > 0) log.push(`💚 You regenerate **${h}** HP.`);
    if (--fight.ps.regen.turns <= 0) fight.ps.regen = null;
  }
  // "of the Leech" gear regen — a small heal every round while equipped.
  if (fight.pd.regen > 0 && fight.php > 0) {
    const h = Math.min(fight.pd.maxhp, fight.php + fight.pd.regen) - fight.php;
    if (h > 0) { fight.php += h; log.push(`🌿 Your gear mends **${h}** HP.`); }
  }
  // Countdown timers.
  for (const k of ['atkUp', 'defUp', 'dmgUp', 'evade', 'immune', 'atkDown', 'slowed']) if (fight.ps[k] > 0) fight.ps[k]--;
  for (const k of ['marked', 'slow', 'weaken', 'mdefUp', 'accDownTurns']) if (fight.ms[k] > 0) fight.ms[k]--;
  if (fight.ps.counter && --fight.ps.counter.turns <= 0) fight.ps.counter = null;
  if (fight.ps.mpShield && --fight.ps.mpShield.turns <= 0) fight.ps.mpShield = null;
}

// If a killing blow lands, see if the player cheats death (survive_lethal / auto_revive).
function checkDeath(fight, log) {
  if (fight.php > 0) return false;
  if (fight.ps.survive) { fight.ps.survive = 0; fight.php = 1; log.push('❤️‍🔥 You cling to life at 1 HP!'); return false; }
  if (fight.ps.autoRevive && Math.random() * 100 < fight.ps.autoRevive) {
    fight.ps.autoRevive = 0; fight.php = Math.round(fight.pd.maxhp * 0.5);
    log.push(`✝️ A revival ward triggers — you rise with **${fight.php}** HP!`);
    return false;
  }
  return true;
}

/**
 * Resolve one round. kind: 'attack' | 'skill' | 'use' | 'flee'.
 * arg: skill object (skill) or heal amount (use). Returns { log, win?, lose?, fled?, error? }.
 */
export function takeTurn(fight, kind, arg) {
  const log = [];
  const pd = fight.pd, m = fight.monster;

  // Stunned by a monster: lose the action (but the round still resolves).
  if (fight.ps.stun && (kind === 'attack' || kind === 'skill')) {
    fight.ps.stun = 0;
    log.push('💫 You are dazed and lose your turn!');
  } else if (kind === 'attack') {
    const r = computeDamage(fight, null);
    const dealt = dealToMonster(fight, r, log);
    log.push(`🗡️ You strike the ${m.name} for **${dealt}**${r.crit ? ' 💥 CRIT!' : ''}.`);
  } else if (kind === 'skill') {
    const free = fight.ps.mpFree;
    if (!free && fight.pmp < arg.mp) return { error: `Not enough MP for ${arg.name} (need ${arg.mp}, have ${fight.pmp}).` };
    if (free) { fight.ps.mpFree = 0; } else { fight.pmp -= arg.mp; }
    applySkill(fight, arg, log);
  } else if (kind === 'use') {
    const hr = fight.pd.traits.heal_received || 1;
    const healed = Math.min(fight.pd.maxhp, fight.php + Math.round(arg * hr)) - fight.php;
    fight.php += healed;
    log.push(`🧪 You quaff a potion, restoring **${healed}** HP.`);
  } else if (kind === 'flee') {
    const chance = clamp(45 + (pd.st.agi - m.stats.agi) * 2 + (pd.traits.flee_bonus || 0), 10, 95);
    if (Math.random() * 100 < chance) { log.push('🏃 You slip away from the fight.'); return { fled: true, log }; }
    log.push('🚫 You fail to escape!');
  }

  if (fight.mhp <= 0) return { win: true, log };

  monsterTurn(fight, log);
  tickStatuses(fight, log);

  if (fight.php <= 0 && checkDeath(fight, log)) return { lose: true, log };
  if (fight.mhp <= 0) return { win: true, log };
  fight.turn++;
  return { log };
}

// Grant rewards on victory and persist the character's current HP/MP.
export function resolveWin(fight, char) {
  const zone = ZONES[fight.zoneId];
  const loot = rollLoot(fight.monster, zone);
  // Ballad of Fortune (loot_up) boosts gold + XP; pickpocket adds stolen gold.
  if (fight.lootMult) { loot.gold = Math.round(loot.gold * fight.lootMult); loot.xp = Math.round(loot.xp * fight.lootMult); }
  if (fight.bonusGold) loot.gold += fight.bonusGold;
  char.gold = (char.gold || 0) + loot.gold;
  for (const it of loot.items) addItem(char, it);
  const levels = grantXp(char, loot.xp);
  const pd = derive(char);
  if (levels.length) { char.hp = pd.maxhp; char.mp = pd.maxmp; } // heal on level up
  else { char.hp = clamp(fight.php, 1, pd.maxhp); char.mp = clamp(fight.pmp, 0, pd.maxmp); }

  // Boss fight: mark the zone cleared and note the newly-unlocked zone.
  let clearedBoss = false, unlocked = null;
  if (fight.bossZone && !(char.cleared && char.cleared[fight.bossZone])) {
    char.cleared = char.cleared || {};
    char.cleared[fight.bossZone] = true;
    clearedBoss = true;
    const idx = ZONE_LIST.findIndex((z) => z.id === fight.bossZone);
    unlocked = idx >= 0 && ZONE_LIST[idx + 1] ? ZONE_LIST[idx + 1].name : null;
  }
  return { ...loot, levels, clearedBoss, unlocked };
}

// On defeat: revive at the tavern with a small gold loss (friendly — no XP loss).
export function resolveLoss(char) {
  const pd = derive(char);
  const lost = Math.round((char.gold || 0) * 0.1);
  char.gold = Math.max(0, (char.gold || 0) - lost);
  char.hp = Math.max(1, Math.round(pd.maxhp * 0.5));
  char.mp = Math.round(pd.maxmp * 0.5);
  return { lost };
}

// A weighted encounter pick for a zone.
export function pickEncounter(zone) {
  const enc = (zone.encounters || []).filter((e) => MONSTERS[e.monster]);
  if (!enc.length) return null;
  const total = enc.reduce((s, e) => s + (e.weight || 1), 0);
  let x = Math.random() * total;
  for (const e of enc) { x -= e.weight || 1; if (x <= 0) return e.monster; }
  return enc[0].monster;
}
