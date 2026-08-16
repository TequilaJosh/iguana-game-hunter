import { EmbedBuilder } from 'discord.js';
import { MONSTERS, ZONES, ZONE_LIST } from './content.js';
import {
  derive, playerAttack, monsterAttack, rollLoot, grantXp, clamp, bossForZone, highestUnlockedZone,
} from './engine.js';
import { addItem } from './fights.js';
import { getPlayer, savePlayer } from './store.js';
import { getGuild } from '../guildStore.js';
import { log } from '../logger.js';

const LOBBY_MS = 60 * 60 * 1000;        // "starts in 1 hour" sign-up window
const COMBAT_TICK_MS = 4000;            // auto-combat tick
const COMBAT_CAP_MS = 30 * 60 * 1000;   // combat can't run forever
const RAID_HP_MULT = 15;                // boss HP is a big shared pool
const SPAWN_MIN = 60 * 60 * 1000;       // announce a raid every 1–3h at random
const SPAWN_MAX = 3 * 60 * 60 * 1000;
const SCHED_MS = 5 * 60 * 1000;

const raids = new Map();       // guildId -> raid
const nextSpawn = new Map();   // guildId -> timestamp

export const getRaid = (gid) => raids.get(gid) || null;
export const hasRaid = (gid) => raids.has(gid);
const rand = (lo, hi) => lo + Math.random() * (hi - lo);

// Where to announce raids: the dedicated Tavern Tales channel, else the clips channel.
const announceChannelId = (gid) => { const c = getGuild(gid); return c.tavernChannelId || c.clipChannelId || ''; };
const mins = (ms) => Math.max(0, Math.round(ms / 60000));

function bar(c, m, w = 16) { const p = clamp(m > 0 ? c / m : 0, 0, 1); const f = Math.round(p * w); return '`' + '█'.repeat(f) + '░'.repeat(w - f) + '`'; }

export function raidEmbed(raid) {
  const zone = ZONES[raid.zoneId];
  const roster = [...raid.parts.values()].slice(0, 15).map((p) => raid.phase === 'lobby'
    ? `• ${p.name}`
    : `${p.downed ? '💀' : '🗡️'} ${p.name} — ${Math.max(0, p.hp)}/${p.maxhp} · ${p.dmg} dmg`).join('\n') || '_No one yet — be the first!_';
  const e = new EmbedBuilder().setColor(raid.phase === 'lobby' ? 0xd6a92f : raid.hp <= 0 ? 0x3fa34d : 0x9a4fd6);
  if (raid.phase === 'lobby') {
    return e.setTitle(`🐉 RAID INCOMING — ${raid.boss.name}`)
      .setDescription(
        `**Difficulty:** T${zone.tier} · ${zone.name}\n` +
        `⏳ Starts in **${mins(raid.startsAt - Date.now())}m** · 👥 ${raid.parts.size} signed up\n\n` +
        `**Signed up**\n${roster}\n\n_Type **!raid join** to sign up!_`
      );
  }
  return e.setTitle(`🐉 RAID — ${raid.boss.name}`)
    .setDescription(
      `**Difficulty:** T${zone.tier} · ${zone.name}\n` +
      `${bar(raid.hp, raid.maxhp)}  ${Math.max(0, raid.hp)}/${raid.maxhp}\n` +
      `⏳ ${mins(raid.combatEndsAt - Date.now())}m left · 👥 ${raid.parts.size} raiders\n\n` +
      `**Raiders**\n${roster}\n\n_**!raid skill <name>** · **!raid use** · **!raid revive**_`
    );
}

function addParticipant(raid, discordId) {
  if (raid.parts.has(discordId)) return raid.parts.get(discordId);
  const char = getPlayer(discordId);
  if (!char) return null;
  const pd = derive(char);
  const p = { discordId, name: char.name, pd, dmg: 0, hp: pd.maxhp, maxhp: pd.maxhp, downed: false, queued: null };
  raid.parts.set(discordId, p);
  return p;
}

// Announce a raid with a sign-up lobby (lobbyMs=0 to start combat immediately).
export function spawnRaid(guildId, zone, channel, lobbyMs = LOBBY_MS) {
  if (raids.has(guildId)) return { error: 'A raid is already active — `!raid join`!' };
  const boss = MONSTERS[bossForZone(zone)];
  const maxhp = boss.stats.hp * RAID_HP_MULT;
  const raid = {
    guildId, zoneId: zone.id, boss, hp: maxhp, maxhp,
    phase: 'lobby', startsAt: Date.now() + lobbyMs, combatEndsAt: 0,
    channel: channel || null, message: null, parts: new Map(), timer: null, lobbyTimer: null,
  };
  raids.set(guildId, raid);
  raid.lobbyTimer = setTimeout(() => beginCombat(guildId).catch(() => {}), Math.max(0, lobbyMs));
  return { raid, zone };
}

export function startRaid(guildId, starterChar, starterId, channel) {
  const zone = highestUnlockedZone(starterChar);
  const r = spawnRaid(guildId, zone, channel, 0); // manual = start now
  if (r.error) return r;
  addParticipant(r.raid, starterId);
  return r;
}

export function joinRaid(guildId, discordId) {
  const raid = raids.get(guildId); if (!raid) return { error: 'No raid right now. They’re announced every 1–3 hours.' };
  const p = addParticipant(raid, discordId);
  if (!p) return { error: 'Make a hero first with `!create`.' };
  return { raid, p, phase: raid.phase };
}

export function raidAction(guildId, discordId, action) {
  const raid = raids.get(guildId); if (!raid) return { error: 'No active raid.' };
  const p = addParticipant(raid, discordId); if (!p) return { error: 'Make a hero first with `!create`.' };
  if (action.kind === 'revive') {
    if (!p.downed) return { error: 'You’re still standing.' };
    p.downed = false; p.hp = Math.max(1, Math.round(p.maxhp * 0.5));
    return { raid, revived: true };
  }
  p.queued = action;
  return { raid, queued: true };
}

async function beginCombat(guildId) {
  const raid = raids.get(guildId); if (!raid) return;
  if (raid.parts.size === 0) { // nobody signed up
    if (raid.channel) raid.channel.send(`🐉 The raid on **${raid.boss.name}** fizzled — no one signed up.`).catch(() => {});
    clearTimeout(raid.lobbyTimer); raids.delete(guildId); return;
  }
  raid.phase = 'combat';
  raid.combatEndsAt = Date.now() + COMBAT_CAP_MS;
  raid.timer = setInterval(() => tick(guildId).catch(() => {}), COMBAT_TICK_MS);
  if (raid.channel) raid.channel.send(`⚔️ **The raid on ${raid.boss.name} begins!** ${raid.parts.size} brave raiders — good luck!`).catch(() => {});
  await renderRaid(raid).catch(() => {});
}

async function tick(guildId) {
  const raid = raids.get(guildId); if (!raid || raid.phase !== 'combat') return;
  if (Date.now() >= raid.combatEndsAt) return finishRaid(guildId, 'timeout');
  const boss = raid.boss;
  const active = [...raid.parts.values()].filter((p) => !p.downed);

  for (const p of active) {
    const act = p.queued; p.queued = null;
    if (act?.kind === 'use') {
      const char = getPlayer(p.discordId);
      const pot = (char?.inventory || []).find((i) => i.effect === 'heal_pct' && (i.qty || 0) > 0);
      if (pot) { p.hp = Math.min(p.maxhp, p.hp + Math.round(p.maxhp * (pot.magnitude || 30) / 100)); pot.qty -= 1; if (pot.qty <= 0) char.inventory = char.inventory.filter((i) => i !== pot); savePlayer(p.discordId, char); }
      continue;
    }
    const r = playerAttack(p.pd, boss, act?.kind === 'skill' ? act.skill : null);
    raid.hp = Math.max(0, raid.hp - r.dmg);
    p.dmg += r.dmg;
    if (raid.hp <= 0) break;
  }
  if (raid.hp <= 0) return finishRaid(guildId, 'defeated');

  const targets = active.filter((p) => !p.downed);
  const swings = Math.min(targets.length, (boss.actions_per_turn || 1) + 1);
  for (let i = 0; i < swings && targets.length; i++) {
    const p = targets[Math.floor(Math.random() * targets.length)];
    const a = monsterAttack(boss, p.pd);
    p.hp = Math.max(0, p.hp - a.dmg);
    if (p.hp <= 0) p.downed = true;
  }
  await renderRaid(raid).catch(() => {});
}

async function renderRaid(raid) {
  if (!raid.channel) return;
  const embed = raidEmbed(raid);
  if (raid.message) await raid.message.edit({ embeds: [embed] }).catch(async () => { raid.message = await raid.channel.send({ embeds: [embed] }).catch(() => null); });
  else raid.message = await raid.channel.send({ embeds: [embed] }).catch(() => null);
}

function finishRaid(guildId, reason) {
  const raid = raids.get(guildId); if (!raid) return null;
  clearInterval(raid.timer); clearTimeout(raid.lobbyTimer);
  raids.delete(guildId);
  const zone = ZONES[raid.zoneId];
  const defeated = raid.hp <= 0;
  const results = [];
  for (const p of raid.parts.values()) {
    const char = getPlayer(p.discordId); if (!char) continue;
    const base = raid.boss.rewards || { xp: 0, gold: 0 };
    let xp = Math.round(base.xp * 4), gold = Math.round(base.gold * 4);
    if (!defeated) { xp = Math.round(xp * 0.4); gold = Math.round(gold * 0.4); }
    if (p.downed) { xp = Math.round(xp * 0.5); gold = Math.round(gold * 0.5); }
    char.gold = (char.gold || 0) + gold;
    let gear = [];
    if (defeated && !p.downed) { gear = rollLoot(raid.boss, zone).items; for (const it of gear) addItem(char, it); }
    const levels = grantXp(char, xp);
    const pd = derive(char);
    char.hp = pd.maxhp; char.mp = pd.maxmp; // resurrect after the raid
    savePlayer(p.discordId, char);
    results.push({ name: p.name, xp, gold, downed: p.downed, gear, levels });
  }
  if (raid.channel) raid.channel.send(raidSummary(raid, defeated, results)).catch(() => {});
  log.info(`Raid ${guildId} ${defeated ? 'DEFEATED' : 'ended'} (${reason}), ${results.length} raiders`);
  return { defeated, results };
}

function raidSummary(raid, defeated, results) {
  if (!results.length) return `🐉 The raid on **${raid.boss.name}** ended with no survivors to reward.`;
  const head = defeated ? `🏆 **${raid.boss.name} has been slain!** Loot for all who fought:` : `🐉 The raid on **${raid.boss.name}** timed out — it survived, so rewards are reduced.`;
  const lines = results.sort((a, b) => b.xp - a.xp).map((r) =>
    `${r.downed ? '💀' : '⚔️'} **${r.name}** — +${r.xp} XP · +${r.gold} 🪙${r.gear.length ? ` · ${r.gear.length} loot` : ''}${r.levels.length ? ` · 🎉 Lv ${r.levels[r.levels.length - 1]}` : ''}${r.downed ? ' *(diminished)*' : ''}`);
  return `${head}\n${lines.join('\n')}`;
}

function humanLobby(ms) {
  return ms >= 3600000 ? `${Math.round(ms / 3600000)} hour${ms >= 7200000 ? 's' : ''}` : `${Math.max(1, Math.round(ms / 60000))} minutes`;
}

async function announceRaid(raid, channel, zone, lobbyMs) {
  await renderRaid(raid).catch(() => {});
  channel.send(`🐉 **A RAID is coming!** A **${raid.boss.name}** (T${zone.tier} · ${zone.name}) will attack in **${humanLobby(lobbyMs)}**. Type **\`!raid join\`** to sign up — everyone who fights shares the loot!`).catch(() => {});
  log.info(`Announced raid in ${raid.guildId}: ${raid.boss.name} (T${zone.tier}), lobby ${humanLobby(lobbyMs)}`);
}

// Force-announce a raid right now (used by /raidnow). Random difficulty; short lobby by default.
export async function forceRaid(guildId, client, lobbyMs = 5 * 60 * 1000) {
  if (raids.has(guildId)) return { error: 'A raid is already active.' };
  const chanId = announceChannelId(guildId);
  if (!chanId) return { error: 'No raid/clips channel set. Run /setup tavern #channel first.' };
  const channel = await client.channels.fetch(chanId).catch(() => null);
  if (!channel || !channel.isTextBased()) return { error: 'Clips channel not found or not text.' };
  const zone = ZONE_LIST[Math.floor(Math.random() * ZONE_LIST.length)];
  const r = spawnRaid(guildId, zone, channel, lobbyMs);
  if (r.error) return r;
  nextSpawn.set(guildId, Date.now() + rand(SPAWN_MIN, SPAWN_MAX)); // push back the next auto one
  await announceRaid(r.raid, channel, zone, lobbyMs);
  return { raid: r.raid, zone };
}

// ── auto-announce scheduler (a raid every 1–3h, with a 1h sign-up lobby) ──────
export function startRaidScheduler(client) {
  const check = async () => {
    for (const [gid] of client.guilds.cache) {
      const chanId = announceChannelId(gid);
      if (!chanId || raids.has(gid)) continue;
      const due = nextSpawn.get(gid);
      if (due == null) { nextSpawn.set(gid, Date.now() + rand(SPAWN_MIN, SPAWN_MAX)); continue; }
      if (Date.now() < due) continue;
      nextSpawn.set(gid, Date.now() + rand(SPAWN_MIN, SPAWN_MAX));
      const channel = await client.channels.fetch(chanId).catch(() => null);
      if (!channel || !channel.isTextBased()) continue;
      const zone = ZONE_LIST[Math.floor(Math.random() * ZONE_LIST.length)]; // random difficulty
      const r = spawnRaid(gid, zone, channel, LOBBY_MS);
      if (!r.error) await announceRaid(r.raid, channel, zone, LOBBY_MS);
    }
  };
  setInterval(() => check().catch(() => {}), SCHED_MS);
  setTimeout(() => check().catch(() => {}), 15000);
  log.info('Raid scheduler started (raids announced every 1–3h, 1h sign-up).');
}
