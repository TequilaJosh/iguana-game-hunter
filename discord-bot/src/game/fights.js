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

function buffedPlayerDamage(fight, skill) {
  const r = playerAttack(fight.pd, fight.monster, skill);
  if (fight.ps.atkUp > 0) r.dmg = Math.round(r.dmg * 1.3);
  if (fight.pd.elemDmg) r.dmg += fight.pd.elemDmg;   // elemental weapon affixes (flat on-hit)
  return r;
}

// Heal the attacker from a Vampiric weapon (pd.lifesteal = % of damage dealt).
function applyLifesteal(fight, dmg, log) {
  const pct = fight.pd.lifesteal || 0;
  if (pct <= 0 || dmg <= 0) return;
  const h = Math.min(fight.pd.maxhp, fight.php + Math.round(dmg * pct / 100)) - fight.php;
  if (h > 0) { fight.php += h; log.push(`🩸 Your weapon drains **${h}** HP.`); }
}

function applyBuff(fight, effName, sk, log) {
  if (effName.startsWith('attack_up')) { fight.ps.atkUp = BUFF_TURNS; log.push(`💪 ${sk.name} — your attacks are boosted!`); }
  else if (effName.startsWith('defense_up') || effName === 'damage_cut' || effName === 'resist_up') { fight.ps.defUp = BUFF_TURNS; log.push(`🛡️ ${sk.name} — you brace for impact!`); }
  else if (effName === 'all_stats_up') { fight.ps.atkUp = BUFF_TURNS; fight.ps.defUp = BUFF_TURNS; log.push(`🌟 ${sk.name} — everything sharpens!`); }
  else if (effName === 'regen') { fight.ps.regen = { turns: BUFF_TURNS, amt: Math.round(fight.pd.maxhp * 0.08) }; log.push(`💚 ${sk.name} — you begin to regenerate.`); }
  else log.push(`✨ ${sk.name} takes effect.`);
}

function applySkill(fight, sk, log) {
  const m = fight.monster;
  const [effName, effRaw] = (sk.effect || 'none').split(':');
  const effVal = Number(effRaw) || 0;

  if (sk.type === 'heal') {
    const amt = Math.round(fight.pd.st.mag * 1.6 * (sk.power || 1) + fight.pd.maxhp * 0.10);
    const healed = Math.min(fight.pd.maxhp, fight.php + amt) - fight.php;
    fight.php += healed;
    log.push(`✨ ${sk.name} restores **${healed}** HP.`);
    return;
  }
  if (sk.type === 'buff' || sk.type === 'utility' || (sk.power === 0 && sk.target === 'self')) {
    applyBuff(fight, effName, sk, log);
    return;
  }

  // Damage skill.
  const r = buffedPlayerDamage(fight, sk);
  if (effName === 'guaranteed_crit' && !r.crit) r.dmg = Math.round(r.dmg * 1.6);
  if (effName === 'execute_below' && (fight.mhp / fight.mmaxhp) * 100 <= effVal) {
    r.dmg = fight.mhp;
    log.push('☠️ Execution!');
  }
  fight.mhp -= r.dmg;
  log.push(`✨ ${sk.name} hits the ${m.name} for **${r.dmg}**${r.crit ? ' 💥 CRIT!' : ''}.`);
  applyLifesteal(fight, r.dmg, log);

  if (effName === 'stun' && Math.random() * 100 < effVal) { fight.ms.stun = 1; log.push(`💫 The ${m.name} is stunned!`); }
  if (effName === 'poison' || effName === 'bleed') { fight.ms.dot = { dmg: Math.max(3, Math.round(fight.mmaxhp * 0.04)), turns: 3 }; log.push(`🧪 The ${m.name} is afflicted!`); }
  if (effName === 'lifesteal') { const h = Math.round(r.dmg * (effVal ? effVal / 100 : 0.3)); fight.php = Math.min(fight.pd.maxhp, fight.php + h); log.push(`🩸 You drain **${h}** HP.`); }
}

function monsterTurn(fight, log) {
  const m = fight.monster, pd = fight.pd;
  if (fight.ms.stun) { fight.ms.stun = 0; log.push(`💫 The ${m.name} is stunned and can't act.`); return; }
  const actions = m.actions_per_turn || 1;
  for (let i = 0; i < actions; i++) {
    if (fight.php <= 0) break;
    if (Math.random() * 100 < pHit(m.stats.agi, pd.st.agi)) {
      const a = monsterAttack(m, pd);
      let dmg = a.dmg;
      if (fight.ps.defUp > 0) dmg = Math.round(dmg * 0.7);
      fight.php -= dmg;
      log.push(`👹 The ${m.name} hits you for **${dmg}**${a.magic ? ' ✨' : ''}.`);
    } else {
      log.push(`💨 The ${m.name}'s attack misses!`);
    }
  }
}

function tickStatuses(fight, log) {
  if (fight.ms.dot) {
    fight.mhp -= fight.ms.dot.dmg;
    log.push(`🧪 The ${fight.monster.name} suffers **${fight.ms.dot.dmg}** from its affliction.`);
    if (--fight.ms.dot.turns <= 0) fight.ms.dot = null;
  }
  if (fight.ps.regen) {
    const h = Math.min(fight.pd.maxhp, fight.php + fight.ps.regen.amt) - fight.php;
    fight.php += h;
    if (h > 0) log.push(`💚 You regenerate **${h}** HP.`);
    if (--fight.ps.regen.turns <= 0) fight.ps.regen = null;
  }
  // "of the Leech" gear regen — a small heal every round, for as long as it's equipped.
  if (fight.pd.regen > 0 && fight.php > 0) {
    const h = Math.min(fight.pd.maxhp, fight.php + fight.pd.regen) - fight.php;
    if (h > 0) { fight.php += h; log.push(`🌿 Your gear mends **${h}** HP.`); }
  }
  if (fight.ps.atkUp > 0) fight.ps.atkUp--;
  if (fight.ps.defUp > 0) fight.ps.defUp--;
}

/**
 * Resolve one round. kind: 'attack' | 'skill' | 'use' | 'flee'.
 * arg: skill object (skill) or heal amount (use). Returns { log, win?, lose?, fled?, error? }.
 */
export function takeTurn(fight, kind, arg) {
  const log = [];
  const pd = fight.pd, m = fight.monster;

  if (kind === 'attack') {
    const r = buffedPlayerDamage(fight, null);
    fight.mhp -= r.dmg;
    log.push(`🗡️ You strike the ${m.name} for **${r.dmg}**${r.crit ? ' 💥 CRIT!' : ''}.`);
    applyLifesteal(fight, r.dmg, log);
  } else if (kind === 'skill') {
    if (fight.pmp < arg.mp) return { error: `Not enough MP for ${arg.name} (need ${arg.mp}, have ${fight.pmp}).` };
    fight.pmp -= arg.mp;
    applySkill(fight, arg, log);
  } else if (kind === 'use') {
    const healed = Math.min(fight.pd.maxhp, fight.php + arg) - fight.php;
    fight.php += healed;
    log.push(`🧪 You quaff a potion, restoring **${healed}** HP.`);
  } else if (kind === 'flee') {
    const chance = clamp(45 + (pd.st.agi - m.stats.agi) * 2, 10, 90);
    if (Math.random() * 100 < chance) { log.push('🏃 You slip away from the fight.'); return { fled: true, log }; }
    log.push('🚫 You fail to escape!');
  }

  if (fight.mhp <= 0) return { win: true, log };

  monsterTurn(fight, log);
  tickStatuses(fight, log);

  if (fight.php <= 0) return { lose: true, log };
  if (fight.mhp <= 0) return { win: true, log };
  fight.turn++;
  return { log };
}

// Grant rewards on victory and persist the character's current HP/MP.
export function resolveWin(fight, char) {
  const zone = ZONES[fight.zoneId];
  const loot = rollLoot(fight.monster, zone);
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
