import { EmbedBuilder } from 'discord.js';
import {
  RACE_LIST, CLASS_LIST, ZONE_LIST, RACES, CLASSES, ZONES, RARITIES, STAT_KEYS, ITEMS,
  skillsForClass,
} from './content.js';
import {
  derive, xpToNext, startingKit, gearScore, clamp, shopInventory, sellValue, makeGear,
  bossForZone, isZoneUnlocked, highestUnlockedZone, currentBossZone,
} from './engine.js';
import { getPlayer, savePlayer, deletePlayer, allPlayers, nextStaminaMs, MAX_STAMINA } from './store.js';
import {
  getFight, hasFight, endFight, startFight, takeTurn, resolveWin, resolveLoss,
  pickEncounter, addItem,
} from './fights.js';
import { getRaid, joinRaid, raidAction, startRaid, raidEmbed } from './raids.js';
import { gather, getWorker, workerXpToNext } from './gather.js';
import { PROFESSIONS, getProf, profXpToNext, merchantSale, merchantBuyPrice, merchantDiscountPct } from './professions.js';
import { craft, listRecipes, recipeName, inputsLine } from './recipes.js';
import { hasMats, countMat } from './invutil.js';
import { enchantList, enchantCap, nextCost, doEnchant, REAGENT_NAME } from './enchant.js';
import { boxPrice, getBoxes, openBox } from './lootbox.js';
import { ensureQuest, questProgress, questClaim } from './quests.js';
import { guideUrl } from '../features/guide.js';
import { profileUrl } from '../profile.js';
import { playUrl } from '../play.js';
import { getGuild } from '../guildStore.js';

const PREFIX = '!';
const GEAR_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const RARITY_EMOJI = { common: '⚪', uncommon: '🟢', rare: '🔵', epic: '🟣', legendary: '🟡' };
const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);

// ── item descriptions (stats + special effects) ───────────────────────────────
function statBonusStr(sb) {
  return Object.entries(sb || {})
    .map(([k, v]) => (k === 'crit' ? `CRIT+${v}%` : `${k.toUpperCase()}+${v}`))
    .join(' ');
}

// Human-readable effect for a consumable.
function effectDesc(it) {
  const m = it.magnitude || 0;
  const e = it.effect || '';
  if (e === 'heal_pct') return `heals ${m}% HP`;
  if (e === 'heal_full') return 'fully restores HP';
  if (e === 'mp_pct') return `restores ${m}% MP`;
  if (e.startsWith('cure:')) return `cures ${e.slice(5)}`;
  if (e === 'revive') return `revives a fallen ally at ${m}% HP`;
  if (e === 'flee_guaranteed') return 'guarantees escape from a fight';
  if (e.startsWith('damage:')) return `deals ${m} ${e.slice(7)} damage to the enemy`;
  if (e === 'stamina') return `restores ${m} ⚡ stamina`;
  return 'consumable';
}

// One-line stat/effect summary for an item (gear, consumable or material).
function itemStats(it) {
  if (GEAR_SLOTS.includes(it.slot)) {
    const p = [];
    if (it.power) p.push(`PWR ${it.power}`);
    if (it.defense) p.push(`DEF ${it.defense}`);
    if (it.resist) p.push(`RES ${it.resist}`);
    const sb = statBonusStr(it.stat_bonus);
    if (sb) p.push(sb);
    if (it.enchant) p.push(`✨+${it.enchant}`);
    return p.join(' · ') || 'no bonuses';
  }
  if (it.slot === 'consumable') return effectDesc(it);
  if (it.slot === 'material') return `crafting material${it.value ? ` · ~${it.value} 🪙 each` : ''}`;
  return '';
}

// Rich multi-line description (for `tt inspect`).
function itemDetail(it) {
  const rarity = it.rarity ? `${RARITY_EMOJI[it.rarity] || ''} ${cap(it.rarity)} ` : '';
  const kind = GEAR_SLOTS.includes(it.slot)
    ? `${rarity}${it.weapon_type ? cap(it.weapon_type) + ' ' : ''}${it.slot}${it.scales ? ` · scales with ${it.scales.toUpperCase()}` : ''}`
    : it.slot;
  const lines = [`**${it.name}**`, `_${kind}_`, itemStats(it)];
  if (GEAR_SLOTS.includes(it.slot)) lines.push(`Sells for ${sellValue(it)} 🪙`);
  else if (it.slot === 'material') lines.push(`Sells for ${it.value || 1} 🪙 each`);
  return lines.filter(Boolean).join('\n');
}

function bar(cur, max, width = 12) {
  const p = clamp(max > 0 ? cur / max : 0, 0, 1);
  const f = Math.round(p * width);
  return '`' + '█'.repeat(f) + '░'.repeat(width - f) + '`';
}

// Compact one-line summary of the hero's professions (only ones they've started).
function profsSummary(char) {
  const parts = [];
  for (const [key, meta] of Object.entries(PROFESSIONS)) {
    const p = getProf(char, key);
    if (key === 'worker' || p.level > 1 || p.xp > 0) parts.push(`${meta.emoji} ${p.level}`);
  }
  return parts.join(' · ') || '—';
}

// ── formatting ────────────────────────────────────────────────────────────────
function sheetEmbed(char) {
  const pd = derive(char);
  const race = RACES[char.race], cls = CLASSES[char.cls];
  const stam = char.stamina ?? MAX_STAMINA;
  const stats = STAT_KEYS.map((k) => `${k.toUpperCase()} ${pd.st[k]}`).join(' · ');
  const gear = GEAR_SLOTS
    .map((s) => char.equipped?.[s]
      ? `${RARITY_EMOJI[char.equipped[s].rarity] || ''} **${char.equipped[s].name}** — ${itemStats(char.equipped[s])}`
      : null)
    .filter(Boolean).join('\n') || 'Nothing equipped';
  return new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle(`${char.ascension ? `⭐${char.ascension} ` : ''}${char.name} — Lvl ${char.level} ${race.name} ${cls.name}`)
    .setDescription(cls.blurb)
    .addFields(
      { name: 'HP', value: `${char.hp ?? pd.maxhp}/${pd.maxhp}`, inline: true },
      { name: 'MP', value: `${char.mp ?? pd.maxmp}/${pd.maxmp}`, inline: true },
      { name: 'XP', value: `${char.xp || 0}/${xpToNext(char.level)}`, inline: true },
      { name: 'Gold', value: `${char.gold || 0} 🪙`, inline: true },
      { name: 'Stamina', value: `${stam}/${MAX_STAMINA} ⚡`, inline: true },
      { name: 'Professions', value: profsSummary(char), inline: true },
      { name: 'Power', value: `ATK ~${Math.round(pd.st[pd.scales] * 1.3 + pd.wpow * 1.4)} · DEF ${pd.def} · RES ${pd.res}`, inline: true },
      { name: 'Stats', value: stats },
      { name: 'Equipped', value: gear },
    )
    .setFooter({ text: 'Adventure with tt adventure · gear up with tt inv / tt equip' });
}

function fightEmbed(fight, extraLog = []) {
  const m = fight.monster;
  const log = [...fight.log, ...extraLog].slice(-7).join('\n') || 'The battle begins!';
  const rankTag = m.rank && m.rank !== 'trash' ? ` (${m.rank.toUpperCase()})` : '';
  return new EmbedBuilder()
    .setColor(m.rank === 'boss' ? 0xd64f4f : m.rank === 'elite' ? 0x9a4fd6 : 0x3f7fd6)
    .setTitle(`⚔️ ${m.name}${rankTag}`)
    .setDescription(
      `**${m.name}**  ${fight.mhp}/${fight.mmaxhp}\n${bar(fight.mhp, fight.mmaxhp)}\n\n` +
      `**You**  ❤️ ${fight.php}/${fight.pd.maxhp}  ·  💧 ${fight.pmp}/${fight.pd.maxmp}\n${bar(fight.php, fight.pd.maxhp)}\n\n` +
      `${log}`
    )
    .setFooter({ text: 'tt attack · tt skill <name> · tt use · tt flee' });
}

// ── command handlers ──────────────────────────────────────────────────────────
async function cmdHelp(msg) {
  const e = new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle('🍺 Tavern Tales — commands')
    .setDescription(
      '**Getting started**\n' +
      '`tt classes` `tt races` — see your options\n' +
      '`tt create <class> <race> [name]` — roll a hero\n' +
      '`tt char` — your character sheet · `tt skills` — your abilities\n' +
      '`tt new <class> <race> [name]` — start over with a fresh hero\n\n' +
      '**Adventuring**\n' +
      '`tt zones` — where you can go\n' +
      '`tt adventure [zone]` — find a fight (costs ⚡ stamina)\n' +
      '`tt boss` — challenge your zone’s boss to unlock the next zone\n' +
      'In combat: `tt attack` starts auto-battle · `tt skill <name>` · `tt use` · `tt flee`\n\n' +
      '**Raids** (a boss appears now and then — team up!)\n' +
      '`tt raid join` · `tt raid skill <name>` · `tt raid use` · `tt raid revive`\n\n' +
      '**Gathering & crafting**\n' +
      '`tt chop` `tt mine` `tt fish` `tt forage` `tt dig` `tt scavenge` — gather materials + Worker XP (3-min cooldown each)\n' +
      '`tt recipes` · `tt craft <#>` · `tt brew <#>` · `tt enchant <#>` — make & upgrade gear\n\n' +
      '**Professions & extras**\n' +
      '`tt quest` — your daily quest · `tt quest claim` — collect the reward\n' +
      '`tt lootbox` — buy & open mystery boxes\n' +
      '`tt ascend` — prestige at lvl 30+ for permanent bonuses\n' +
      'Trading levels your 💰 Merchant (better prices); every profession shows on `tt char`.\n\n' +
      '**Gear & town**\n' +
      '`tt inv` — bag & gold · `tt inspect <#>` — item stats & effects · `tt equip <#>` — wear gear\n' +
      '`tt shop` — buy gear/potions · `tt buy <#>` · `tt sell <#>`\n' +
      '`tt rest` — recover HP/MP · `tt leaderboard` — top heroes\n\n' +
      '**Playing from stream chat**\n' +
      '`tt play <your Discord @username>` then `tt confirm <code>` — link your chat account to your Discord hero so your progress follows you everywhere.'
    );
  const url = guideUrl();
  if (url) e.setFooter({ text: '📖 Full web guide — tt guide' }).setURL(url);
  return msg.reply({ embeds: [e] });
}

function cmdGuide(msg) {
  const url = guideUrl();
  if (url) return msg.reply(`📖 **Tavern Tales player guide:** ${url}\nEverything — how to play, every command, classes, professions & items.`);
  return msg.reply('📖 Full command list: `tt help`. (A web guide link isn’t configured on this bot.)');
}

function cmdProfile(msg) {
  if (!getPlayer(msg.author.id)) return msg.reply('No hero yet — `tt create` first.');
  const url = profileUrl(msg.author.id);
  if (url) return msg.reply(`🪪 **Your hero profile:** ${url}`);
  return msg.reply('Your character sheet: `tt char`. (A web profile link isn’t configured on this bot.)');
}

function cmdWeb(msg) {
  const url = playUrl(msg.author.id);
  if (!url) return msg.reply('The browser version isn’t configured on this bot. Play here with `tt` commands, or see `tt help`.');
  // The link controls the hero (like a password), so never print it in public chat —
  // it can only be delivered by DM, which stream chat can't do.
  if (msg._chat) return msg.reply('🎮 The browser link is private (it controls your hero). Type `tt web` in our Discord and I’ll DM it to you.');
  // The link is a private key to this hero — DM it so it isn't exposed in public chat.
  const send = async () => {
    try {
      const dm = await msg.author.createDM();
      await dm.send(`🎮 **Play Tavern Tales in your browser** — this link is private to your hero (like a password, don’t share it):\n${url}`);
      return msg.reply('🎮 I’ve DM’d you your private browser-play link! (It’s tied to your hero — keep it secret.)');
    } catch {
      return msg.reply(`🎮 **Play in your browser:** ${url}\n⚠️ This link controls your hero — treat it like a password and don’t share it.`);
    }
  };
  return send();
}

function cmdClasses(msg) {
  const lines = CLASS_LIST.map((c) => `**${c.name}** — ${c.blurb} *(${c.primary.toUpperCase()}, ${c.armor_weight})*`);
  return msg.reply('🛡️ **Classes**\n' + lines.join('\n'));
}

function cmdRaces(msg) {
  const lines = RACE_LIST.map((r) => `**${r.name}** — ${r.blurb}`);
  return msg.reply('🧬 **Races**\n' + lines.join('\n'));
}

function cmdCreate(msg, args) {
  if (getPlayer(msg.author.id)) return msg.reply('You already have a hero — see `tt char`. Want to start over? `tt new <class> <race> [name]`.');
  const clsQ = (args[0] || '').toLowerCase();
  const raceQ = (args[1] || '').toLowerCase();
  const cls = CLASS_LIST.find((c) => c.id === clsQ || c.name.toLowerCase() === clsQ);
  const race = RACE_LIST.find((r) => r.id === raceQ || r.name.toLowerCase() === raceQ);
  if (!cls || !race) {
    return msg.reply(
      'Usage: `tt signup <class> <race> [name]` (or `tt create`)\n' +
      `Classes: ${CLASS_LIST.map((c) => c.id).join(', ')}\n` +
      `Races: ${RACE_LIST.map((r) => r.id).join(', ')}`
    );
  }
  const name = args.slice(2).join(' ').trim().slice(0, 32) || msg.author.username;
  const char = { name, cls: cls.id, race: race.id, level: 1, xp: 0, gold: 25, cleared: {} };
  startingKit(char);
  const pd = derive(char);
  char.hp = pd.maxhp; char.mp = pd.maxmp;
  char.stamina = MAX_STAMINA; char.stamTs = Date.now();
  savePlayer(msg.author.id, char);
  return msg.reply({ content: `🎉 **${name}** the ${race.name} ${cls.name} steps into the tavern!`, embeds: [sheetEmbed(char)] });
}

function cmdChar(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — make one with `tt create <class> <race> [name]`.');
  return msg.reply({ embeds: [sheetEmbed(char)] });
}

function cmdSkills(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const skills = skillsForClass(char.cls, char.level);
  const locked = skillsForClass(char.cls, 99).filter((s) => s.unlock_level > char.level);
  const lines = skills.map((s, i) => `\`${i + 1}\` **${s.name}** — ${s.mp} MP · ${s.type}${s.power ? ` · pow ${s.power}` : ''}`);
  let out = `✨ **${CLASSES[char.cls].name} skills (Lvl ${char.level})** — cast with \`tt skill <#>\`\n` + (lines.join('\n') || '_none yet_');
  if (locked.length) out += `\n\n🔒 Next: ${locked.slice(0, 3).map((s) => `${s.name} (Lv${s.unlock_level})`).join(', ')}`;
  return msg.reply(out);
}

function cmdZones(msg) {
  const char = getPlayer(msg.author.id);
  const lvl = char?.level ?? 1;
  const lines = ZONE_LIST.map((z) => {
    const ok = lvl >= z.level_required;
    return `${ok ? '✅' : '🔒'} **${z.name}** — Lv ${z.level_required}+ · ${z.stamina_cost}⚡ · T${z.tier}\n    _${z.description}_`;
  });
  return msg.reply('🗺️ **Zones**\n' + lines.join('\n') + (char ? '\n\nTravel with `tt adventure <zone>` (or just `tt adventure` for the best one you can enter).' : '\n\nMake a hero first with `tt create`.'));
}

function bestZoneFor(level) {
  const eligible = ZONE_LIST.filter((z) => level >= z.level_required);
  return eligible[eligible.length - 1] || ZONE_LIST[0];
}

async function cmdAdventure(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  if (hasFight(msg.author.id)) return msg.reply('You’re already in a fight! `tt attack`, `tt skill`, `tt use`, or `tt flee`.');

  let zone;
  if (args[0]) {
    const q = args.join(' ').toLowerCase();
    zone = ZONE_LIST.find((z) => z.id === q || z.name.toLowerCase() === q || z.name.toLowerCase().includes(q));
    if (!zone) return msg.reply('No such zone. See `tt zones`.');
    if (!isZoneUnlocked(char, zone)) return msg.reply(`🔒 **${zone.name}** is locked — beat the previous zone's boss (\`tt boss\`) to unlock it.`);
  } else {
    zone = highestUnlockedZone(char);
  }

  const stam = char.stamina ?? MAX_STAMINA;
  if (stam < zone.stamina_cost) {
    const ms = nextStaminaMs(char) || 0;
    return msg.reply(`😴 Not enough stamina (need ${zone.stamina_cost}⚡, have ${stam}). Next point in ~${Math.ceil(ms / 60000)} min.`);
  }
  char.stamina = stam - zone.stamina_cost;
  if (char.stamTs == null) char.stamTs = Date.now();

  const monsterId = pickEncounter(zone);
  if (!monsterId) { savePlayer(msg.author.id, char); return msg.reply('The zone is eerily quiet… (no encounters found).'); }
  savePlayer(msg.author.id, char);

  const fight = startFight(msg.author.id, char, monsterId, zone.id);
  fight.log.push(`You venture into **${zone.name}** and a **${fight.monster.name}** appears!`);

  // Twitch/relay: auto-fight to the finish and reply once (no editing, no spam).
  if (msg._auto) return autoFinish(msg, char);
  // Stream chat can't edit messages, so it stays manual (type "tt attack" each turn).
  if (msg._chat) return msg.reply({ embeds: [fightEmbed(fight)] });

  // Discord: show the encounter and WAIT. The live auto-battle starts on their first
  // action (registered but not armed yet).
  const sent = await msg.reply({
    content: '⚔️ **Type `tt attack` to begin!** (or `tt skill <name>` · `tt flee`)',
    embeds: [fightEmbed(fight)],
  });
  autos.set(msg.author.id, { message: sent, char, timer: null });
}

async function cmdBoss(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  if (hasFight(msg.author.id)) return msg.reply('Finish your current fight first!');

  let zone;
  if (args[0]) {
    const q = args.join(' ').toLowerCase();
    zone = ZONE_LIST.find((z) => z.id === q || z.name.toLowerCase() === q || z.name.toLowerCase().includes(q));
    if (!zone) return msg.reply('No such zone. See `tt zones`.');
  } else {
    zone = currentBossZone(char);
  }
  if (!isZoneUnlocked(char, zone)) return msg.reply(`🔒 **${zone.name}** is locked — clear the previous zone's boss first.`);

  const fight = startFight(msg.author.id, char, bossForZone(zone), zone.id);
  fight.bossZone = zone.id;
  fight.log.push(`⚔️ You challenge **${fight.monster.name}**, the boss of **${zone.name}**!`);
  if (msg._auto) return autoFinish(msg, char);
  if (msg._chat) return msg.reply({ embeds: [fightEmbed(fight)] });
  const sent = await msg.reply({ content: '👑 **BOSS FIGHT!** Type `tt attack` to begin!', embeds: [fightEmbed(fight)] });
  autos.set(msg.author.id, { message: sent, char, timer: null });
}

// Resolve the channel a manual raid should announce/refresh in.
async function raidChannel(msg, gid) {
  if (!msg._chat && msg.channel) return msg.channel;
  try {
    const cfg = getGuild(gid);
    const chanId = cfg.tavernChannelId || cfg.clipChannelId;
    if (chanId && msg.client) { const ch = await msg.client.channels.fetch(chanId).catch(() => null); if (ch && ch.isTextBased()) return ch; }
  } catch { /* ignore */ }
  return null;
}

async function cmdRaid(msg, args) {
  const gid = msg.guildId;
  if (!gid) return msg.reply('Raids happen in a server.');
  const char = getPlayer(msg.author.id);
  const sub = (args[0] || '').toLowerCase();
  const raid = getRaid(gid);

  if (sub === 'join') {
    const r = joinRaid(gid, msg.author.id);
    return msg.reply(r.error || `⚔️ **${char?.name || 'You'}** joined the raid! Your hero auto-attacks — interject with \`tt raid skill <name>\`, \`tt raid use\`, or \`tt raid revive\`.`);
  }
  if (sub === 'skill' || sub === 'cast') {
    if (!char) return msg.reply('Make a hero first with `tt create`.');
    if (!raid) return msg.reply('No active raid.');
    const q = args.slice(1).join(' ').toLowerCase().trim();
    const list = skillsForClass(char.cls, char.level);
    const skill = list.find((s) => s.id === q || s.name.toLowerCase() === q) || list.find((s) => s.name.toLowerCase().includes(q));
    if (!skill) return msg.reply(`No skill "${args.slice(1).join(' ')}". Yours: ${list.map((s) => s.name).join(', ') || 'none'}.`);
    raidAction(gid, msg.author.id, { kind: 'skill', skill });
    return msg.reply(`✨ You’ll cast **${skill.name}** on the raid boss next tick.`);
  }
  if (sub === 'use' || sub === 'potion') {
    const r = raidAction(gid, msg.author.id, { kind: 'use' });
    return msg.reply(r.error || '🧪 You’ll quaff a potion next tick.');
  }
  if (sub === 'revive') {
    const r = raidAction(gid, msg.author.id, { kind: 'revive' });
    return msg.reply(r.error || '💚 You’re back on your feet — rejoining the fight!');
  }
  if (sub === 'start') {
    if (!char) return msg.reply('Make a hero first with `tt create`.');
    if (raid) return msg.reply({ embeds: [raidEmbed(raid)] });
    const r = startRaid(gid, char, msg.author.id, await raidChannel(msg, gid));
    if (r.error) return msg.reply(r.error);
    return msg.reply(`🐉 **You summoned a raid** on **${r.raid.boss.name}** (T${r.zone.tier} · ${r.zone.name})! \`tt raid join\` to fight — it auto-battles for up to 1 hour.`);
  }

  if (!raid) return msg.reply('No active raid right now. They pop up every 6–12 hours — team up when one does!');
  return msg.reply({ embeds: [raidEmbed(raid)] });
}

// ── shared win/lose rendering ────────────────────────────────────────────────
function victoryEmbed(fight, reward, char) {
  let desc = fight.log.slice(-6).join('\n') + `\n\n**+${reward.xp} XP · +${reward.gold} 🪙**`;
  if (reward.items.length) desc += '\n\n**Loot:**\n' + reward.items.map((i) => {
    const stats = itemStats(i);
    return `${RARITY_EMOJI[i.rarity] || '•'} ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}${stats ? ` — ${stats}` : ''}`;
  }).join('\n');
  if (reward.levels.length) desc += `\n\n🎉 **LEVEL UP!** You’re now level **${char.level}** (fully healed).`;
  if (reward.clearedBoss) desc += reward.unlocked
    ? `\n\n🗺️ **BOSS DEFEATED!** New zone unlocked: **${reward.unlocked}**!`
    : `\n\n👑 **BOSS DEFEATED!** You've conquered the final zone!`;
  const next = ['`tt adventure`'];
  if (reward.levels.length) next.push('`tt skills`');
  if (reward.items.length) next.push('`tt inv`');
  next.push('`tt char`', '`tt shop`', '`tt rest`');
  desc += `\n\n▶️ **Next:** ${next.join(' · ')}`;
  return new EmbedBuilder().setColor(0x3fa34d).setTitle(`🏆 ${fight.monster.name} defeated!`).setDescription(desc);
}
function defeatEmbed(fight, lost) {
  return new EmbedBuilder().setColor(0xd64f4f).setTitle('💀 You have fallen…')
    .setDescription(fight.log.slice(-6).join('\n') + `\n\nYou wake at the tavern, down **${lost}** 🪙 but alive. Rest with \`tt rest\`.`);
}

// ── Discord auto-battle loop (edits one message in place) ─────────────────────
const AUTO_MS = 1500;
const autos = new Map(); // uid -> { message, char, timer }

function disarm(uid) { const a = autos.get(uid); if (a && a.timer) { clearTimeout(a.timer); a.timer = null; } }
function stopAuto(uid) { disarm(uid); autos.delete(uid); }
function arm(uid) { const a = autos.get(uid); if (!a) return; if (a.timer) clearTimeout(a.timer); a.timer = setTimeout(() => autoTick(uid), AUTO_MS); }

async function autoTick(uid) {
  const a = autos.get(uid); if (!a) return;
  const fight = getFight(uid); if (!fight) { stopAuto(uid); return; }
  const res = takeTurn(fight, 'attack');
  fight.log.push(...res.log);
  await settleAuto(uid, fight, res, true); // auto keeps swinging
}

// Apply an outcome to the live message; re-arm the auto-loop only if `resume`.
async function settleAuto(uid, fight, res, resume) {
  const a = autos.get(uid); if (!a) return;
  const char = a.char;
  if (res.win) { stopAuto(uid); const reward = resolveWin(fight, char); questProgress(char, 'win', 1); endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '', embeds: [victoryEmbed(fight, reward, char)] }).catch(() => {}); return; }
  if (res.lose) { stopAuto(uid); const { lost } = resolveLoss(char); endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '', embeds: [defeatEmbed(fight, lost)] }).catch(() => {}); return; }
  if (res.fled) { stopAuto(uid); char.hp = fight.php; char.mp = fight.pmp; endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '🏃 You fled the fight.', embeds: [] }).catch(() => {}); return; }
  const foot = resume ? '' : '\n⏸️ Paused — type `tt attack` to resume auto-attacking.';
  await a.message.edit({ content: foot, embeds: [fightEmbed(fight)] }).catch(() => {});
  if (resume) arm(uid);
}

// Route a player action: Discord auto-fight edits the live message; chat replies.
// resume=true (only for !attack) keeps the auto-loop going; other actions do one round.
async function act(msg, char, res, resume) {
  const uid = msg.author.id;
  if (res.error) return msg.reply(`⚠️ ${res.error}`);
  if (autos.has(uid) && !msg._chat) {
    disarm(uid);
    const fight = getFight(uid);
    if (fight) fight.log.push(...res.log);
    return settleAuto(uid, fight, res, resume);
  }
  return afterTurn(msg, char, res);
}

// Manual (chat / non-auto) turn handling — one reply per action.
async function afterTurn(msg, char, res) {
  const fight = getFight(msg.author.id);
  if (res.error) return msg.reply(`⚠️ ${res.error}`);
  if (fight) fight.log.push(...res.log);
  if (res.fled) { char.hp = fight.php; char.mp = fight.pmp; endFight(msg.author.id); savePlayer(msg.author.id, char); return msg.reply('🏃 ' + res.log.join('\n')); }
  if (res.win) { const reward = resolveWin(fight, char); questProgress(char, 'win', 1); endFight(msg.author.id); savePlayer(msg.author.id, char); return msg.reply({ embeds: [victoryEmbed(fight, reward, char)] }); }
  if (res.lose) { const { lost } = resolveLoss(char); endFight(msg.author.id); savePlayer(msg.author.id, char); return msg.reply({ embeds: [defeatEmbed(fight, lost)] }); }
  // Twitch/relay auto mode: no per-turn spam and no message editing available, so
  // swing the fight to its conclusion and send a single summary line.
  if (msg._auto) return autoFinish(msg, char);
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

// Resolve the current fight to completion (chat auto-battle) → one concise reply.
async function autoFinish(msg, char) {
  const uid = msg.author.id;
  const fight = getFight(uid);
  if (!fight) return msg.reply('You’re not in a fight. `tt adventure` to find one.');
  let guard = 0;
  while (guard++ < 500) {
    const res = takeTurn(fight, 'attack');
    fight.log.push(...res.log);
    if (res.win) {
      const reward = resolveWin(fight, char); questProgress(char, 'win', 1); endFight(uid); savePlayer(uid, char);
      return msg.reply(autoWinLine(fight, reward, char));
    }
    if (res.lose) {
      const { lost } = resolveLoss(char); endFight(uid); savePlayer(uid, char);
      return msg.reply(`💀 You fell to the **${fight.monster.name}** — down ${lost} 🪙 but alive. \`tt rest\`, then \`tt adventure\`.`);
    }
    if (res.fled) { char.hp = fight.php; char.mp = fight.pmp; endFight(uid); savePlayer(uid, char); return msg.reply('🏃 You slipped away from the fight.'); }
  }
  endFight(uid); savePlayer(uid, char);
  return msg.reply('The battle dragged on and fizzled out.');
}
function autoWinLine(fight, reward, char) {
  const pd = derive(char);
  let s = `🏆 **${fight.monster.name}** defeated! +${reward.xp} XP · +${reward.gold} 🪙`;
  if (reward.items && reward.items.length) s += ` · Loot: ${reward.items.map((i) => `${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}`).join(', ')}`;
  if (reward.levels && reward.levels.length) s += ` · 🎉 now Lv ${char.level}!`;
  if (reward.clearedBoss) s += reward.unlocked ? ` · 🗺️ unlocked **${reward.unlocked}**!` : ' · 👑 final zone cleared!';
  s += ` · ❤️ ${char.hp}/${pd.maxhp}`;
  return s;
}

function cmdAttack(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `tt adventure` to find one.');
  return act(msg, char, takeTurn(fight, 'attack'), true); // attack (re)starts the auto-loop
}

function cmdSkill(msg, args) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `tt adventure` to find one.');
  const q = args.join(' ').toLowerCase().trim();
  if (!q) return msg.reply('Which skill? `skill <name>` or `skill 1` — see `tt skills`.');
  const list = skillsForClass(char.cls, char.level);
  const skill = /^\d+$/.test(q)
    ? list[parseInt(q, 10) - 1]
    : (list.find((s) => s.id === q || s.name.toLowerCase() === q) || list.find((s) => s.name.toLowerCase().includes(q) || s.id.includes(q)));
  if (!skill) return msg.reply(`No skill "${args.join(' ')}". Yours: ${list.map((s, i) => `${i + 1}=${s.name}`).join(', ') || 'none'}.`);
  return act(msg, char, takeTurn(fight, 'skill', skill), false);
}

function cmdUse(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You can only quaff potions in a fight right now. `tt rest` to heal in town.');
  const pot = (char.inventory || []).find((i) => i.effect === 'heal_pct' && (i.qty || 0) > 0);
  if (!pot) return msg.reply('No potions in your bag.');
  const heal = Math.round(fight.pd.maxhp * (pot.magnitude || 30) / 100);
  pot.qty -= 1;
  if (pot.qty <= 0) char.inventory = char.inventory.filter((i) => i !== pot);
  return act(msg, char, takeTurn(fight, 'use', heal));
}

function cmdFlee(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight.');
  return act(msg, char, takeTurn(fight, 'flee'));
}

function cmdStatus(msg) {
  const fight = getFight(msg.author.id);
  if (!fight) return msg.reply('You’re not in a fight. `tt adventure` to find one.');
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

function cmdInv(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const inv = char.inventory || [];
  const gear = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
  const other = inv.filter((i) => !GEAR_SLOTS.includes(i.slot));
  const junkValue = other.filter((i) => i.slot === 'material').reduce((s, m) => s + (m.value || 1) * (m.qty || 1), 0);
  let out = `🎒 **${char.name}'s bag** — ${char.gold || 0} 🪙\n`;
  if (gear.length) out += '\n**Gear** (equip with `tt equip <#>`, sell with `tt sell <#>`)\n' + gear.map((i, n) => `\`${n + 1}\` ${RARITY_EMOJI[i.rarity] || '•'} ${i.name} — ${i.slot} · ${itemStats(i)}`).join('\n');
  if (other.length) out += '\n\n**Items**\n' + other.map((i) => {
    const tail = i.slot === 'material'
      ? ` (${(i.value || 1) * (i.qty || 1)} 🪙)`
      : i.slot === 'consumable' ? ` — ${effectDesc(i)}` : '';
    return `• ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}${tail}`;
  }).join('\n');
  if (junkValue > 0) out += `\n\n_Sell all materials with \`tt sell junk\` (+${junkValue} 🪙)_`;
  out += '\n_Inspect anything: `tt inspect <#>` (gear number from above)._';
  if (!gear.length && !other.length) out += '\n_Empty. Go adventuring!_';
  return msg.reply(out);
}

function cmdInspect(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const inv = char.inventory || [];
  const gear = inv.filter((i) => GEAR_SLOTS.includes(i.slot));

  let item;
  const idx = parseInt(args[0], 10) - 1;
  if (Number.isInteger(idx) && idx >= 0 && gear[idx]) {
    item = gear[idx];
  } else {
    const q = args.join(' ').toLowerCase().trim();
    if (!q) return msg.reply('Inspect what? `tt inspect <#>` (gear number from `tt inv`) or `tt inspect <name>`.');
    item = inv.find((i) => i.name.toLowerCase() === q)
        || Object.values(char.equipped || {}).find((i) => i && i.name.toLowerCase() === q)
        || inv.find((i) => i.name.toLowerCase().includes(q))
        || Object.values(char.equipped || {}).find((i) => i && i.name.toLowerCase().includes(q));
    if (!item) {
      const base = Object.values(ITEMS).find((b) => b.name.toLowerCase() === q)
                || Object.values(ITEMS).find((b) => b.name.toLowerCase().includes(q));
      if (base) item = base;
    }
  }
  if (!item) return msg.reply("Couldn't find that item. Use a gear number from `tt inv`, or the item's name.");

  let out = '🔎 ' + itemDetail(item);
  if (GEAR_SLOTS.includes(item.slot)) {
    const eq = char.equipped?.[item.slot];
    if (eq && eq !== item) {
      const d = gearScore(item) - gearScore(eq);
      out += `\n\nvs equipped **${eq.name}** (${itemStats(eq)}): ${d > 0 ? `▲ +${d} better` : d < 0 ? `▼ ${d} worse` : '= same'}`;
    }
  }
  return msg.reply(out);
}

function cmdShop(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const shop = shopInventory(char);
  const eq = char.equipped || {};
  const lines = shop.map((s, n) => {
    const base = ITEMS[s.id];
    const price = merchantBuyPrice(char, s.price);
    if (!GEAR_SLOTS.includes(base.slot)) {
      const heal = base.effect === 'heal_pct' ? ` · heals ${base.magnitude}% HP` : '';
      return `\`${n + 1}\` ${s.name} — **${price}** 🪙${heal}`;
    }
    const parts = [];
    if (base.power) parts.push(`PWR ${base.power}`);
    if (base.defense) parts.push(`DEF ${base.defense}`);
    if (base.resist) parts.push(`RES ${base.resist}`);
    for (const [k, v] of Object.entries(base.stat_bonus || {})) parts.push(`${k.toUpperCase()}+${v}`);
    const d = gearScore(base) - (eq[base.slot] ? gearScore(eq[base.slot]) : 0);
    const cmp = d > 0 ? `▲ +${d}` : d < 0 ? `▼ ${d}` : '= same';
    return `\`${n + 1}\` ${s.name} — **${price}** 🪙 · ${parts.join(' ')} · vs equipped ${cmp}`;
  });
  const disc = merchantDiscountPct(char);
  const footer = disc > 0 ? `\n_💰 Merchant Lv ${getProf(char, 'merchant').level}: −${disc}% prices, +sell value._` : '';
  return msg.reply(`🏪 **Shop** (you have ${char.gold || 0} 🪙)\n` + lines.join('\n') +
    '\n\nBuy with `tt buy <#>` · sell gear with `tt sell <#>` (from `tt inv`).' + footer);
}

function cmdBuy(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  if (!args.length) return msg.reply('Buy what? `tt buy <#>` (from `tt shop`), with an optional quantity: `tt buy 2 5`.');
  const shop = shopInventory(char);

  let pick, qty = 1;
  if (/^\d+$/.test(args[0])) {                       // by number
    pick = shop[parseInt(args[0], 10) - 1];
    if (args[1] && /^\d+$/.test(args[1])) qty = parseInt(args[1], 10);
  } else {                                           // by name (trailing number = qty)
    const nameArgs = args.slice();
    if (nameArgs.length > 1 && /^\d+$/.test(nameArgs[nameArgs.length - 1])) qty = parseInt(nameArgs.pop(), 10);
    const q = nameArgs.join(' ').toLowerCase();
    pick = shop.find((s) => s.name.toLowerCase() === q || s.id === q) || shop.find((s) => s.name.toLowerCase().includes(q));
  }
  if (!pick) return msg.reply(`Not in the shop. See \`tt shop\` (buy by number, e.g. \`tt buy 1\`).`);

  const base = ITEMS[pick.id];
  const isGear = GEAR_SLOTS.includes(base.slot);
  if (isGear) qty = 1; // gear auto-equips — no point buying stacks
  qty = Math.max(1, Math.min(qty, 99));
  const unit = merchantBuyPrice(char, pick.price);   // Merchant discount
  const total = unit * qty;
  if ((char.gold || 0) < total) return msg.reply(`Not enough gold — ${qty}× ${pick.name} costs ${total} 🪙, you have ${char.gold || 0}.`);
  char.gold -= total;

  if (isGear) {
    const bought = makeGear(base.id, RARITIES[0]);
    char.equipped = char.equipped || {};
    const old = char.equipped[base.slot];
    if (old && gearScore(old) > gearScore(bought)) {
      addItem(char, bought); // keep the better equipped item; stash the purchase
      savePlayer(msg.author.id, char);
      return msg.reply(`🛒 Bought **${bought.name}** for ${unit} 🪙, but your equipped **${old.name}** is better — kept it on. New one's in your bag. (${char.gold} left)`);
    }
    char.equipped[base.slot] = bought;
    let tail = ' Auto-equipped.';
    if (old) { const sold = merchantSale(char, sellValue(old)).gold; char.gold += sold; tail = ` Auto-equipped and sold your old **${old.name}** for ${sold} 🪙.`; }
    savePlayer(msg.author.id, char);
    return msg.reply(`🛒 Bought **${bought.name}** for ${unit} 🪙.${tail} (${char.gold} left)`);
  }

  for (let i = 0; i < qty; i++) {
    addItem(char, { base: base.id, slot: base.slot, name: base.name, qty: 1, stackable: !!base.stackable, effect: base.effect, magnitude: base.magnitude, value: base.value });
  }
  savePlayer(msg.author.id, char);
  return msg.reply(`🛒 Bought **${qty}× ${pick.name}** for ${total} 🪙. (${char.gold} left)`);
}

function cmdSell(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const inv = char.inventory || [];
  const arg = args.join(' ').toLowerCase().trim();

  // Sell all gear at once.
  if (['allgear', 'gear', 'equipment', 'equip', 'weapons'].includes(arg)) {
    const g = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
    if (!g.length) return msg.reply('No gear in your bag to sell (equipped items are safe).');
    const base = g.reduce((s, it) => s + sellValue(it), 0);
    const { gold, leveled } = merchantSale(char, base);
    char.inventory = inv.filter((i) => !GEAR_SLOTS.includes(i.slot));
    char.gold = (char.gold || 0) + gold;
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold ${g.length} piece(s) of gear for **${gold}** 🪙.${leveled ? ' 💰 **Merchant level up!**' : ''} (${char.gold} total)`);
  }

  // Sell all materials/junk at once.
  if (['junk', 'all', 'mats', 'materials', 'trash'].includes(arg)) {
    const junk = inv.filter((i) => i.slot === 'material');
    if (!junk.length) return msg.reply('No materials to sell.');
    const base = junk.reduce((s, m) => s + (m.value || 1) * (m.qty || 1), 0);
    const count = junk.reduce((s, m) => s + (m.qty || 1), 0);
    const { gold, leveled } = merchantSale(char, base);
    char.inventory = inv.filter((i) => i.slot !== 'material');
    char.gold = (char.gold || 0) + gold;
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold ${count} material(s) for **${gold}** 🪙.${leveled ? ' 💰 **Merchant level up!**' : ''} (${char.gold} total)`);
  }

  // Sell a gear item by its !inv number.
  const gear = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
  const idx = parseInt(args[0], 10) - 1;
  if (Number.isInteger(idx) && idx >= 0 && gear[idx]) {
    const item = gear[idx];
    const { gold, leveled } = merchantSale(char, sellValue(item));
    char.gold = (char.gold || 0) + gold;
    char.inventory = inv.filter((i) => i !== item);
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold **${item.name}** for ${gold} 🪙.${leveled ? ' 💰 **Merchant level up!**' : ''} (${char.gold} total)`);
  }

  // Sell by name (materials, spare potions, or gear) — whole stack.
  if (arg) {
    const item = inv.find((i) => i.name.toLowerCase() === arg) || inv.find((i) => i.name.toLowerCase().includes(arg));
    if (item) {
      const base = item.slot === 'material' ? (item.value || 1) * (item.qty || 1) : sellValue(item) * (item.qty || 1);
      const { gold, leveled } = merchantSale(char, base);
      char.gold = (char.gold || 0) + gold;
      char.inventory = inv.filter((i) => i !== item);
      savePlayer(msg.author.id, char);
      return msg.reply(`💰 Sold **${item.name}**${item.qty > 1 ? ` x${item.qty}` : ''} for ${gold} 🪙.${leveled ? ' 💰 **Merchant level up!**' : ''} (${char.gold} total)`);
    }
  }

  return msg.reply('Sell what? `tt sell <#>` (gear from `tt inv`) · `tt sell junk` (all materials) · `tt sell <name>`.');
}

function cmdEquip(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const gear = (char.inventory || []).filter((i) => GEAR_SLOTS.includes(i.slot));
  const idx = parseInt(args[0], 10) - 1;
  const item = Number.isInteger(idx) ? gear[idx] : gear.find((i) => i.name.toLowerCase().includes(args.join(' ').toLowerCase()));
  if (!item) return msg.reply('Which item? `tt equip <#>` — numbers from `tt inv`.');
  char.equipped = char.equipped || {};
  const old = char.equipped[item.slot];
  char.equipped[item.slot] = item;
  char.inventory = char.inventory.filter((i) => i !== item);
  if (old) addItem(char, old);
  savePlayer(msg.author.id, char);
  const delta = old ? ` (was ${old.name})` : '';
  return msg.reply(`✅ Equipped **${item.name}** in ${item.slot}${delta}.`);
}

function cmdRest(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  if (hasFight(msg.author.id)) return msg.reply('You can’t rest mid-fight!');
  const pd = derive(char);
  char.hp = pd.maxhp; char.mp = pd.maxmp;
  char.stamina = MAX_STAMINA; char.stamTs = Date.now();
  savePlayer(msg.author.id, char);
  return msg.reply(`🛌 You rest at the tavern. HP, MP & stamina fully restored (❤️ ${pd.maxhp} · 💧 ${pd.maxmp} · ⚡ ${MAX_STAMINA}).`);
}

function cmdDelete(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero to delete.');
  if ((args[0] || '').toLowerCase() !== 'confirm') {
    return msg.reply(`⚠️ This permanently deletes **${char.name}** (Lvl ${char.level}). Type \`tt deletechar confirm\` to proceed.`);
  }
  endFight(msg.author.id);
  deletePlayer(msg.author.id);
  return msg.reply('🪦 Character deleted. `tt create` to begin anew.');
}

// Start over: wipe the existing hero and roll a fresh one in a single command.
function cmdNew(msg, args) {
  const existing = getPlayer(msg.author.id);
  if (!existing) return cmdCreate(msg, args);          // no hero yet → just create
  if ((args[0] || '').toLowerCase() !== 'confirm') {
    return msg.reply(
      `⚠️ You already have **${existing.name}** (Lvl ${existing.level}). Starting over deletes them for good.\n` +
      `To reroll, resend with **confirm**: \`tt new confirm <class> <race> [name]\`\n` +
      `e.g. \`tt new confirm ${existing.cls} ${existing.race} ${existing.name}\``
    );
  }
  endFight(msg.author.id);
  deletePlayer(msg.author.id);
  return cmdCreate(msg, args.slice(1));                // drop the "confirm" token
}

// ── gathering (Worker profession) ────────────────────────────────────────────
const GATHER_EMOJI = { chop: '🪓', mine: '⛏️', fish: '🎣', forage: '🌿', dig: '🪏', scavenge: '♻️' };

function cmdGather(msg, args, command) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const res = gather(char, command);
  if (res.status === 'cooldown') {
    const s = Math.ceil(res.remaining / 1000);
    return msg.reply(`⏳ **${command}** is resting — ${Math.floor(s / 60)}m ${s % 60}s left.`);
  }
  if (res.status === 'nothing') return msg.reply(`There's nothing to ${command} around here.`);
  questProgress(char, 'gather', res.qty);
  savePlayer(msg.author.id, char);
  const e = GATHER_EMOJI[command] || '⛏️';
  let out = `${e} You ${command} and gather **${res.qty}× ${res.material}**! +${res.xp} Worker XP (Worker Lv ${res.workerLevel}).`;
  if (res.levelsGained) out += ' 🎉 **Worker level up!**';
  out += ' Ready again in 3m — sell mats with `tt sell junk`.';
  return msg.reply(out);
}

// ── crafting (Crafter) & brewing (Alchemist) ─────────────────────────────────
function showRecipes(msg, char, prof, verb) {
  const recipes = listRecipes(prof);
  const lvl = getProf(char, prof).level;
  const lines = recipes.map((r, i) => {
    const locked = lvl < r.level;
    const ready = !locked && hasMats(char, r.inputs);
    const tag = locked ? ` 🔒Lv${r.level}` : ready ? ' ✅' : '';
    return `\`${i + 1}\` ${recipeName(r)} — ${inputsLine(char, r)}${tag}`;
  });
  const meta = PROFESSIONS[prof];
  return msg.reply(
    `${meta.emoji} **${meta.name} recipes** (you're Lv ${lvl}) — make one with \`tt ${verb} <#>\`\n` +
    lines.join('\n') +
    '\n\n✅ = ready · 🔒 = higher level needed. Gather materials with `tt chop` `tt mine` `tt fish` `tt forage` `tt dig` `tt scavenge`.'
  );
}

// Shared handler for `tt craft` (Crafter) and `tt brew` (Alchemist).
function cmdMake(msg, args, prof, verb) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const recipes = listRecipes(prof);
  if (!args.length || !/^\d+$/.test(args[0])) return showRecipes(msg, char, prof, verb);

  const recipe = recipes[parseInt(args[0], 10) - 1];
  if (!recipe) return msg.reply(`No recipe #${args[0]}. See \`tt ${verb === 'brew' ? 'recipes brew' : 'recipes'}\`.`);

  const res = craft(char, recipe);
  if (!res.ok) {
    if (res.reason === 'level') {
      return msg.reply(`🔒 **${recipeName(recipe)}** needs ${PROFESSIONS[prof].name} Lv ${res.need} — you're Lv ${getProf(char, prof).level}.`);
    }
    return msg.reply(`Not enough materials for **${recipeName(recipe)}** — need ${inputsLine(char, recipe)}. Go gather more!`);
  }
  questProgress(char, 'craft', 1);
  savePlayer(msg.author.id, char);

  const meta = PROFESSIONS[prof];
  let out;
  if (res.gear) {
    out = `${meta.emoji} You craft **${RARITY_EMOJI[res.rarity.id] || ''} ${res.gear.name}** (${res.gear.slot})! It's in your bag — \`tt equip <#>\` to wear it.`;
  } else {
    out = `${meta.emoji} You ${verb} **${res.qty}× ${res.item.name}**!`;
  }
  out += ` +${recipe.xp} ${meta.name} XP (Lv ${res.profLevel}).`;
  if (res.levelsGained) out += ` 🎉 **${meta.name} level up!**`;
  return msg.reply(out);
}

function cmdRecipes(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const prof = /^(brew|alch|alchemy|alchemist|potion)/i.test(args[0] || '') ? 'alchemist' : 'crafter';
  return showRecipes(msg, char, prof, prof === 'alchemist' ? 'brew' : 'craft');
}

const cmdCraft = (msg, args) => cmdMake(msg, args, 'crafter', 'craft');
const cmdBrew = (msg, args) => cmdMake(msg, args, 'alchemist', 'brew');

// ── enchanting (Enchanter) ───────────────────────────────────────────────────
function cmdEnchant(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const list = enchantList(char);
  if (!list.length) return msg.reply('No gear to enchant yet — craft or find some first.');
  const cap = enchantCap(char);

  if (!args.length || !/^\d+$/.test(args[0])) {
    const lines = list.map((e, i) => {
      const lvl = e.item.enchant || 0;
      const c = nextCost(e.item);
      const where = e.where === 'equipped' ? '⚔️' : '🎒';
      const cost = lvl >= cap ? '**MAX**' : `${c.gold} 🪙 + ${c.quartz} ${REAGENT_NAME}`;
      return `\`${i + 1}\` ${where} ${RARITY_EMOJI[e.item.rarity] || '•'} ${e.item.name} — next: ${cost}`;
    });
    return msg.reply(
      `✨ **Enchanting** (Enchanter Lv ${getProf(char, 'enchanter').level}, cap +${cap}) — upgrade with \`tt enchant <#>\`\n` +
      lines.join('\n') +
      `\n\nEach enchant boosts the piece's stats. Reagent: **${REAGENT_NAME}** (mine it). You have ${countMat(char, 'quartz')}.`
    );
  }

  const entry = list[parseInt(args[0], 10) - 1];
  if (!entry) return msg.reply(`No item #${args[0]}. See \`tt enchant\`.`);

  const res = doEnchant(char, entry);
  if (!res.ok) {
    if (res.reason === 'maxed') return msg.reply(`**${entry.item.name}** is at your cap (+${res.cap}). Level up Enchanting to raise it.`);
    if (res.reason === 'gold') return msg.reply(`Not enough gold — that enchant costs ${res.cost.gold} 🪙 (you have ${char.gold || 0}).`);
    return msg.reply(`Not enough ${REAGENT_NAME} — need ${res.cost.quartz} (you have ${countMat(char, 'quartz')}). Mine more with \`tt mine\`.`);
  }
  savePlayer(msg.author.id, char);
  const stats = [];
  if (res.item.power != null) stats.push(`PWR ${res.item.power}`);
  if (res.item.defense != null) stats.push(`DEF ${res.item.defense}`);
  if (res.item.resist != null) stats.push(`RES ${res.item.resist}`);
  let out = `✨ Enchanted **${res.item.name}**! ${stats.join(' · ')}. +${res.xp} Enchanter XP (Lv ${res.profLevel}).`;
  if (res.levelsGained) out += ' 🎉 **Enchanter level up!**';
  return msg.reply(out);
}

// ── lootboxes (Lootboxer) ────────────────────────────────────────────────────
function rewardText(r) {
  if (r.type === 'gold') return `${r.gold} 🪙`;
  if (r.type === 'gear') return `${RARITY_EMOJI[r.rarity] || '•'} ${r.name}`;
  if (r.type === 'mat') return `${r.qty}× ${r.name}`;
  return r.name;
}

function cmdLootbox(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const sub = (args[0] || '').toLowerCase();
  const price = boxPrice(char);
  const boxes = getBoxes(char);

  if (sub === 'buy') {
    let n = parseInt(args[1], 10);
    if (!Number.isInteger(n) || n < 1) n = 1;
    n = Math.min(n, 50);
    const total = price * n;
    if ((char.gold || 0) < total) return msg.reply(`Not enough gold — ${n} box(es) cost ${total} 🪙 (you have ${char.gold || 0}).`);
    char.gold -= total;
    char.boxes = boxes + n;
    savePlayer(msg.author.id, char);
    return msg.reply(`🎁 Bought **${n}** mystery box(es) for ${total} 🪙. Open with \`tt lootbox open\`. (${char.gold} left)`);
  }

  if (sub === 'open' || /^\d+$/.test(sub)) {
    if (boxes < 1) return msg.reply(`No boxes to open. Buy one for ${price} 🪙 with \`tt lootbox buy\`.`);
    let n = /^\d+$/.test(sub) ? parseInt(sub, 10) : parseInt(args[1], 10);
    if (!Number.isInteger(n) || n < 1) n = 1;
    n = Math.min(n, boxes, 10);
    const results = [];
    let levels = 0, profLevel = 0;
    for (let i = 0; i < n; i++) {
      char.boxes -= 1;
      const o = openBox(char);
      levels += o.levelsGained; profLevel = o.profLevel;
      results.push(rewardText(o.reward));
      questProgress(char, 'box', 1);
    }
    savePlayer(msg.author.id, char);
    let out = `🎁 Opened **${n}** box(es): ${results.join(' · ')} — Lootboxer Lv ${profLevel}.`;
    if (levels) out += ' 🎉 **Lootboxer level up!**';
    if (char.boxes > 0) out += ` (${char.boxes} left)`;
    return msg.reply(out);
  }

  return msg.reply(
    `🎁 **Mystery Boxes** — you hold **${boxes}**.\n` +
    `Buy for **${price}** 🪙 each: \`tt lootbox buy [n]\`\n` +
    `Open: \`tt lootbox open [n]\`\n` +
    `Boxes drop gold, gear, materials or potions. Lootboxer Lv ${getProf(char, 'lootboxer').level} improves the odds.`
  );
}

// ── daily quests ─────────────────────────────────────────────────────────────
function cmdQuest(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const q = ensureQuest(char);

  if ((args[0] || '').toLowerCase() === 'claim') {
    const r = questClaim(char);
    if (!r.ok) {
      if (r.reason === 'claimed') return msg.reply("✅ You've already claimed today's quest — a new one arrives tomorrow.");
      return msg.reply(`Not done yet — **${q.desc}**: ${q.progress}/${q.target}.`);
    }
    savePlayer(msg.author.id, char);
    let out = `🎉 Quest complete! **+${r.q.gold} 🪙 · +${r.q.xp} XP**.`;
    if (r.levels.length) out += ` 🆙 You reached level **${char.level}**!`;
    return msg.reply(out);
  }

  savePlayer(msg.author.id, char); // persist a freshly-rolled quest
  const done = q.progress >= q.target;
  const status = q.claimed ? '✅ claimed' : done ? '✨ **ready!** — `tt quest claim`' : `${q.progress}/${q.target}`;
  return msg.reply(
    `📜 **Daily Quest** — ${q.desc}\n` +
    `Progress: ${status}\n` +
    `Reward: ${q.gold} 🪙 + ${q.xp} XP${q.claimed || done ? '' : ' · matching actions count automatically'}`
  );
}

// ── ascension (prestige) ─────────────────────────────────────────────────────
const ASCEND_LEVEL = 30;
const allZonesCleared = (char) => ZONE_LIST.every((z) => char.cleared && char.cleared[z.id]);

function cmdAscend(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `tt create` first.');
  const asc = char.ascension || 0;
  const eligible = char.level >= ASCEND_LEVEL || allZonesCleared(char);
  if (!eligible) {
    return msg.reply(`⭐ **Ascension** unlocks at level ${ASCEND_LEVEL} (or after clearing every zone). You're level ${char.level} — keep adventuring!`);
  }
  if ((args[0] || '').toLowerCase() !== 'confirm') {
    return msg.reply(
      `⭐ **Ascend?** Reset to level 1 and climb again — but keep your gold, gear, professions and boxes, and gain **+5% XP and +2% to all stats, permanently** (you'd become Ascension ${asc + 1}).\n` +
      'Confirm with `tt ascend confirm`.'
    );
  }
  char.ascension = asc + 1;
  char.level = 1;
  char.xp = 0;
  char.cleared = {};
  endFight(msg.author.id);
  const pd = derive(char);
  char.hp = pd.maxhp; char.mp = pd.maxmp;
  char.stamina = MAX_STAMINA; char.stamTs = Date.now();
  savePlayer(msg.author.id, char);
  return msg.reply(
    `🌟 **${char.name} ascends!** Now **Ascension ${char.ascension}** — +${5 * char.ascension}% XP and +${2 * char.ascension}% stats, forever. A new journey begins at level 1.`
  );
}

function cmdLeaderboard(msg) {
  const all = Object.values(allPlayers()).filter(Boolean);
  if (!all.length) return msg.reply('No heroes yet. Be the first with `tt create`!');
  const top = all.sort((a, b) =>
    ((b.ascension || 0) - (a.ascension || 0)) || (b.level - a.level) || ((b.xp || 0) - (a.xp || 0))
  ).slice(0, 10);
  const lines = top.map((c, i) => `${['🥇', '🥈', '🥉'][i] || `\`${i + 1}\``} **${c.name}** — ${c.ascension ? `⭐${c.ascension} ` : ''}Lv ${c.level} ${CLASSES[c.cls]?.name || ''}`);
  return msg.reply('🏆 **Top heroes**\n' + lines.join('\n'));
}

// ── dispatch ──────────────────────────────────────────────────────────────────
const COMMANDS = {
  rpg: cmdHelp, tavern: cmdHelp, tt: cmdHelp, tthelp: cmdHelp, help: cmdHelp, commands: cmdHelp,
  classes: cmdClasses, races: cmdRaces,
  create: cmdCreate, signup: cmdCreate, start: cmdCreate, char: cmdChar, sheet: cmdChar, me: cmdChar,
  skills: cmdSkills, zones: cmdZones,
  adventure: cmdAdventure, explore: cmdAdventure, hunt: cmdAdventure,
  boss: cmdBoss, raid: cmdRaid,
  chop: (m, a) => cmdGather(m, a, 'chop'), mine: (m, a) => cmdGather(m, a, 'mine'),
  fish: (m, a) => cmdGather(m, a, 'fish'), forage: (m, a) => cmdGather(m, a, 'forage'),
  dig: (m, a) => cmdGather(m, a, 'dig'), scavenge: (m, a) => cmdGather(m, a, 'scavenge'),
  recipes: cmdRecipes, craft: cmdCraft, brew: cmdBrew, enchant: cmdEnchant,
  lootbox: cmdLootbox, box: cmdLootbox, boxes: cmdLootbox,
  quest: cmdQuest, quests: cmdQuest, daily: cmdQuest,
  ascend: cmdAscend, ascension: cmdAscend, prestige: cmdAscend,
  attack: cmdAttack, a: cmdAttack,
  skill: cmdSkill, cast: cmdSkill,
  use: cmdUse, potion: cmdUse,
  flee: cmdFlee, run: cmdFlee,
  status: cmdStatus, fight: cmdStatus,
  inv: cmdInv, inventory: cmdInv, bag: cmdInv,
  inspect: cmdInspect, item: cmdInspect, examine: cmdInspect,
  guide: cmdGuide, wiki: cmdGuide,
  profile: cmdProfile, pf: cmdProfile,
  web: cmdWeb, play: cmdWeb, browser: cmdWeb, online: cmdWeb,
  shop: cmdShop, store: cmdShop, buy: cmdBuy, sell: cmdSell,
  equip: cmdEquip, rest: cmdRest,
  leaderboard: cmdLeaderboard, lb: cmdLeaderboard,
  deletechar: cmdDelete,
  new: cmdNew, restart: cmdNew, reroll: cmdNew, startover: cmdNew,
};

// Every Tavern Tales command is "tt <command> [args]". The legacy "!command"
// still works so nothing breaks for people used to it.
const TT = 'tt';

export function isRpgCommand(content) {
  const low = (content || '').trim().toLowerCase();
  if (low === TT || low.startsWith(TT + ' ')) return true;          // claim all "tt ..."
  if (low.startsWith(PREFIX)) return !!COMMANDS[low.slice(PREFIX.length).split(/\s+/)[0]];
  return false;
}

export async function handleRpg(msg) {
  const raw = (msg.content || '').trim();
  const low = raw.toLowerCase();
  let body, viaTt = false;
  if (low === TT) { body = 'help'; viaTt = true; }
  else if (low.startsWith(TT + ' ')) { body = raw.slice(3).trim(); viaTt = true; }
  else if (raw.startsWith(PREFIX)) { body = raw.slice(PREFIX.length).trim(); }
  else return;

  const parts = body.split(/\s+/);
  const name = (parts.shift() || '').toLowerCase();
  const fn = COMMANDS[name];
  if (!fn) { if (viaTt) await msg.reply('Unknown command — try `tt help`.'); return; }
  await fn(msg, parts);
}

// Flatten an embed to plain text (for non-Discord platforms).
function embedToText(e) {
  const d = e?.data || {};
  const s = [];
  if (d.title) s.push(d.title);
  if (d.description) s.push(d.description);
  for (const f of d.fields || []) s.push(`${f.name}: ${f.value}`);
  if (d.footer?.text) s.push(d.footer.text);
  return s.join('\n');
}

// Collapse a rich reply into a single chat-safe line (Twitch etc. are one-line).
function toChatLine(t) {
  return String(t).replace(/\*\*/g, '').replace(/`/g, '').replace(/\n+/g, ' · ')
    .replace(/\s{2,}/g, ' ').trim().slice(0, 480);
}

/**
 * Run a game command for a non-Discord chatter (already resolved to a Discord id),
 * reusing the exact same handlers, and return a one-line plain-text reply.
 */
export async function runForChat({ discordId, username, content, guildId, client, auto = false }) {
  let captured = null;
  const msg = {
    author: { id: discordId, username, bot: false },
    content,
    guildId,      // for raids (per-server)
    client,       // for resolving the announce channel
    _chat: true,  // stream chat: manual mode, no auto-battle loop / message editing
    _auto: auto,  // Twitch/relay: resolve the whole fight and reply with one summary
    reply: async (payload) => { captured = payload; return {}; },
  };
  await handleRpg(msg);
  if (captured == null) return null;
  if (typeof captured === 'string') return toChatLine(captured);
  const parts = [];
  if (captured.content) parts.push(captured.content);
  for (const e of captured.embeds || []) parts.push(embedToText(e));
  return toChatLine(parts.join('\n'));
}
