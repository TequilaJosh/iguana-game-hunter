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
import { getGuild } from '../guildStore.js';

const PREFIX = '!';
const GEAR_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const RARITY_EMOJI = { common: '⚪', uncommon: '🟢', rare: '🔵', epic: '🟣', legendary: '🟡' };
const cap = (s) => s.charAt(0).toUpperCase() + s.slice(1);

function bar(cur, max, width = 12) {
  const p = clamp(max > 0 ? cur / max : 0, 0, 1);
  const f = Math.round(p * width);
  return '`' + '█'.repeat(f) + '░'.repeat(width - f) + '`';
}

// ── formatting ────────────────────────────────────────────────────────────────
function sheetEmbed(char) {
  const pd = derive(char);
  const race = RACES[char.race], cls = CLASSES[char.cls];
  const stam = char.stamina ?? MAX_STAMINA;
  const stats = STAT_KEYS.map((k) => `${k.toUpperCase()} ${pd.st[k]}`).join(' · ');
  const gear = GEAR_SLOTS
    .map((s) => char.equipped?.[s] ? `${RARITY_EMOJI[char.equipped[s].rarity] || ''} ${char.equipped[s].name}` : null)
    .filter(Boolean).join('\n') || 'Nothing equipped';
  return new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle(`${char.name} — Lvl ${char.level} ${race.name} ${cls.name}`)
    .setDescription(cls.blurb)
    .addFields(
      { name: 'HP', value: `${char.hp ?? pd.maxhp}/${pd.maxhp}`, inline: true },
      { name: 'MP', value: `${char.mp ?? pd.maxmp}/${pd.maxmp}`, inline: true },
      { name: 'XP', value: `${char.xp || 0}/${xpToNext(char.level)}`, inline: true },
      { name: 'Gold', value: `${char.gold || 0} 🪙`, inline: true },
      { name: 'Stamina', value: `${stam}/${MAX_STAMINA} ⚡`, inline: true },
      { name: 'Worker', value: `Lv ${getWorker(char).level} (${getWorker(char).xp}/${workerXpToNext(getWorker(char).level)})`, inline: true },
      { name: 'Power', value: `ATK ~${Math.round(pd.st[pd.scales] * 1.3 + pd.wpow * 1.4)} · DEF ${pd.def} · RES ${pd.res}`, inline: true },
      { name: 'Stats', value: stats },
      { name: 'Equipped', value: gear },
    )
    .setFooter({ text: 'Adventure with !adventure · gear up with !inv / !equip' });
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
    .setFooter({ text: '!attack · !skill <name> · !use · !flee' });
}

// ── command handlers ──────────────────────────────────────────────────────────
async function cmdHelp(msg) {
  const e = new EmbedBuilder()
    .setColor(0x7cc44a)
    .setTitle('🍺 Tavern Tales — commands')
    .setDescription(
      '**Getting started**\n' +
      '`!classes` `!races` — see your options\n' +
      '`!create <class> <race> [name]` — roll a hero\n' +
      '`!char` — your character sheet · `!skills` — your abilities\n\n' +
      '**Adventuring**\n' +
      '`!zones` — where you can go\n' +
      '`!adventure [zone]` — find a fight (costs ⚡ stamina)\n' +
      '`!boss` — challenge your zone’s boss to unlock the next zone\n' +
      'In combat: `!attack` starts auto-battle · `!skill <name>` · `!use` · `!flee`\n\n' +
      '**Raids** (a boss appears now and then — team up!)\n' +
      '`!raid join` · `!raid skill <name>` · `!raid use` · `!raid revive`\n\n' +
      '**Gathering** (no `!` needed — just type the word)\n' +
      '`chop` `mine` `fish` `forage` `dig` `scavenge` — gather materials + Worker XP (3-min cooldown each)\n\n' +
      '**Gear & town**\n' +
      '`!inv` — bag & gold · `!equip <#>` — wear gear\n' +
      '`!shop` — buy gear/potions · `!buy <name>` · `!sell <#>`\n' +
      '`!rest` — recover HP/MP · `!leaderboard` — top heroes\n\n' +
      '**Playing from stream chat**\n' +
      '`!play <your Discord @username>` then `!confirm <code>` — link your chat account to your Discord hero so your progress follows you everywhere.'
    );
  return msg.reply({ embeds: [e] });
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
  if (getPlayer(msg.author.id)) return msg.reply('You already have a hero — see `!char`. (Start over with `!deletechar`.)');
  const clsQ = (args[0] || '').toLowerCase();
  const raceQ = (args[1] || '').toLowerCase();
  const cls = CLASS_LIST.find((c) => c.id === clsQ || c.name.toLowerCase() === clsQ);
  const race = RACE_LIST.find((r) => r.id === raceQ || r.name.toLowerCase() === raceQ);
  if (!cls || !race) {
    return msg.reply(
      'Usage: `!create <class> <race> [name]`\n' +
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
  if (!char) return msg.reply('No hero yet — make one with `!create <class> <race> [name]`.');
  return msg.reply({ embeds: [sheetEmbed(char)] });
}

function cmdSkills(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const skills = skillsForClass(char.cls, char.level);
  const locked = skillsForClass(char.cls, 99).filter((s) => s.unlock_level > char.level);
  const lines = skills.map((s, i) => `\`${i + 1}\` **${s.name}** — ${s.mp} MP · ${s.type}${s.power ? ` · pow ${s.power}` : ''}`);
  let out = `✨ **${CLASSES[char.cls].name} skills (Lvl ${char.level})** — cast with \`skill <#>\`\n` + (lines.join('\n') || '_none yet_');
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
  return msg.reply('🗺️ **Zones**\n' + lines.join('\n') + (char ? '\n\nTravel with `!adventure <zone>` (or just `!adventure` for the best one you can enter).' : '\n\nMake a hero first with `!create`.'));
}

function bestZoneFor(level) {
  const eligible = ZONE_LIST.filter((z) => level >= z.level_required);
  return eligible[eligible.length - 1] || ZONE_LIST[0];
}

async function cmdAdventure(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  if (hasFight(msg.author.id)) return msg.reply('You’re already in a fight! `!attack`, `!skill`, `!use`, or `!flee`.');

  let zone;
  if (args[0]) {
    const q = args.join(' ').toLowerCase();
    zone = ZONE_LIST.find((z) => z.id === q || z.name.toLowerCase() === q || z.name.toLowerCase().includes(q));
    if (!zone) return msg.reply('No such zone. See `!zones`.');
    if (!isZoneUnlocked(char, zone)) return msg.reply(`🔒 **${zone.name}** is locked — beat the previous zone's boss (\`!boss\`) to unlock it.`);
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

  // Stream chat can't edit messages, so it stays manual (type !attack each turn).
  if (msg._chat) return msg.reply({ embeds: [fightEmbed(fight)] });

  // Discord: show the encounter and WAIT. The live auto-battle starts on their first
  // action (registered but not armed yet).
  const sent = await msg.reply({
    content: '⚔️ **Type `!attack` to begin!** (or `!skill <name>` · `!flee`)',
    embeds: [fightEmbed(fight)],
  });
  autos.set(msg.author.id, { message: sent, char, timer: null });
}

async function cmdBoss(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  if (hasFight(msg.author.id)) return msg.reply('Finish your current fight first!');

  let zone;
  if (args[0]) {
    const q = args.join(' ').toLowerCase();
    zone = ZONE_LIST.find((z) => z.id === q || z.name.toLowerCase() === q || z.name.toLowerCase().includes(q));
    if (!zone) return msg.reply('No such zone. See `!zones`.');
  } else {
    zone = currentBossZone(char);
  }
  if (!isZoneUnlocked(char, zone)) return msg.reply(`🔒 **${zone.name}** is locked — clear the previous zone's boss first.`);

  const fight = startFight(msg.author.id, char, bossForZone(zone), zone.id);
  fight.bossZone = zone.id;
  fight.log.push(`⚔️ You challenge **${fight.monster.name}**, the boss of **${zone.name}**!`);
  if (msg._chat) return msg.reply({ embeds: [fightEmbed(fight)] });
  const sent = await msg.reply({ content: '👑 **BOSS FIGHT!** Type `!attack` to begin!', embeds: [fightEmbed(fight)] });
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
    return msg.reply(r.error || `⚔️ **${char?.name || 'You'}** joined the raid! Your hero auto-attacks — interject with \`!raid skill <name>\`, \`!raid use\`, or \`!raid revive\`.`);
  }
  if (sub === 'skill' || sub === 'cast') {
    if (!char) return msg.reply('Make a hero first with `!create`.');
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
    if (!char) return msg.reply('Make a hero first with `!create`.');
    if (raid) return msg.reply({ embeds: [raidEmbed(raid)] });
    const r = startRaid(gid, char, msg.author.id, await raidChannel(msg, gid));
    if (r.error) return msg.reply(r.error);
    return msg.reply(`🐉 **You summoned a raid** on **${r.raid.boss.name}** (T${r.zone.tier} · ${r.zone.name})! \`!raid join\` to fight — it auto-battles for up to 1 hour.`);
  }

  if (!raid) return msg.reply('No active raid right now. They pop up every 1–3 hours — or start one with `!raid start`.');
  return msg.reply({ embeds: [raidEmbed(raid)] });
}

// ── shared win/lose rendering ────────────────────────────────────────────────
function victoryEmbed(fight, reward, char) {
  let desc = fight.log.slice(-6).join('\n') + `\n\n**+${reward.xp} XP · +${reward.gold} 🪙**`;
  if (reward.items.length) desc += '\n\n**Loot:**\n' + reward.items.map((i) => `${RARITY_EMOJI[i.rarity] || '•'} ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}`).join('\n');
  if (reward.levels.length) desc += `\n\n🎉 **LEVEL UP!** You’re now level **${char.level}** (fully healed).`;
  if (reward.clearedBoss) desc += reward.unlocked
    ? `\n\n🗺️ **BOSS DEFEATED!** New zone unlocked: **${reward.unlocked}**!`
    : `\n\n👑 **BOSS DEFEATED!** You've conquered the final zone!`;
  const next = ['`!adventure`'];
  if (reward.levels.length) next.push('`!skills`');
  if (reward.items.length) next.push('`!inv`');
  next.push('`!char`', '`!shop`', '`!rest`');
  desc += `\n\n▶️ **Next:** ${next.join(' · ')}`;
  return new EmbedBuilder().setColor(0x3fa34d).setTitle(`🏆 ${fight.monster.name} defeated!`).setDescription(desc);
}
function defeatEmbed(fight, lost) {
  return new EmbedBuilder().setColor(0xd64f4f).setTitle('💀 You have fallen…')
    .setDescription(fight.log.slice(-6).join('\n') + `\n\nYou wake at the tavern, down **${lost}** 🪙 but alive. Rest with \`!rest\`.`);
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
  if (res.win) { stopAuto(uid); const reward = resolveWin(fight, char); endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '', embeds: [victoryEmbed(fight, reward, char)] }).catch(() => {}); return; }
  if (res.lose) { stopAuto(uid); const { lost } = resolveLoss(char); endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '', embeds: [defeatEmbed(fight, lost)] }).catch(() => {}); return; }
  if (res.fled) { stopAuto(uid); char.hp = fight.php; char.mp = fight.pmp; endFight(uid); savePlayer(uid, char); await a.message.edit({ content: '🏃 You fled the fight.', embeds: [] }).catch(() => {}); return; }
  const foot = resume ? '' : '\n⏸️ Paused — type `!attack` to resume auto-attacking.';
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
  if (res.win) { const reward = resolveWin(fight, char); endFight(msg.author.id); savePlayer(msg.author.id, char); return msg.reply({ embeds: [victoryEmbed(fight, reward, char)] }); }
  if (res.lose) { const { lost } = resolveLoss(char); endFight(msg.author.id); savePlayer(msg.author.id, char); return msg.reply({ embeds: [defeatEmbed(fight, lost)] }); }
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

function cmdAttack(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `!adventure` to find one.');
  return act(msg, char, takeTurn(fight, 'attack'), true); // attack (re)starts the auto-loop
}

function cmdSkill(msg, args) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `!adventure` to find one.');
  const q = args.join(' ').toLowerCase().trim();
  if (!q) return msg.reply('Which skill? `skill <name>` or `skill 1` — see `!skills`.');
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
  if (!char || !fight) return msg.reply('You can only quaff potions in a fight right now. `!rest` to heal in town.');
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
  if (!fight) return msg.reply('You’re not in a fight. `!adventure` to find one.');
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

function cmdInv(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const inv = char.inventory || [];
  const gear = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
  const other = inv.filter((i) => !GEAR_SLOTS.includes(i.slot));
  const junkValue = other.filter((i) => i.slot === 'material').reduce((s, m) => s + (m.value || 1) * (m.qty || 1), 0);
  let out = `🎒 **${char.name}'s bag** — ${char.gold || 0} 🪙\n`;
  if (gear.length) out += '\n**Gear** (equip with `!equip <#>`, sell with `!sell <#>`)\n' + gear.map((i, n) => `\`${n + 1}\` ${RARITY_EMOJI[i.rarity] || '•'} ${i.name} — ${i.slot}`).join('\n');
  if (other.length) out += '\n\n**Items**\n' + other.map((i) => `• ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}${i.slot === 'material' ? ` (${(i.value || 1) * (i.qty || 1)} 🪙)` : ''}`).join('\n');
  if (junkValue > 0) out += `\n\n_Sell all materials with \`!sell junk\` (+${junkValue} 🪙)_`;
  if (!gear.length && !other.length) out += '\n_Empty. Go adventuring!_';
  return msg.reply(out);
}

function cmdShop(msg) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const shop = shopInventory(char);
  const eq = char.equipped || {};
  const lines = shop.map((s, n) => {
    const base = ITEMS[s.id];
    if (!GEAR_SLOTS.includes(base.slot)) {
      const heal = base.effect === 'heal_pct' ? ` · heals ${base.magnitude}% HP` : '';
      return `\`${n + 1}\` ${s.name} — **${s.price}** 🪙${heal}`;
    }
    const parts = [];
    if (base.power) parts.push(`PWR ${base.power}`);
    if (base.defense) parts.push(`DEF ${base.defense}`);
    if (base.resist) parts.push(`RES ${base.resist}`);
    for (const [k, v] of Object.entries(base.stat_bonus || {})) parts.push(`${k.toUpperCase()}+${v}`);
    const d = gearScore(base) - (eq[base.slot] ? gearScore(eq[base.slot]) : 0);
    const cmp = d > 0 ? `▲ +${d}` : d < 0 ? `▼ ${d}` : '= same';
    return `\`${n + 1}\` ${s.name} — **${s.price}** 🪙 · ${parts.join(' ')} · vs equipped ${cmp}`;
  });
  return msg.reply(`🏪 **Shop** (you have ${char.gold || 0} 🪙)\n` + lines.join('\n') +
    '\n\nBuy with `!buy <name>` · sell gear with `!sell <#>` (from `!inv`).');
}

function cmdBuy(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  if (!args.length) return msg.reply('Buy what? `!buy <#>` (from `!shop`), with an optional quantity: `!buy 2 5`.');
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
  if (!pick) return msg.reply(`Not in the shop. See \`!shop\` (buy by number, e.g. \`!buy 1\`).`);

  qty = Math.max(1, Math.min(qty, 99));
  const total = pick.price * qty;
  if ((char.gold || 0) < total) return msg.reply(`Not enough gold — ${qty}× ${pick.name} costs ${total} 🪙, you have ${char.gold || 0}.`);
  char.gold -= total;
  const base = ITEMS[pick.id];
  for (let i = 0; i < qty; i++) {
    if (GEAR_SLOTS.includes(base.slot)) addItem(char, makeGear(base.id, RARITIES[0]));
    else addItem(char, { base: base.id, slot: base.slot, name: base.name, qty: 1, stackable: !!base.stackable, effect: base.effect, magnitude: base.magnitude, value: base.value });
  }
  savePlayer(msg.author.id, char);
  return msg.reply(`🛒 Bought **${qty}× ${pick.name}** for ${total} 🪙. (${char.gold} left)${GEAR_SLOTS.includes(base.slot) ? ' Equip with `!inv` → `!equip <#>`.' : ''}`);
}

function cmdSell(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const inv = char.inventory || [];
  const arg = args.join(' ').toLowerCase().trim();

  // Sell all gear at once.
  if (['allgear', 'gear', 'equipment', 'equip', 'weapons'].includes(arg)) {
    const g = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
    if (!g.length) return msg.reply('No gear in your bag to sell (equipped items are safe).');
    const total = g.reduce((s, it) => s + sellValue(it), 0);
    char.inventory = inv.filter((i) => !GEAR_SLOTS.includes(i.slot));
    char.gold = (char.gold || 0) + total;
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold ${g.length} piece(s) of gear for **${total}** 🪙. (${char.gold} total)`);
  }

  // Sell all materials/junk at once.
  if (['junk', 'all', 'mats', 'materials', 'trash'].includes(arg)) {
    const junk = inv.filter((i) => i.slot === 'material');
    if (!junk.length) return msg.reply('No materials to sell.');
    const total = junk.reduce((s, m) => s + (m.value || 1) * (m.qty || 1), 0);
    const count = junk.reduce((s, m) => s + (m.qty || 1), 0);
    char.inventory = inv.filter((i) => i.slot !== 'material');
    char.gold = (char.gold || 0) + total;
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold ${count} material(s) for **${total}** 🪙. (${char.gold} total)`);
  }

  // Sell a gear item by its !inv number.
  const gear = inv.filter((i) => GEAR_SLOTS.includes(i.slot));
  const idx = parseInt(args[0], 10) - 1;
  if (Number.isInteger(idx) && idx >= 0 && gear[idx]) {
    const item = gear[idx];
    const price = sellValue(item);
    char.gold = (char.gold || 0) + price;
    char.inventory = inv.filter((i) => i !== item);
    savePlayer(msg.author.id, char);
    return msg.reply(`💰 Sold **${item.name}** for ${price} 🪙. (${char.gold} total)`);
  }

  // Sell by name (materials, spare potions, or gear) — whole stack.
  if (arg) {
    const item = inv.find((i) => i.name.toLowerCase() === arg) || inv.find((i) => i.name.toLowerCase().includes(arg));
    if (item) {
      const price = item.slot === 'material' ? (item.value || 1) * (item.qty || 1) : sellValue(item) * (item.qty || 1);
      char.gold = (char.gold || 0) + price;
      char.inventory = inv.filter((i) => i !== item);
      savePlayer(msg.author.id, char);
      return msg.reply(`💰 Sold **${item.name}**${item.qty > 1 ? ` x${item.qty}` : ''} for ${price} 🪙. (${char.gold} total)`);
    }
  }

  return msg.reply('Sell what? `!sell <#>` (gear from `!inv`) · `!sell junk` (all materials) · `!sell <name>`.');
}

function cmdEquip(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const gear = (char.inventory || []).filter((i) => GEAR_SLOTS.includes(i.slot));
  const idx = parseInt(args[0], 10) - 1;
  const item = Number.isInteger(idx) ? gear[idx] : gear.find((i) => i.name.toLowerCase().includes(args.join(' ').toLowerCase()));
  if (!item) return msg.reply('Which item? `!equip <#>` — numbers from `!inv`.');
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
  if (!char) return msg.reply('No hero yet — `!create` first.');
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
    return msg.reply(`⚠️ This permanently deletes **${char.name}** (Lvl ${char.level}). Type \`!deletechar confirm\` to proceed.`);
  }
  endFight(msg.author.id);
  deletePlayer(msg.author.id);
  return msg.reply('🪦 Character deleted. `!create` to begin anew.');
}

// ── gathering (Worker profession) ────────────────────────────────────────────
const GATHER_EMOJI = { chop: '🪓', mine: '⛏️', fish: '🎣', forage: '🌿', dig: '🪏', scavenge: '♻️' };

function cmdGather(msg, args, command) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const res = gather(char, command);
  if (res.status === 'cooldown') {
    const s = Math.ceil(res.remaining / 1000);
    return msg.reply(`⏳ **${command}** is resting — ${Math.floor(s / 60)}m ${s % 60}s left.`);
  }
  if (res.status === 'nothing') return msg.reply(`There's nothing to ${command} around here.`);
  savePlayer(msg.author.id, char);
  const e = GATHER_EMOJI[command] || '⛏️';
  let out = `${e} You ${command} and gather **${res.qty}× ${res.material}**! +${res.xp} Worker XP (Worker Lv ${res.workerLevel}).`;
  if (res.levelsGained) out += ' 🎉 **Worker level up!**';
  out += ' Ready again in 3m — sell mats with `!sell junk`.';
  return msg.reply(out);
}

function cmdLeaderboard(msg) {
  const all = Object.values(allPlayers()).filter(Boolean);
  if (!all.length) return msg.reply('No heroes yet. Be the first with `!create`!');
  const top = all.sort((a, b) => (b.level - a.level) || ((b.xp || 0) - (a.xp || 0))).slice(0, 10);
  const lines = top.map((c, i) => `${['🥇', '🥈', '🥉'][i] || `\`${i + 1}\``} **${c.name}** — Lv ${c.level} ${CLASSES[c.cls]?.name || ''}`);
  return msg.reply('🏆 **Top heroes**\n' + lines.join('\n'));
}

// ── dispatch ──────────────────────────────────────────────────────────────────
const COMMANDS = {
  rpg: cmdHelp, tavern: cmdHelp, tt: cmdHelp, tthelp: cmdHelp, help: cmdHelp, commands: cmdHelp,
  classes: cmdClasses, races: cmdRaces,
  create: cmdCreate, char: cmdChar, sheet: cmdChar, me: cmdChar,
  skills: cmdSkills, zones: cmdZones,
  adventure: cmdAdventure, explore: cmdAdventure, hunt: cmdAdventure,
  boss: cmdBoss, raid: cmdRaid,
  chop: (m, a) => cmdGather(m, a, 'chop'), mine: (m, a) => cmdGather(m, a, 'mine'),
  fish: (m, a) => cmdGather(m, a, 'fish'), forage: (m, a) => cmdGather(m, a, 'forage'),
  dig: (m, a) => cmdGather(m, a, 'dig'), scavenge: (m, a) => cmdGather(m, a, 'scavenge'),
  attack: cmdAttack, a: cmdAttack,
  skill: cmdSkill, cast: cmdSkill,
  use: cmdUse, potion: cmdUse,
  flee: cmdFlee, run: cmdFlee,
  status: cmdStatus, fight: cmdStatus,
  inv: cmdInv, inventory: cmdInv, bag: cmdInv,
  shop: cmdShop, store: cmdShop, buy: cmdBuy, sell: cmdSell,
  equip: cmdEquip, rest: cmdRest,
  leaderboard: cmdLeaderboard, lb: cmdLeaderboard,
  deletechar: cmdDelete,
};

// These commands also work WITHOUT the "!" prefix (just type the word).
const BARE_COMMANDS = new Set([
  'chop', 'mine', 'fish', 'forage', 'dig', 'scavenge',
  'attack', 'a', 'skill', 'cast', 'use', 'potion', 'flee', 'run',
]);

export function isRpgCommand(content) {
  content = (content || '').trim();
  const bare = !content.startsWith(PREFIX);
  const word = (bare ? content : content.slice(PREFIX.length)).trim().split(/\s+/)[0]?.toLowerCase();
  if (!word || !COMMANDS[word]) return false;
  return bare ? BARE_COMMANDS.has(word) : true;
}

export async function handleRpg(msg) {
  const raw = (msg.content || '').trim();
  const bare = !raw.startsWith(PREFIX);
  const body = bare ? raw : raw.slice(PREFIX.length);
  const parts = body.trim().split(/\s+/);
  const name = (parts.shift() || '').toLowerCase();
  const fn = COMMANDS[name];
  if (!fn) return;
  if (bare) {
    if (!BARE_COMMANDS.has(name)) return;   // only certain words work bare
    if (!getPlayer(msg.author.id)) return;  // silent for non-players (avoid triggering on normal chat)
  }
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
export async function runForChat({ discordId, username, content, guildId, client }) {
  let captured = null;
  const msg = {
    author: { id: discordId, username, bot: false },
    content,
    guildId,      // for raids (per-server)
    client,       // for resolving the announce channel
    _chat: true,  // stream chat: manual mode, no auto-battle loop / message editing
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
