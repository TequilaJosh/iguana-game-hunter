import { EmbedBuilder } from 'discord.js';
import {
  RACE_LIST, CLASS_LIST, ZONE_LIST, RACES, CLASSES, ZONES, RARITIES, STAT_KEYS, ITEMS,
  skillsForClass,
} from './content.js';
import { derive, xpToNext, startingKit, gearScore, clamp, shopInventory, sellValue, makeGear } from './engine.js';
import { getPlayer, savePlayer, deletePlayer, allPlayers, nextStaminaMs, MAX_STAMINA } from './store.js';
import {
  getFight, hasFight, endFight, startFight, takeTurn, resolveWin, resolveLoss,
  pickEncounter, addItem,
} from './fights.js';

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
      'In combat: `!attack` · `!skill <name>` · `!use` (potion) · `!flee`\n\n' +
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
  const char = { name, cls: cls.id, race: race.id, level: 1, xp: 0, gold: 25 };
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
  const lines = skills.map((s) => `**${s.name}** — ${s.mp} MP · ${s.type}${s.power ? ` · pow ${s.power}` : ''}`);
  let out = `✨ **${CLASSES[char.cls].name} skills (Lvl ${char.level})**\n` + (lines.join('\n') || '_none yet_');
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

function cmdAdventure(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  if (hasFight(msg.author.id)) return msg.reply('You’re already in a fight! `!attack`, `!skill`, `!use`, or `!flee`.');

  let zone;
  if (args[0]) {
    const q = args.join(' ').toLowerCase();
    zone = ZONE_LIST.find((z) => z.id === q || z.name.toLowerCase() === q || z.name.toLowerCase().includes(q));
    if (!zone) return msg.reply('No such zone. See `!zones`.');
    if (char.level < zone.level_required) return msg.reply(`🔒 **${zone.name}** needs level ${zone.level_required}. You’re ${char.level}.`);
  } else {
    zone = bestZoneFor(char.level);
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
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

async function afterTurn(msg, char, res) {
  const fight = getFight(msg.author.id);
  if (res.error) return msg.reply(`⚠️ ${res.error}`);
  if (fight) fight.log.push(...res.log);

  if (res.fled) {
    char.hp = fight.php; char.mp = fight.pmp;
    endFight(msg.author.id); savePlayer(msg.author.id, char);
    return msg.reply('🏃 ' + res.log.join('\n'));
  }
  if (res.win) {
    const reward = resolveWin(fight, char);
    endFight(msg.author.id); savePlayer(msg.author.id, char);
    const e = new EmbedBuilder().setColor(0x3fa34d).setTitle(`🏆 ${fight.monster.name} defeated!`);
    let desc = res.log.join('\n') + `\n\n**+${reward.xp} XP · +${reward.gold} 🪙**`;
    if (reward.items.length) {
      desc += '\n\n**Loot:**\n' + reward.items.map((i) => `${RARITY_EMOJI[i.rarity] || '•'} ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}`).join('\n');
    }
    if (reward.levels.length) desc += `\n\n🎉 **LEVEL UP!** You’re now level **${char.level}** (fully healed).`;

    // What players can do next.
    const next = ['`!adventure`'];
    if (reward.levels.length) next.push('`!skills`');
    if (reward.items.length) next.push('`!inv`');
    next.push('`!char`', '`!shop`', '`!rest`');
    desc += `\n\n▶️ **Next:** ${next.join(' · ')}`;

    e.setDescription(desc);
    return msg.reply({ embeds: [e] });
  }
  if (res.lose) {
    const { lost } = resolveLoss(char);
    endFight(msg.author.id); savePlayer(msg.author.id, char);
    const e = new EmbedBuilder().setColor(0xd64f4f).setTitle('💀 You have fallen…')
      .setDescription(res.log.join('\n') + `\n\nYou wake at the tavern, down **${lost}** 🪙 but alive. Rest with \`!rest\`.`);
    return msg.reply({ embeds: [e] });
  }
  return msg.reply({ embeds: [fightEmbed(fight)] });
}

function cmdAttack(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `!adventure` to find one.');
  return afterTurn(msg, char, takeTurn(fight, 'attack'));
}

function cmdSkill(msg, args) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight. `!adventure` to find one.');
  const q = args.join(' ').toLowerCase().trim();
  if (!q) return msg.reply('Which skill? `!skill <name>` — see `!skills`.');
  const list = skillsForClass(char.cls, char.level);
  const skill = list.find((s) => s.id === q || s.name.toLowerCase() === q)
    || list.find((s) => s.name.toLowerCase().includes(q) || s.id.includes(q));
  if (!skill) return msg.reply(`No skill "${args.join(' ')}". Yours: ${list.map((s) => s.name).join(', ') || 'none'}.`);
  return afterTurn(msg, char, takeTurn(fight, 'skill', skill));
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
  return afterTurn(msg, char, takeTurn(fight, 'use', heal));
}

function cmdFlee(msg) {
  const char = getPlayer(msg.author.id);
  const fight = getFight(msg.author.id);
  if (!char || !fight) return msg.reply('You’re not in a fight.');
  return afterTurn(msg, char, takeTurn(fight, 'flee'));
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
  let out = `🎒 **${char.name}'s bag** — ${char.gold || 0} 🪙\n`;
  if (gear.length) out += '\n**Gear** (equip with `!equip <#>`)\n' + gear.map((i, n) => `\`${n + 1}\` ${RARITY_EMOJI[i.rarity] || '•'} ${i.name} — ${i.slot}`).join('\n');
  if (other.length) out += '\n\n**Items**\n' + other.map((i) => `• ${i.name}${i.qty > 1 ? ` x${i.qty}` : ''}`).join('\n');
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
  const q = args.join(' ').toLowerCase().trim();
  if (!q) return msg.reply('Buy what? `!buy <name>` — see `!shop`.');
  const shop = shopInventory(char);
  const pick = shop.find((s) => s.name.toLowerCase() === q || s.id === q) || shop.find((s) => s.name.toLowerCase().includes(q));
  if (!pick) return msg.reply(`"${args.join(' ')}" isn't in the shop. See \`!shop\`.`);
  if ((char.gold || 0) < pick.price) return msg.reply(`Not enough gold — ${pick.name} costs ${pick.price} 🪙, you have ${char.gold || 0}.`);
  char.gold -= pick.price;
  const base = ITEMS[pick.id];
  if (GEAR_SLOTS.includes(base.slot)) addItem(char, makeGear(base.id, RARITIES[0]));
  else addItem(char, { base: base.id, slot: base.slot, name: base.name, qty: 1, stackable: !!base.stackable, effect: base.effect, magnitude: base.magnitude, value: base.value });
  savePlayer(msg.author.id, char);
  return msg.reply(`🛒 Bought **${pick.name}** for ${pick.price} 🪙. (${char.gold} left)${GEAR_SLOTS.includes(base.slot) ? ' Equip it with `!inv` → `!equip <#>`.' : ''}`);
}

function cmdSell(msg, args) {
  const char = getPlayer(msg.author.id);
  if (!char) return msg.reply('No hero yet — `!create` first.');
  const gear = (char.inventory || []).filter((i) => GEAR_SLOTS.includes(i.slot));
  const idx = parseInt(args[0], 10) - 1;
  const item = gear[idx];
  if (!item) return msg.reply('Sell which? `!sell <#>` — numbers from `!inv`.');
  const price = sellValue(item);
  char.gold = (char.gold || 0) + price;
  char.inventory = char.inventory.filter((i) => i !== item);
  savePlayer(msg.author.id, char);
  return msg.reply(`💰 Sold **${item.name}** for ${price} 🪙. (${char.gold} total)`);
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

export function isRpgCommand(content) {
  if (!content.startsWith(PREFIX)) return false;
  const name = content.slice(PREFIX.length).trim().split(/\s+/)[0]?.toLowerCase();
  return !!COMMANDS[name];
}

export async function handleRpg(msg) {
  const parts = msg.content.slice(PREFIX.length).trim().split(/\s+/);
  const name = (parts.shift() || '').toLowerCase();
  const fn = COMMANDS[name];
  if (!fn) return;
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
export async function runForChat({ discordId, username, content }) {
  let captured = null;
  const msg = {
    author: { id: discordId, username, bot: false },
    content,
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
