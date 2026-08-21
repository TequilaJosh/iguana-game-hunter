import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { config } from './config.js';
import { log } from './logger.js';
import { getPlayer, savePlayer, allPlayers } from './game/store.js';
import { ensureQuest } from './game/quests.js';
import { merchantSale } from './game/professions.js';
import { CLASSES, RACES, CLASS_LIST, RACE_LIST, ZONE_LIST, STAT_KEYS, skillsForClass, ITEMS } from './game/content.js';
import { derive, xpToNext, shopInventory, sellValue, gearScore, isZoneUnlocked } from './game/engine.js';
import { getFight, addItem } from './game/fights.js';
import { PROFESSIONS, getProf, profXpToNext } from './game/professions.js';
import { WORKER_COMMANDS } from './game/gather.js';
import { describeItem } from './game/itemInfo.js';
import { listRecipes, recipeName, matName } from './game/recipes.js';
import { countMat, hasMats } from './game/invutil.js';
import { enchantList, enchantCap, nextCost, REAGENT_NAME } from './game/enchant.js';
import { boxPrice, getBoxes } from './game/lootbox.js';
import { runForChat } from './game/rpg.js';

const GEAR_SLOTS = new Set(['weapon', 'head', 'body', 'shield', 'feet', 'accessory']);
const EQUIP_SLOTS = ['weapon', 'head', 'body', 'shield', 'feet', 'accessory'];
const PROF_KEYS = Object.keys(PROFESSIONS);
const MAX_STAMINA = 20;

// ── Web-play tokens ─────────────────────────────────────────────────────────
// A per-player secret that lets the browser act AS that hero. Players fetch their
// own link with `tt web` in chat; anyone holding the link can play that character,
// so it's treated like a password (never shown publicly).
const TOKEN_FILE = path.join(config.dataDir, 'webtokens.json');
let tokens = {}; // token -> discordId
try { tokens = JSON.parse(fs.readFileSync(TOKEN_FILE, 'utf8')) || {}; } catch { tokens = {}; }

function saveTokens() {
  try {
    fs.mkdirSync(config.dataDir, { recursive: true });
    fs.writeFileSync(TOKEN_FILE, JSON.stringify(tokens, null, 2));
  } catch (e) { log.error('Failed to persist web tokens:', e.message); }
}

function tokenFor(discordId) {
  for (const [t, id] of Object.entries(tokens)) if (id === discordId) return t;
  const t = crypto.randomBytes(24).toString('base64url');
  tokens[t] = discordId;
  saveTokens();
  return t;
}
function resolveToken(t) {
  if (!t) return null;
  if (tokens[t]) return tokens[t];
  // Miss: a token may have been minted by another process/instance — reload once.
  try {
    const disk = JSON.parse(fs.readFileSync(TOKEN_FILE, 'utf8')) || {};
    if (disk[t]) { tokens = disk; return tokens[t]; }
  } catch { /* no file yet */ }
  return null;
}

/** A player's personal, write-capable play link (or null if no public URL is set). */
export const playUrl = (discordId) => {
  if (!config.publicUrl || !discordId) return null;
  return `${config.publicUrl.replace(/\/+$/, '')}/play?t=${tokenFor(discordId)}`;
};

// A tile emoji for an inventory item, by kind.
const WEAPON_EMOJI = { sword: '🗡️', greatsword: '⚔️', axe: '🪓', spear: '🔱', dagger: '🔪', bow: '🏹', knuckles: '🥊', staff: '🪄', rod: '🔮', mace: '🔨' };
const ARMOR_EMOJI = { head: '🪖', body: '🧥', shield: '🛡️', feet: '🥾', accessory: '💍' };
function itemEmoji(it) {
  if (it.slot === 'material') return '📦';
  if (it.effect) {
    const e = String(it.effect);
    if (e.startsWith('cure:')) return '💊';
    if (e.startsWith('damage:')) return '💣';
    if (e === 'mp_pct') return '🔵';
    if (e === 'revive') return '🪽';
    if (e === 'flee_guaranteed') return '💨';
    if (e === 'stamina') return '⚡';
    return '🧪';
  }
  if (it.slot === 'weapon') return WEAPON_EMOJI[it.weapon_type] || '🗡️';
  return ARMOR_EMOJI[it.slot] || '❔';
}

// Structured, clickable recipe list for a profession (crafter/alchemist).
function recipesFor(c, prof) {
  const lvl = getProf(c, prof).level;
  return listRecipes(prof).map((r, i) => {
    const locked = lvl < r.level;
    const inputs = Object.entries(r.inputs).map(([b, q]) => {
      const have = countMat(c, b);
      return { name: matName(b), have, need: q, ok: have >= q };
    });
    const out = ITEMS[r.output] || {};
    return {
      n: i + 1, name: recipeName(r), emoji: itemEmoji(out), slot: out.slot || '',
      level: r.level, locked, ready: !locked && hasMats(c, r.inputs), inputs, xp: r.xp,
    };
  });
}

// Profession level + XP-to-next progress, for the craft tab headers/pills.
function profProg(c, key) {
  const p = getProf(c, key);
  return { level: p.level, xp: p.xp || 0, next: profXpToNext(p.level), name: PROFESSIONS[key].name, emoji: PROFESSIONS[key].emoji };
}

// ── State snapshot the browser renders from ─────────────────────────────────
function buildState(discordId) {
  const c = getPlayer(discordId);
  if (!c) {
    return {
      hasChar: false,
      classes: CLASS_LIST.map((x) => ({ id: x.id, name: x.name, blurb: x.blurb || '' })),
      races: RACE_LIST.map((x) => ({ id: x.id, name: x.name, blurb: x.blurb || '' })),
    };
  }
  const pd = derive(c);
  const cls = CLASSES[c.cls], race = RACES[c.race];
  const inv = c.inventory || [];
  const gear = inv.filter((i) => GEAR_SLOTS.has(i.slot));
  const fight = getFight(discordId);
  const shop = shopInventory(c);

  const skills = skillsForClass(c.cls, c.level).map((s, i) => ({
    n: i + 1, name: s.name, mp: s.mp, type: s.type,
    affordable: (fight ? fight.pmp : (c.mp ?? pd.maxmp)) >= s.mp,
  }));

  return {
    hasChar: true,
    name: c.name, cls: c.cls, className: cls?.name || c.cls, raceName: race?.name || c.race,
    level: c.level || 1, xp: c.xp || 0, xpNext: xpToNext(c.level || 1),
    gold: c.gold || 0, ascension: c.ascension || 0,
    hp: c.hp ?? pd.maxhp, maxhp: pd.maxhp, mp: c.mp ?? pd.maxmp, maxmp: pd.maxmp,
    stamina: c.stamina ?? MAX_STAMINA, maxStamina: MAX_STAMINA,
    power: Math.round(pd.st[pd.scales] * 1.3 + pd.wpow * 1.4), def: pd.def, res: pd.res,
    stats: STAT_KEYS.map((k) => ({ k: k.toUpperCase(), v: pd.st[k] })),
    skills,
    inFight: !!fight,
    fight: fight ? {
      monster: fight.monster?.name || 'Monster', emoji: fight.monster?.emoji || '👹',
      mhp: Math.max(0, fight.mhp), mmaxhp: fight.mmaxhp,
      php: Math.max(0, fight.php), pmaxhp: pd.maxhp, pmp: fight.pmp, pmaxmp: pd.maxmp,
      turn: fight.turn, log: (fight.log || []).slice(-60),
    } : null,
    equipped: EQUIP_SLOTS.map((s) => ({ slot: s, name: c.equipped?.[s]?.name || null, rarity: c.equipped?.[s]?.rarity || '' })),
    // Unified bag: every non-equipped item, indexed by its position in char.inventory
    // (ii) so the item-action endpoint can act on exactly the one clicked.
    items: inv.map((i, ii) => ({
      ii, name: i.name, slot: i.slot, rarity: i.rarity || '', qty: i.qty || 1,
      gear: GEAR_SLOTS.has(i.slot), potion: i.effect === 'heal_pct', emoji: itemEmoji(i),
      sell: sellValue(i) * (i.qty || 1), desc: describeItem(i, c),
    })),
    shop: shop.map((i, idx) => ({
      n: idx + 1, name: i.name, price: i.price ?? i.value ?? 0, slot: i.slot, rarity: i.rarity || '',
      affordable: (c.gold || 0) >= (i.price ?? i.value ?? 0), desc: describeItem(i, c),
    })),
    zones: ZONE_LIST.map((z) => ({ name: z.name, unlocked: isZoneUnlocked(c, z), cleared: !!c.cleared?.[z.id] })),
    // Remaining gather cooldown per worker action (ms, 0 = ready) so the browser can
    // show a live countdown over each button.
    gatherCd: (() => {
      const now = Date.now(), cd = c.gatherCd || {};
      const o = {};
      for (const k of WORKER_COMMANDS) o[k] = Math.max(0, (cd[k] || 0) - now);
      return o;
    })(),
    professions: PROF_KEYS.map((k) => ({ name: PROFESSIONS[k].name, emoji: PROFESSIONS[k].emoji, level: (c.professions?.[k]?.level) || 1 }))
      .filter((p) => p.level > 1 || p.name === 'Worker'),
    recipes: { crafter: recipesFor(c, 'crafter'), alchemist: recipesFor(c, 'alchemist') },
    craftProg: { crafter: profProg(c, 'crafter'), alchemist: profProg(c, 'alchemist'), enchanter: profProg(c, 'enchanter') },
    lootbox: { boxes: getBoxes(c), price: boxPrice(c) },
    skillbook: {
      className: cls?.name || c.cls,
      unlocked: skillsForClass(c.cls, c.level).map((s) => ({ name: s.name, mp: s.mp, type: s.type, power: s.power || 0 })),
      locked: skillsForClass(c.cls, 99).filter((s) => s.unlock_level > (c.level || 1))
        .map((s) => ({ name: s.name, level: s.unlock_level })),
    },
    quest: (() => {
      const q = ensureQuest(c), done = (q.progress || 0) >= q.target;
      return { desc: q.desc, progress: q.progress || 0, target: q.target, gold: q.gold, xp: q.xp, claimed: !!q.claimed, done, claimable: done && !q.claimed };
    })(),
    leaderboard: Object.entries(allPlayers()).filter(([, p]) => p && p.name)
      .sort(([, a], [, b]) => ((b.ascension || 0) - (a.ascension || 0)) || (b.level - a.level) || ((b.xp || 0) - (a.xp || 0)))
      .slice(0, 10)
      .map(([id, p], i) => ({ rank: i + 1, name: p.name, level: p.level || 1, cls: p.cls, className: CLASSES[p.cls]?.name || p.cls, ascension: p.ascension || 0, me: id === discordId })),
    enchants: (() => {
      const cap = enchantCap(c), quartz = countMat(c, 'quartz');
      return {
        level: getProf(c, 'enchanter').level, cap, reagent: REAGENT_NAME, quartz,
        list: enchantList(c).map((e, i) => {
          const cur = e.item.enchant || 0, cost = nextCost(e.item), maxed = cur >= cap;
          return {
            n: i + 1, name: e.item.name, rarity: e.item.rarity || '', emoji: itemEmoji(e.item),
            where: e.where, cur, maxed, cost,
            affordable: !maxed && (c.gold || 0) >= cost.gold && quartz >= cost.quartz,
          };
        }),
      };
    })(),
  };
}

/** Playable browser client at /play. Drives the SAME engine as chat via runForChat. */
export function mountPlay(app, client) {
  app.get('/play', (_req, res) => res.type('html').send(PLAY_HTML));

  // Every /play/api call carries the player's token (query ?t= or x-play-token header).
  const gate = (req, res, next) => {
    const t = req.query.t || req.get('x-play-token') || '';
    const id = resolveToken(t);
    if (!id) return res.status(401).json({ error: 'Bad or missing play link. Type `tt web` in chat to get yours.' });
    req.discordId = id;
    next();
  };

  app.get('/play/api/state', gate, (req, res) => res.json(buildState(req.discordId)));

  app.post('/play/api/cmd', gate, express_json_guard, async (req, res) => {
    const command = String(req.body?.command || '').trim().slice(0, 200);
    if (!command) return res.status(400).json({ error: 'no command' });
    const c = getPlayer(req.discordId);
    let reply = null;
    try {
      reply = await runForChat({
        discordId: req.discordId,
        username: c?.name || 'Adventurer',
        content: 'tt ' + command,
        guildId: undefined,
        client,
      });
    } catch (e) {
      log.error('play cmd failed:', e);
      return res.status(500).json({ error: 'Something went wrong running that.' });
    }
    res.json({ reply: reply || '…', state: buildState(req.discordId) });
  });

  // Per-item bag actions (equip / sell / toss) by inventory index — used by the
  // grid UI. Goes through the store like everything else so chat + web stay in sync.
  app.post('/play/api/item', gate, express_json_guard, (req, res) => {
    const idx = Number(req.body?.idx);
    const action = String(req.body?.action || '');
    const c = getPlayer(req.discordId);
    if (!c) return res.status(400).json({ error: 'No hero.' });
    const inv = c.inventory || [];
    const item = inv[idx];
    if (!item) return res.json({ reply: 'That item is no longer in your bag.', state: buildState(req.discordId) });

    let reply;
    if (action === 'equip') {
      if (!GEAR_SLOTS.has(item.slot)) { reply = `You can’t equip a ${item.name}.`; }
      else {
        c.equipped = c.equipped || {};
        const old = c.equipped[item.slot];
        c.equipped[item.slot] = item;
        c.inventory = inv.filter((x) => x !== item);
        if (old) addItem(c, old);
        reply = `✅ Equipped ${item.name}${old ? ` (was ${old.name})` : ''}.`;
      }
    } else if (action === 'sell') {
      const base = sellValue(item) * (item.qty || 1);
      const { gold, leveled } = merchantSale(c, base);
      c.gold = (c.gold || 0) + gold;
      c.inventory = inv.filter((x) => x !== item);
      reply = `💰 Sold ${item.qty > 1 ? item.qty + '× ' : ''}${item.name} for ${gold} 🪙.${leveled ? ' 💰 Merchant level up!' : ''}`;
    } else if (action === 'toss') {
      c.inventory = inv.filter((x) => x !== item);
      reply = `🗑️ Tossed ${item.qty > 1 ? item.qty + '× ' : ''}${item.name}.`;
    } else {
      return res.status(400).json({ error: 'bad action' });
    }
    savePlayer(req.discordId, c);
    res.json({ reply, state: buildState(req.discordId) });
  });
}

// express.json is already mounted app-wide in ingest.js, so req.body is parsed —
// this just guards against a missing body object.
function express_json_guard(req, _res, next) { req.body = req.body || {}; next(); }

const PLAY_HTML = `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Play</title>
<style>
  :root{--accent:#7cc44a;--ink:#e8e0c4;--dim:#93a888;--panel:#0c1610;--line:#23331f;--gold:#f0c84a;}
  *{box-sizing:border-box}
  body{margin:0;min-height:100vh;color:var(--ink);font-family:'Segoe UI',system-ui,Arial,sans-serif;
       background:radial-gradient(1200px 700px at 50% -10%, #16241a 0%, #0a120d 55%, #070d09 100%)}
  .wrap{max-width:1080px;margin:0 auto;padding:16px 18px 70px}
  h1{color:var(--accent);text-align:center;font-size:20px;margin:4px 0 14px}
  .grid2{display:grid;grid-template-columns:1fr;gap:14px;align-items:start}
  @media(min-width:720px){.grid2{grid-template-columns:1.05fr .95fr}}
  .card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:14px}
  .card.hero{border-color:var(--accent);box-shadow:inset 0 0 60px -30px var(--accent)}
  .row{display:flex;align-items:center;gap:10px}
  .emoji{font-size:38px;line-height:1}
  .name{font-size:20px;font-weight:800;color:#fff}
  .sub{color:var(--accent);font-weight:700;font-size:13px}
  .stars{color:var(--gold)}
  .barlab{display:flex;justify-content:space-between;font-size:11px;color:var(--dim);margin:9px 0 3px;text-transform:uppercase;letter-spacing:.05em}
  .bar{height:12px;border-radius:8px;background:#0a140e;border:1px solid var(--line);overflow:hidden}
  .fill{height:100%;border-radius:8px;transition:width .25s}
  .fill.hp{background:#d46a6a}.fill.mp{background:#5a9ad4}.fill.xp{background:var(--accent)}.fill.stam{background:#c8a24a}
  .fill.foe{background:#d46a6a}.fill.you{background:#5fc27e}
  /* Combat: clearly separated Enemy vs You */
  .fighter{border:1px solid var(--line);border-radius:10px;padding:9px 11px}
  .fighter.foe-side{border-color:rgba(212,106,106,.45);background:rgba(212,106,106,.06)}
  .fighter.you-side{border-color:rgba(95,194,126,.45);background:rgba(95,194,126,.06)}
  .frow{display:flex;justify-content:space-between;align-items:center;margin-bottom:2px}
  .fname{font-weight:800;color:#fff;font-size:14px}
  .youtag{font-size:9px;font-weight:800;color:#0a140e;background:#5fc27e;border-radius:10px;padding:1px 7px;margin-left:4px;vertical-align:middle}
  .vsdiv{text-align:center;color:var(--dim);font-size:11px;font-weight:800;letter-spacing:.08em;margin:6px 0}
  .grid{display:grid;grid-template-columns:repeat(3,1fr);gap:7px;margin-top:12px}
  .stat{background:#0a140e;border:1px solid var(--line);border-radius:9px;padding:7px;text-align:center;cursor:help}
  .stat:hover{border-color:var(--accent)}
  .stat b{display:block;font-size:16px;color:#fff}.stat span{font-size:9px;color:var(--dim);text-transform:uppercase}
  h3{font-size:11px;color:var(--accent);text-transform:uppercase;letter-spacing:.06em;margin:16px 0 7px}
  .btns{display:flex;flex-wrap:wrap;gap:7px}
  button{background:#13251a;color:var(--ink);border:1px solid #2c5233;border-radius:9px;
         padding:9px 12px;font-size:13px;font-weight:600;cursor:pointer;transition:.12s}
  button:hover:not(:disabled){background:#1b3524;border-color:var(--accent);transform:translateY(-1px)}
  button:disabled{opacity:.4;cursor:not-allowed}
  button.p{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  button.atk{background:#3a1c1c;border-color:#7a3a3a}button.atk:hover:not(:disabled){background:#4d2424}
  button.sm{padding:6px 9px;font-size:12px}
  button.on{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  /* Gather button with a cooldown overlay */
  button.gather{position:relative;overflow:hidden}
  button.gather.cooling{color:var(--dim)}
  .cd{position:absolute;inset:0;display:none;align-items:center;justify-content:center;
      background:rgba(8,15,10,.78);color:#f0c84a;font-weight:800;font-size:13px;letter-spacing:.03em}
  button.gather.cooling .cd{display:flex}
  .price{color:var(--gold);font-weight:700}
  .rar-uncommon{color:#6fd06f}.rar-rare{color:#5a9ad4}.rar-epic{color:#b06ad0}.rar-legendary{color:var(--gold)}
  .eqrow{display:flex;justify-content:space-between;font-size:13px;padding:3px 0;border-bottom:1px solid rgba(35,51,31,.5)}
  .eqrow .slot{color:var(--dim);text-transform:capitalize}.eqrow .empty{color:#5f7355}
  .listrow{display:flex;justify-content:space-between;align-items:center;gap:8px;padding:5px 0;font-size:13px}
  .item{padding:8px 0;border-bottom:1px solid rgba(35,51,31,.5)}
  .item:hover{background:rgba(124,196,74,.04)}
  .idet{margin-top:2px;font-size:11.5px;line-height:1.5}
  .istat{color:#b8c9ad}
  .iflav{color:#7f9673;font-style:italic;margin-top:2px}
  .cmp{display:inline-block;margin-left:4px;padding:1px 7px;border-radius:20px;font-size:11px;font-weight:700}
  .cmp.up{background:rgba(111,208,111,.15);color:#7fd07f}
  .cmp.down{background:rgba(212,106,106,.15);color:#e08a8a}
  .cmp.same{background:rgba(147,168,136,.15);color:var(--dim)}
  .cmp.new{background:rgba(240,200,74,.15);color:var(--gold)}
  .chips{display:flex;flex-wrap:wrap;gap:6px}
  .chip{background:#0a140e;border:1px solid var(--line);border-radius:20px;padding:3px 10px;font-size:12px}
  .chip .pill{color:var(--accent);font-weight:700}
  .chip.zc-cleared{color:#7fd07f;border-color:#2f4a2f;background:rgba(111,208,111,.07)}
  .chip.zc-open{color:var(--ink);border-color:var(--accent);background:rgba(124,196,74,.12);font-weight:700}
  .chip.zc-locked{opacity:.45}
  .ztag{font-size:9px;font-weight:800;color:#0a140e;background:var(--accent);border-radius:10px;padding:1px 6px;margin-left:4px;vertical-align:middle}
  .log{background:#080f0a;border:1px solid var(--line);border-radius:10px;padding:10px 12px;font-size:13px;
       min-height:44px;white-space:pre-wrap;line-height:1.5}
  .clog{max-height:180px;overflow-y:auto;background:#080f0a;border:1px solid var(--line);border-radius:10px;
        padding:8px 11px;margin:10px 0 2px;font-size:12.5px;line-height:1.5}
  .clog .cl{padding:2px 0;border-bottom:1px solid rgba(35,51,31,.35)}
  .clog .cl:last-child{border-bottom:none}
  .clog b{color:#fff}
  .foe{display:flex;justify-content:space-between;align-items:center}
  .foe .fn{font-weight:800;color:#fff}
  .tabs{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:10px}
  .tabs button.active{background:var(--accent);color:#0a140e;border-color:var(--accent)}
  .lvpill{font-size:10px;font-weight:800;padding:1px 6px;border-radius:20px;background:rgba(0,0,0,.28);
          border:1px solid rgba(255,255,255,.14);vertical-align:middle;margin-left:2px}
  .tabs button.active .lvpill{background:rgba(0,0,0,.18)}
  .hide{display:none}
  .muted{color:var(--dim);font-size:12px}
  /* Bag grid */
  .bagGrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(88px,1fr));gap:8px;
           max-height:52vh;overflow-y:auto;padding:4px 2px}
  .tile{position:relative;background:#0a140e;border:1px solid var(--line);border-radius:11px;
        padding:9px 6px 8px;text-align:center;cursor:pointer;transition:.12s;min-height:78px}
  .tile:hover{border-color:var(--accent);transform:translateY(-1px);background:#12241a}
  .tile.sel{border-color:var(--accent);background:#15291b;box-shadow:0 0 0 1px var(--accent)}
  .tile.rar-uncommon{border-color:#3f6b3f}.tile.rar-rare{border-color:#3a5f80}
  .tile.rar-epic{border-color:#6a4a80}.tile.rar-legendary{border-color:#8a7326}
  .tile .tie{font-size:26px;line-height:1}
  .tile .tin{font-size:10.5px;color:var(--ink);margin-top:5px;line-height:1.25;
             display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}
  .tile .tq{position:absolute;top:4px;right:6px;font-size:10px;color:var(--dim);font-weight:700}
  .tile .tc{position:absolute;top:3px;left:6px;font-size:11px;font-weight:800}
  .tile .tc.up{color:#7fd07f}.tile .tc.down{color:#e08a8a}.tile .tc.same{color:var(--dim)}.tile .tc.new{color:var(--gold)}
  .bagsel{background:#0a140e;border:1px solid var(--accent);border-radius:11px;padding:10px 12px;margin-bottom:10px}
  .bstop{display:flex;justify-content:space-between;align-items:center;font-weight:700}
  .bstop .x{background:none;border:none;color:var(--dim);font-size:14px;padding:2px 6px;cursor:pointer}
  button.danger{background:#3a1c1c;border-color:#7a3a3a}
  button.danger:hover:not(:disabled){background:#4d2424}
  .bagpop{position:fixed;z-index:50;display:none;background:#0c1610;border:1px solid var(--accent);
          border-radius:10px;padding:9px 11px;box-shadow:0 8px 30px rgba(0,0,0,.6);pointer-events:none;font-size:12px}
  .bagpop .pn{font-weight:800;color:#fff}
  .bagpop .ps{color:#b8c9ad;margin-top:3px}
  .bagpop .pf{color:#7f9673;font-style:italic;margin-top:4px}
  /* Recipe / enchant list */
  .recipeList{display:flex;flex-direction:column;gap:7px;max-height:52vh;overflow-y:auto;padding:2px}
  .rrow{background:#0a140e;border:1px solid var(--line);border-radius:10px;padding:9px 11px;transition:.12s}
  .rrow.ready{cursor:pointer;border-color:#2c5233}
  .rrow.ready:hover{background:#13251a;border-color:var(--accent);transform:translateY(-1px)}
  .rrow.locked{opacity:.6}
  .rtop{display:flex;justify-content:space-between;align-items:center;gap:8px}
  .rname{font-weight:700;color:#fff;font-size:13.5px}
  .rstat{font-size:11px;font-weight:800;white-space:nowrap}
  .rstat.g{color:var(--accent)}.rstat.r{color:#e08a8a}.rstat.l{color:var(--dim)}
  .rings{display:flex;flex-wrap:wrap;gap:5px;margin-top:6px}
  .ing{font-size:11px;padding:2px 8px;border-radius:20px;border:1px solid var(--line)}
  .ing.ok{color:#7fd07f;border-color:#2f4a2f;background:rgba(111,208,111,.08)}
  .ing.no{color:#e08a8a;border-color:#4a2f2f;background:rgba(212,106,106,.08)}
  .rxp{margin-left:auto;font-size:11px;font-weight:700;color:var(--gold);align-self:center}
  .center{text-align:center}
  .cls-pick{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:8px}
  .pk{background:#0a140e;border:1px solid var(--line);border-radius:10px;padding:10px;cursor:pointer;text-align:left}
  .pk:hover{border-color:var(--accent)}.pk.sel{border-color:var(--accent);background:#13251a}
  .pk b{color:#fff}.pk small{color:var(--dim);display:block;margin-top:3px;font-size:11px}
  .spin{color:var(--dim)}
  a{color:var(--accent)}
</style></head><body><div class="wrap">
  <h1>🍺 Tavern Tales</h1>
  <div id="root"><div class="center spin">Loading your hero…</div></div>
</div>
<script>
var THEME={
  knight:{c:"#6fb0e0",e:"🛡️"},berserker:{c:"#e06a5a",e:"🪓"},paladin:{c:"#f0cf5a",e:"⚜️"},
  ranger:{c:"#7cc44a",e:"🏹"},rogue:{c:"#a884e0",e:"🗡️"},mage:{c:"#6a9ff0",e:"🔮"},
  cleric:{c:"#f0e6a6",e:"✨"},necromancer:{c:"#9a6ad0",e:"💀"},monk:{c:"#e0a45a",e:"👊"},bard:{c:"#e884b8",e:"🎵"}
};
var TOKEN=new URLSearchParams(location.search).get("t")||"";
var STAT_TIP={
  POWER:"Power — your overall attack strength, from your main stat plus your weapon.",
  DEF:"Defense — reduces the physical damage you take.",
  RES:"Resistance — reduces the magical damage you take.",
  STR:"Strength — raises physical attack power (melee weapons).",
  MAG:"Magic — raises spell power (staves, rods and skills).",
  VIT:"Vitality — raises your max HP and adds to Defense.",
  SPR:"Spirit — raises your max MP and adds to Resistance.",
  AGI:"Agility — improves hit chance, dodging, and fleeing.",
  LCK:"Luck — improves critical-hit chance and loot quality."
};
var S=null, TAB="adventure", NEWCLS=null, NEWRACE=null, BUSY=false, LASTMSG="", BAG_SEL=null, CRAFT_TAB="crafter", CRAFT_LEVEL=null, MORE_VIEW=null;
function esc(s){return String(s==null?"":s).replace(/[&<>]/g,function(c){return{"&":"&amp;","<":"&lt;",">":"&gt;"}[c];});}
function rar(r){return r?'rar-'+String(r).toLowerCase():'';}
function pct(a,b){return Math.max(0,Math.min(100,Math.round(100*(a||0)/Math.max(1,b))));}

async function api(path,opts){
  opts=opts||{}; opts.headers=Object.assign({'x-play-token':TOKEN},opts.headers||{});
  var r=await fetch(path,opts); var d=await r.json().catch(function(){return {};});
  if(!r.ok) throw new Error(d.error||("HTTP "+r.status));
  return d;
}
async function refresh(){ try{ S=await api("/play/api/state"); syncCooldowns(); startCooldownTicker(); render(); }catch(e){ fail(e.message); } }
function fail(m){ document.getElementById("root").innerHTML='<div class="card center">⚠️ '+esc(m)+'</div>'; }

// Send a game command (as if the player typed "tt <cmd>"), then re-render from fresh state.
async function cmd(c){
  if(BUSY) return; BUSY=true;
  var wasFight=!!(S&&S.inFight);
  try{ var d=await api("/play/api/cmd",{method:"POST",headers:{'Content-Type':'application/json'},body:JSON.stringify({command:c})});
       LASTMSG=d.reply||""; S=d.state; syncCooldowns(); }
  catch(e){ LASTMSG="⚠️ "+e.message; }
  BUSY=false;
  // Staying in the SAME fight → patch values in place (no flicker). Any structural
  // change (fight starts/ends, tab switch) → one full render.
  if(wasFight && S && S.inFight && document.getElementById('combatPanel')) patchCombat();
  else render();
  maybeAuto();
}

// Update only the numbers/bars/log that changed this turn, leaving the DOM intact
// so combat doesn't flash on every auto-swing.
function patchCombat(){
  var f=S.fight; if(!f){ render(); return; }
  setW('ehp-fill',f.mhp,f.mmaxhp); setT('ehp-lab',f.mhp+' / '+f.mmaxhp);
  setW('php-fill',f.php,f.pmaxhp); setT('php-lab',f.php+' / '+f.pmaxhp);
  setW('pmp-fill',f.pmp,f.pmaxmp); setT('pmp-lab',f.pmp+' / '+f.pmaxmp);
  setW('hp-fill',S.hp,S.maxhp);     setT('hp-lab',S.hp+' / '+S.maxhp);
  setW('mp-fill',S.mp,S.maxmp);     setT('mp-lab',S.mp+' / '+S.maxmp);
  setW('xp-fill',S.xp,S.xpNext);    setT('xp-lab',S.xp+' / '+S.xpNext);
  setW('stam-fill',S.stamina,S.maxStamina); setT('stam-lab',S.stamina+' / '+S.maxStamina);
  setT('gold-lab','🪙 '+S.gold+' gold');
  setT('turnlab','Turn '+f.turn);
  setT('autohint',autoHintText());
  var cl=document.getElementById('clog'); if(cl){ cl.innerHTML=clogHTML(f.log); cl.scrollTop=cl.scrollHeight; }
  S.skills.forEach(function(s){ var b=document.getElementById('sk-'+s.n); if(b) b.disabled=!s.affordable; });
  var pot=document.getElementById('potbtn'); if(pot) pot.disabled=!S.items.some(function(b){return b.potion&&b.qty>0;});
}

// ── Auto-attack: always ON, but it waits for the player to throw the FIRST punch
// (click Attack) before it takes over and keeps swinging every ~0.85s until the
// fight ends. This replaces Discord's message-editing auto-battle, which can't run
// in the browser, and still lets you open with a skill/potion if you prefer. ─────
var AUTO=true, autoArmed=false, AUTO_TIMER=null;
function toggleAuto(){ AUTO=!AUTO; if(!AUTO){ autoArmed=false; if(AUTO_TIMER){clearTimeout(AUTO_TIMER);AUTO_TIMER=null;} } render(); maybeAuto(); }
function maybeAuto(){
  if(AUTO_TIMER){ clearTimeout(AUTO_TIMER); AUTO_TIMER=null; }
  if(S && !S.inFight){ autoArmed=false; return; }   // fight over → disarm for next time
  if(AUTO && autoArmed && S && S.inFight && !BUSY){
    AUTO_TIMER=setTimeout(function(){ AUTO_TIMER=null; cmd('attack'); }, 850);
  }
}

function theme(){ var t=THEME[S&&S.cls]||{c:"#7cc44a",e:"🎮"}; document.documentElement.style.setProperty("--accent",t.c); return t; }

function render(){
  if(!S){return;}
  if(!S.hasChar){ return renderCreate(); }
  var t=theme();
  var root=document.getElementById("root");
  var stars=S.ascension?'<span class="stars"> '+"★".repeat(Math.min(5,S.ascension))+(S.ascension>5?"+":"")+'</span>':'';
  var statCells=S.stats.map(function(s){return '<div class="stat"'+tip(STAT_TIP[s.k]||'')+'><b>'+s.v+'</b><span>'+esc(s.k)+'</span></div>';}).join("");
  var hero=
   '<div class="card hero">'+
    '<div class="row"><div class="emoji">'+t.e+'</div><div>'+
      '<div class="name">'+esc(S.name)+'</div>'+
      '<div class="sub">Lv '+S.level+' '+esc(S.raceName)+' '+esc(S.className)+stars+'</div>'+
    '</div></div>'+
    bar("HP",S.hp,S.maxhp,"hp","hp")+bar("MP",S.mp,S.maxmp,"mp","mp")+
    bar("XP",S.xp,S.xpNext,"xp","xp")+bar("Stamina",S.stamina,S.maxStamina,"stam","stam")+
    '<div class="grid">'+
      '<div class="stat"'+tip(STAT_TIP.POWER)+'><b>'+S.power+'</b><span>Power</span></div>'+
      '<div class="stat"'+tip(STAT_TIP.DEF)+'><b>'+S.def+'</b><span>Def</span></div>'+
      '<div class="stat"'+tip(STAT_TIP.RES)+'><b>'+S.res+'</b><span>Res</span></div>'+
    '</div><div class="grid">'+statCells+'</div>'+
    '<div class="barlab" style="margin-top:12px"><span id="gold-lab">🪙 '+S.gold+' gold</span><span></span></div>'+
   '</div>';

  var right = S.inFight ? panelFight() : panelTabs();
  // During a fight the combat panel has its own scrolling log, so drop the top log box.
  root.innerHTML='<div class="grid2">'+hero+'<div>'+(S.inFight?'':logBox())+right+'</div></div>';
  if(S.inFight) scrollClog();
}

function bar(label,a,b,cls,id){
  var lab=id?' id="'+id+'-lab"':'', fill=id?' id="'+id+'-fill"':'';
  return '<div class="barlab"><span>'+label+'</span><span'+lab+'>'+a+' / '+b+'</span></div>'+
         '<div class="bar"><div class="fill '+cls+'"'+fill+' style="width:'+pct(a,b)+'%"></div></div>';
}
function setW(id,a,b){ var e=document.getElementById(id); if(e) e.style.width=pct(a,b)+'%'; }
function setT(id,t){ var e=document.getElementById(id); if(e) e.textContent=t; }
function logBox(){
  return '<div class="card" style="margin-bottom:14px"><div class="log" id="logtext">'+(esc(LASTMSG)||'<span class="muted">Pick an action to begin your adventure.</span>')+'</div></div>';
}
function autoHintText(){ return AUTO?(autoArmed&&S.inFight?'🤖 Auto-battling — tap Skill/Potion/Flee any time to interject.':'🤖 Auto-battle is ON — click 🗡️ Attack to begin.'):'Auto-battle is off — one action per click.'; }
// Render one combat-log line: escape, then turn **bold** into <b>.
function fmtLine(l){ return esc(l).replace(/\\*\\*(.+?)\\*\\*/g,'<b>$1</b>'); }
function clogHTML(lines){ return (lines||[]).map(function(l){return '<div class="cl">'+fmtLine(l)+'</div>';}).join(''); }
function scrollClog(){ var c=document.getElementById('clog'); if(c) c.scrollTop=c.scrollHeight; }

// ── Combat ──────────────────────────────────────────────────────────────────
function panelFight(){
  var f=S.fight||{};
  var skills=S.skills.map(function(s){
    return '<button id="sk-'+s.n+'" class="sm"'+tip("Cast "+s.name+" — costs "+s.mp+" MP ("+s.type+").")+' '+(BUSY||!s.affordable?'disabled':'')+' onclick="cmd(\\'skill '+s.n+'\\')">'+esc(s.name)+' <span class="muted">'+s.mp+'mp</span></button>';
  }).join("");
  var hasPot=S.items.some(function(b){return b.potion&&b.qty>0;});
  var pt=THEME[S.cls]||{e:'🙂'};
  return '<div class="card" id="combatPanel">'+
    // Enemy side
    '<div class="fighter foe-side">'+
      '<div class="frow"><span class="fname">'+(f.emoji||'👹')+' '+esc(f.monster)+'</span><span class="muted" id="turnlab">Turn '+f.turn+'</span></div>'+
      bar("Enemy HP",f.mhp,f.mmaxhp,"foe","ehp")+
    '</div>'+
    '<div class="vsdiv">⚔️ VS</div>'+
    // Your side
    '<div class="fighter you-side">'+
      '<div class="frow"><span class="fname">'+pt.e+' '+esc(S.name)+' <span class="youtag">YOU</span></span></div>'+
      bar("Your HP",f.php,f.pmaxhp,"you","php")+
      bar("Your MP",f.pmp,f.pmaxmp,"mp","pmp")+
    '</div>'+
    '<div class="clog" id="clog">'+clogHTML(f.log)+'</div>'+
    '<h3>Actions</h3><div class="btns">'+
      '<button class="atk" '+(BUSY?'disabled':'')+' title="Swing your weapon for one turn. Once you attack, auto-battle takes over and keeps swinging." onclick="cmd(\\'attack\\')">🗡️ Attack</button>'+
      '<button class="sm '+(AUTO?'on':'')+'" title="Auto-battle repeats your attack until the fight ends. It waits for your first Attack before starting. Click to turn '+(AUTO?'off':'on')+'." onclick="toggleAuto()">'+(AUTO?'🤖 Auto: ON':'🤖 Auto: off')+'</button>'+
      '<button id="potbtn" class="sm" '+(BUSY||!hasPot?'disabled':'')+' title="Quaff a healing potion from your bag." onclick="cmd(\\'use\\')">🧪 Potion</button>'+
      '<button class="sm" '+(BUSY?'disabled':'')+' title="Try to escape the fight. Faster heroes flee more reliably." onclick="cmd(\\'flee\\')">🏃 Flee</button>'+
    '</div>'+
    '<div class="muted" id="autohint" style="margin-top:6px">'+autoHintText()+'</div>'+
    (skills?'<h3>Skills</h3><div class="btns">'+skills+'</div>':'')+
  '</div>';
}

// ── Out of combat: tabbed action panels ─────────────────────────────────────
function panelTabs(){
  var tabs=[["adventure","⚔️ Adventure","Fight, gather materials, and see zones."],["shop","🛒 Shop","Buy gear and potions."],["bag","🎒 Bag","Equip, sell and inspect your items."],["craft","🔨 Craft","Craft gear, brew potions, enchant."],["more","✨ More","Quests, lootboxes, leaderboard, ascend."]];
  var bar='<div class="tabs">'+tabs.map(function(x){
    return '<button class="'+(TAB===x[0]?'active':'')+'"'+tip(x[2])+' onclick="setTab(\\''+x[0]+'\\')">'+x[1]+'</button>';
  }).join("")+'</div>';
  return '<div class="card">'+bar+tabBody()+'</div>';
}
function setTab(t){ TAB=t; BAG_SEL=null; MORE_VIEW=null; bagOut(); render(); }

function tabBody(){
  if(TAB==="adventure") return bodyAdventure();
  if(TAB==="shop") return bodyShop();
  if(TAB==="bag") return bodyBag();
  if(TAB==="craft") return bodyCraft();
  return bodyMore();
}
function tip(t){ return t?' title="'+esc(t)+'"':''; }
function actBtn(label,c,cls,dis,title){ return '<button class="'+(cls||'')+'"'+tip(title)+' '+((BUSY||dis)?'disabled':'')+' onclick="cmd(\\''+c+'\\')">'+label+'</button>'; }

// Gather button: disabled + overlaid with a live countdown while on cooldown.
function gatherBtn(label,c,title){
  var rem=(S.gatherCd&&S.gatherCd[c])||0;
  var cooling=rem>0;
  return '<button id="gb-'+c+'" class="sm gather'+(cooling?' cooling':'')+'"'+tip(title)+' '+((BUSY||cooling)?'disabled':'')+
    ' onclick="cmd(\\''+c+'\\')">'+label+'<span class="cd" id="cd-'+c+'">'+(cooling?fmtCd(rem):'')+'</span></button>';
}

// ── Item descriptions: stats, a comparison vs what's equipped, and funny flavor ──
function cmpBadge(cmp){
  if(!cmp) return '';
  if(cmp.dir==='new') return '<span class="cmp new">✨ new slot</span>';
  if(cmp.dir==='up') return '<span class="cmp up">▲ +'+cmp.delta+' vs your '+esc(cmp.text)+'</span>';
  if(cmp.dir==='down') return '<span class="cmp down">▼ '+cmp.delta+' vs your '+esc(cmp.text)+'</span>';
  return '<span class="cmp same">= same as your '+esc(cmp.text)+'</span>';
}
function itemDetail(d){
  if(!d) return '';
  return '<div class="idet">'+(d.stats?'<span class="istat">'+esc(d.stats)+'</span>':'')+cmpBadge(d.compare)+
    (d.flavor?'<div class="iflav">\\u201c'+esc(d.flavor)+'\\u201d</div>':'')+'</div>';
}
function itemTip(name,d){
  var t=name;
  if(d){
    if(d.stats) t+='\\n'+d.stats;
    if(d.compare){ var c=d.compare; t+='\\n'+(c.dir==='new'?'New slot — nothing equipped':(c.dir==='up'?'+'+c.delta+' vs your '+c.text:(c.dir==='down'?c.delta+' vs your '+c.text:'Same as your '+c.text))); }
    if(d.flavor) t+='\\n\\u201c'+d.flavor+'\\u201d';
  }
  return t;
}
function fmtCd(ms){ var s=Math.ceil(ms/1000); return Math.floor(s/60)+':'+('0'+(s%60)).slice(-2); }

// Turn the per-action remaining-ms from state into absolute local target times, so
// the countdown keeps ticking between server fetches without clock-skew issues.
var GCD_TARGET={};
function syncCooldowns(){
  if(!S||!S.gatherCd) return;
  var now=Date.now();
  for(var k in S.gatherCd){ var rem=S.gatherCd[k]; if(rem>0) GCD_TARGET[k]=now+rem; else delete GCD_TARGET[k]; }
}
// One shared 1s ticker updates the gather overlays in place (no full re-render).
var COOLDOWN_TICKER=null;
function startCooldownTicker(){
  if(COOLDOWN_TICKER) return;
  COOLDOWN_TICKER=setInterval(function(){
    if(BUSY) return;
    for(var k in GCD_TARGET){
      var btn=document.getElementById('gb-'+k), el=document.getElementById('cd-'+k);
      var rem=(GCD_TARGET[k]||0)-Date.now();
      if(rem>0){ if(el)el.textContent=fmtCd(rem); if(btn){btn.classList.add('cooling');btn.disabled=true;} }
      else { delete GCD_TARGET[k]; if(el)el.textContent=''; if(btn){btn.classList.remove('cooling');btn.disabled=false;} }
    }
  },1000);
}

function bodyAdventure(){
  var zonesCleared=S.zones.filter(function(z){return z.cleared;}).length;
  return '<div class="btns">'+
      actBtn("⚔️ Adventure","adventure","p",false,"Explore the current zone and pick a fight for XP, gold and loot.")+
      actBtn("👑 Boss","boss",null,false,"Fight the zone boss. Win to unlock the next zone.")+
      actBtn("🛌 Rest","rest",null,false,"Rest at the tavern to fully restore HP and MP.")+
    '</div>'+
    '<h3>Gather</h3><div class="btns">'+
      gatherBtn("🪓 Chop","chop","Chop wood for crafting materials + Worker XP. 3-minute cooldown.")+
      gatherBtn("⛏️ Mine","mine","Mine ore for crafting materials + Worker XP. 3-minute cooldown.")+
      gatherBtn("🎣 Fish","fish","Fish up materials + Worker XP. 3-minute cooldown.")+
      gatherBtn("🌿 Forage","forage","Forage herbs + Worker XP. 3-minute cooldown.")+
      gatherBtn("🪏 Dig","dig","Dig for excavation finds + Worker XP. 3-minute cooldown.")+
      gatherBtn("🔦 Scavenge","scavenge","Scavenge salvage + Worker XP. 3-minute cooldown.")+
    '</div>'+
    '<h3>Zones ('+zonesCleared+'/'+S.zones.length+' cleared)</h3><div class="chips">'+
      S.zones.map(function(z){
        var cls=z.cleared?'zc-cleared':z.unlocked?'zc-open':'zc-locked';
        var icon=z.cleared?'✅':z.unlocked?'⚔️':'🔒';
        var tag=(!z.cleared&&z.unlocked)?' <span class="ztag">OPEN</span>':'';
        return '<span class="chip '+cls+'">'+icon+' '+esc(z.name)+tag+'</span>';
      }).join("")+
    '</div>';
}

function bodyShop(){
  if(!S.shop.length) return '<div class="muted">The shop is empty right now.</div>';
  var rows=S.shop.map(function(it){
    return '<div class="item"'+tip(itemTip(it.name,it.desc))+'>'+
      '<div class="listrow"><span class="'+rar(it.rarity)+'">'+esc(it.name)+'</span>'+
      '<span><span class="price">🪙 '+it.price+'</span> '+
      '<button class="sm"'+tip("Buy this with gold.")+' '+(BUSY||!it.affordable?'disabled':'')+' onclick="cmd(\\'buy '+it.n+'\\')">Buy</button></span></div>'+
      itemDetail(it.desc)+'</div>';
  }).join("");
  return '<div class="muted">Tap 🎒 Bag to equip or sell what you own.</div>'+rows;
}

function bodyBag(){
  var eq='<h3>Equipped</h3>'+S.equipped.map(function(s){
    return '<div class="eqrow"><span class="slot">'+esc(s.slot)+'</span>'+
      (s.name?'<span class="'+rar(s.rarity)+'">'+esc(s.name)+'</span>':'<span class="empty">— empty —</span>')+'</div>';
  }).join("");
  if(!S.items.length) return eq+'<h3>Bag</h3><div class="muted">Your bag is empty — go adventure!</div>';

  // The action panel for the currently-selected tile.
  var sel=null; for(var i=0;i<S.items.length;i++){ if(S.items[i].ii===BAG_SEL){ sel=S.items[i]; break; } }
  var panel='';
  if(sel){
    var d=sel.desc;
    panel='<div class="bagsel">'+
      '<div class="bstop"><span class="'+rar(sel.rarity)+'">'+sel.emoji+' '+esc(sel.name)+'</span>'+
        '<button class="x" title="Close" onclick="selectTile('+sel.ii+')">✕</button></div>'+
      itemDetail(d)+
      '<div class="btns" style="margin-top:8px">'+
        (sel.gear?'<button class="sm p"'+tip("Equip — swaps out your current "+sel.slot+".")+' '+(BUSY?'disabled':'')+' onclick="itemAction('+sel.ii+',\\'equip\\')">⚔️ Equip</button>':'')+
        '<button class="sm"'+tip("Sell for gold.")+' '+(BUSY?'disabled':'')+' onclick="itemAction('+sel.ii+',\\'sell\\')">💰 Sell · '+sel.sell+'🪙</button>'+
        '<button class="sm danger"'+tip("Throw it away for nothing.")+' '+(BUSY?'disabled':'')+' onclick="itemAction('+sel.ii+',\\'toss\\')">🗑️ Toss</button>'+
      '</div></div>';
  }

  return eq+'<div class="muted" style="margin:2px 0 8px">Hover to inspect · tap to act.</div>'+panel+
    bagSection('⚔️ Equipment', function(it){ return it.gear; })+
    bagSection('🧪 Materials & Items', function(it){ return !it.gear; });
}
// One tile in the bag grid. p is the item's index within S.items (for hover lookup).
function tileHTML(it,p){
  var badge=it.desc.compare?'<div class="tc '+it.desc.compare.dir+'">'+cmpArrow(it.desc.compare)+'</div>':'';
  return '<div class="tile '+rar(it.rarity)+(BAG_SEL===it.ii?' sel':'')+'"'+
    ' onmouseenter="bagHover(event,'+p+')" onmouseleave="bagOut()" onclick="selectTile('+it.ii+')">'+
    '<div class="tie">'+it.emoji+'</div>'+
    '<div class="tin">'+esc(it.name)+'</div>'+
    (it.qty>1?'<div class="tq">×'+it.qty+'</div>':'')+badge+'</div>';
}
// A titled grid of the items matching pred (empty string if none).
function bagSection(title,pred){
  var t=''; S.items.forEach(function(it,p){ if(pred(it)) t+=tileHTML(it,p); });
  return t?'<h3>'+title+'</h3><div class="bagGrid">'+t+'</div>':'';
}
function cmpArrow(c){ return c.dir==='up'?'▲':c.dir==='down'?'▼':c.dir==='new'?'✨':'='; }
function selectTile(ii){ BAG_SEL=(BAG_SEL===ii?null:ii); bagOut(); render(); }

// Per-item action → item endpoint, then re-render the bag with fresh state.
async function itemAction(ii,action){
  if(BUSY) return; BUSY=true; bagOut();
  try{ var d=await api("/play/api/item",{method:"POST",headers:{'Content-Type':'application/json'},body:JSON.stringify({idx:ii,action:action})});
       LASTMSG=d.reply||""; S=d.state; syncCooldowns(); }
  catch(e){ LASTMSG="⚠️ "+e.message; }
  BUSY=false; BAG_SEL=null; render();
}

// Floating hover popover with an item's stats, comparison and flavor.
function ensurePop(){ var p=document.getElementById('bagpop'); if(!p){ p=document.createElement('div'); p.id='bagpop'; p.className='bagpop'; document.body.appendChild(p);} return p; }
function bagHover(ev,p){
  var it=S.items[p]; if(!it) return;
  var d=it.desc, pop=ensurePop();
  pop.innerHTML='<div class="pn '+rar(it.rarity)+'">'+it.emoji+' '+esc(it.name)+' <span class="muted">'+esc(it.slot)+'</span></div>'+
    (d.stats?'<div class="ps">'+esc(d.stats)+'</div>':'')+
    (d.compare?'<div style="margin-top:3px">'+cmpBadge(d.compare)+'</div>':'')+
    (d.flavor?'<div class="pf">\\u201c'+esc(d.flavor)+'\\u201d</div>':'');
  var r=ev.currentTarget.getBoundingClientRect();
  var pw=Math.min(250,window.innerWidth-16); pop.style.width=pw+'px'; pop.style.display='block';
  var left=r.left; if(left+pw>window.innerWidth-8) left=window.innerWidth-8-pw;
  var top=r.bottom+8; if(top+pop.offsetHeight>window.innerHeight-8) top=r.top-8-pop.offsetHeight;
  pop.style.left=Math.max(8,left)+'px'; pop.style.top=Math.max(8,top)+'px';
}
function bagOut(){ var p=document.getElementById('bagpop'); if(p) p.style.display='none'; }

function craftSeg(id,label,lvl){ return '<button class="'+(CRAFT_TAB===id?'active':'')+'" onclick="setCraft(\\''+id+'\\')">'+label+' <span class="lvpill">Lv '+lvl+'</span></button>'; }
function setCraft(t){ CRAFT_TAB=t; CRAFT_LEVEL=null; render(); }
function setCraftLevel(L){ CRAFT_LEVEL=L; render(); }

function xpHeader(prog){
  var p=Math.min(100,Math.round(100*(prog.xp||0)/Math.max(1,prog.next)));
  return '<div style="margin-bottom:10px">'+
    '<div class="barlab"><span>'+prog.emoji+' '+esc(prog.name)+' Lv '+prog.level+'</span><span>'+prog.xp+' / '+prog.next+' XP to next</span></div>'+
    '<div class="bar"><div class="fill xp" style="width:'+p+'%"></div></div></div>';
}

function bodyCraft(){
  var cp=S.craftProg||{crafter:{level:1},alchemist:{level:1},enchanter:{level:1}};
  var seg='<div class="tabs" style="margin-bottom:10px">'+
    craftSeg('crafter','🔨 Craft',cp.crafter.level)+craftSeg('alchemist','⚗️ Brew',cp.alchemist.level)+craftSeg('enchant','✨ Enchant',cp.enchanter.level)+'</div>';
  var body = CRAFT_TAB==='enchant' ? enchantBody() : recipeBody(CRAFT_TAB);
  return seg+body+
    '<h3>Professions</h3><div class="chips">'+
      S.professions.map(function(p){return '<span class="chip">'+esc(p.emoji)+' '+esc(p.name)+' <span class="pill">'+p.level+'</span></span>';}).join("")+
    '</div>';
}

// Clickable recipe list for crafter/alchemist. Tap a ready recipe to make it.
function recipeBody(prof){
  var list=(S.recipes&&S.recipes[prof])||[], verb=prof==='alchemist'?'brew':'craft';
  var head=S.craftProg?xpHeader(S.craftProg[prof]):'';
  if(!list.length) return head+'<div class="muted">No recipes here yet.</div>';
  var myLvl=(S.craftProg&&S.craftProg[prof].level)||1;

  // Group recipes by required level → one tab per tier.
  var levels=[]; list.forEach(function(r){ if(levels.indexOf(r.level)<0) levels.push(r.level); });
  levels.sort(function(a,b){return a-b;});
  var sel=levels.indexOf(CRAFT_LEVEL)>=0?CRAFT_LEVEL:levels[0];
  var lvlTabs=levels.length>1?'<div class="tabs lvltabs" style="margin-bottom:8px">'+levels.map(function(L){
      return '<button class="'+(sel===L?'active':'')+'" onclick="setCraftLevel('+L+')">Lv '+L+(myLvl<L?' 🔒':'')+'</button>';
    }).join('')+'</div>':'';

  var rows=list.filter(function(r){return r.level===sel;}).map(function(r){
    var ings=r.inputs.map(function(x){
      return '<span class="ing '+(x.ok?'ok':'no')+'">'+esc(x.name)+' '+Math.min(x.have,x.need)+'/'+x.need+'</span>';
    }).join('');
    var stat = r.locked?'<span class="rstat l">🔒 Lv '+r.level+'</span>'
             : r.ready?'<span class="rstat g">'+(verb==='brew'?'Brew ▸':'Craft ▸')+'</span>'
             : '<span class="rstat r">need mats</span>';
    var cls='rrow'+(r.ready?' ready':'')+(r.locked?' locked':'');
    var click=r.ready?' onclick="cmd(\\''+verb+' '+r.n+'\\')"':'';
    var ttl=r.locked?'Needs level '+r.level:(r.ready?'Tap to '+verb+' this — grants +'+r.xp+' XP':'Gather the missing materials first');
    return '<div class="'+cls+'"'+tip(ttl)+click+'>'+
      '<div class="rtop"><span class="rname">'+r.emoji+' '+esc(r.name)+'</span>'+stat+'</div>'+
      '<div class="rings">'+ings+'<span class="rxp">+'+r.xp+' XP</span></div></div>';
  }).join('');
  return head+lvlTabs+'<div class="muted" style="margin-bottom:6px">Tap a ready recipe to make it. Gather materials with the Adventure tab.</div>'+
    '<div class="recipeList">'+rows+'</div>';
}

// Clickable enchant list — tap a gear piece to upgrade it.
function enchantBody(){
  var e=S.enchants;
  if(!e||!e.list.length) return '<div class="muted">No gear to enchant yet — craft or find some first.</div>';
  var rows=e.list.map(function(x){
    var where=x.where==='equipped'?'⚔️':'🎒';
    var stat = x.maxed?'<span class="rstat l">MAX +'+x.cur+'</span>'
             : x.affordable?'<span class="rstat g">Enchant ▸</span>'
             : '<span class="rstat r">need '+x.cost.gold+'🪙 + '+x.cost.quartz+'</span>';
    var cls='rrow'+(x.affordable?' ready':'')+(x.maxed?' locked':'');
    var click=x.affordable?' onclick="cmd(\\'enchant '+x.n+'\\')"':'';
    return '<div class="'+cls+'"'+tip(x.maxed?'At your enchant cap':'Costs '+x.cost.gold+' gold + '+x.cost.quartz+' '+e.reagent)+click+'>'+
      '<div class="rtop"><span class="rname">'+where+' '+x.emoji+' <span class="'+rar(x.rarity)+'">'+esc(x.name)+'</span>'+(x.cur?' <span class="muted">+'+x.cur+'</span>':'')+'</span>'+stat+'</div></div>';
  }).join('');
  var head=S.craftProg?xpHeader(S.craftProg.enchanter):'';
  return head+'<div class="muted" style="margin-bottom:6px">cap +'+e.cap+' · '+e.reagent+' ×'+e.quartz+'. Tap a piece to enchant it.</div>'+
    '<div class="recipeList">'+rows+'</div>';
}

function moreView(v){ MORE_VIEW=v; render(); }
function backBar(t){ return '<div class="tabs" style="margin-bottom:10px"><button onclick="moreView(null)">← Back</button><button class="active" style="pointer-events:none">'+t+'</button></div>'; }

function bodyMore(){
  if(MORE_VIEW==='skills') return backBar('✨ Skills')+skillsPanel();
  if(MORE_VIEW==='leaderboard') return backBar('🏆 Leaderboard')+leaderboardPanel();
  if(MORE_VIEW==='quest') return backBar('📜 Quest')+questPanel();

  var lb=S.lootbox||{boxes:0,price:0};
  var box='<h3>🎁 Mystery Boxes</h3>'+
    '<div class="muted" style="margin-bottom:6px">You hold <b style="color:var(--ink)">'+lb.boxes+'</b> box'+(lb.boxes===1?'':'es')+'. Boxes drop gold, gear, materials or potions.</div>'+
    '<div class="btns">'+
      '<button class="sm p" '+(BUSY||S.gold<lb.price?'disabled':'')+' title="Buy a mystery box for '+lb.price+' gold."'+' onclick="cmd(\\'lootbox buy\\')">🛒 Buy box · '+lb.price+'🪙</button>'+
      '<button class="sm" '+(BUSY||lb.boxes<1?'disabled':'')+' title="Open one of your boxes." onclick="cmd(\\'lootbox open\\')">🎁 Open</button>'+
      (lb.boxes>1?'<button class="sm" '+(BUSY?'disabled':'')+' title="Open up to 10 boxes at once." onclick="cmd(\\'lootbox open '+Math.min(lb.boxes,10)+'\\')">Open all ('+Math.min(lb.boxes,10)+')</button>':'')+
    '</div>';
  var q=S.quest||{}, qtag=q.claimable?' <span class="rstat g">ready!</span>':q.claimed?' <span class="rstat l">done</span>':'';
  var other='<h3>More</h3><div class="btns">'+
      '<button class="sm" title="Your daily quest and reward." onclick="moreView(\\'quest\\')">📜 Quest'+qtag+'</button>'+
      '<button class="sm" title="See the top heroes." onclick="moreView(\\'leaderboard\\')">🏆 Leaderboard</button>'+
      '<button class="sm" title="Your class skills and what unlocks next." onclick="moreView(\\'skills\\')">✨ Skills</button>'+
      actBtn("⭐ Ascend","ascend","sm",false,"Prestige at high level: reset for a permanent +2% stats each time.")+
    '</div>';
  return box+other+
    '<div class="muted" style="margin-top:10px">Tip: your browser hero and your chat hero are the same character.</div>';
}

function skillsPanel(){
  var sk=S.skillbook||{unlocked:[],locked:[]};
  var rows=sk.unlocked.map(function(s){
    return '<div class="rrow"><div class="rtop"><span class="rname">✨ '+esc(s.name)+'</span>'+
      '<span class="rstat g">'+s.mp+' MP</span></div>'+
      '<div class="rings"><span class="ing ok">'+esc(s.type)+'</span>'+(s.power?'<span class="ing ok">power '+s.power+'</span>':'')+'</div></div>';
  }).join('') || '<div class="muted">No skills yet.</div>';
  var locked=sk.locked.length?('<h3>🔒 Unlocks next</h3><div class="chips">'+
    sk.locked.map(function(l){return '<span class="chip">'+esc(l.name)+' <span class="pill">Lv '+l.level+'</span></span>';}).join('')+'</div>'):'';
  return '<div class="muted" style="margin-bottom:6px">'+esc(sk.className)+' · Lv '+S.level+' — used in combat.</div><div class="recipeList">'+rows+'</div>'+locked;
}

function leaderboardPanel(){
  var lb=S.leaderboard||[];
  if(!lb.length) return '<div class="muted">No heroes yet.</div>';
  var medal=['🥇','🥈','🥉'];
  var rows=lb.map(function(h){
    var th=THEME[h.cls]||{c:'#7cc44a',e:'🎮'};
    var rank=medal[h.rank-1]||('#'+h.rank);
    var stars=h.ascension?' <span class="stars">'+'★'.repeat(Math.min(5,h.ascension))+(h.ascension>5?'+':'')+'</span>':'';
    return '<div class="rrow'+(h.me?' ready':'')+'"><div class="rtop">'+
      '<span class="rname"><span style="display:inline-block;width:26px">'+rank+'</span>'+th.e+' '+esc(h.name)+(h.me?' <span class="muted">(you)</span>':'')+'</span>'+
      '<span class="rstat g">Lv '+h.level+stars+'</span></div>'+
      '<div class="rings"><span class="ing ok">'+esc(h.className)+'</span></div></div>';
  }).join('');
  return '<div class="recipeList">'+rows+'</div>';
}

function questPanel(){
  var q=S.quest||{};
  if(!q.desc) return '<div class="muted">No quest available.</div>';
  var pctv=Math.min(100,Math.round(100*(q.progress||0)/Math.max(1,q.target)));
  var status=q.claimed?'<span class="rstat l">✅ claimed — new one tomorrow</span>':q.done?'<span class="rstat g">✨ ready to claim!</span>':'<span class="rstat">'+q.progress+' / '+q.target+'</span>';
  return '<div class="card" style="border-color:var(--accent)">'+
    '<div class="rtop"><span class="rname">📜 '+esc(q.desc)+'</span>'+status+'</div>'+
    '<div class="bar" style="margin-top:10px"><div class="fill xp" style="width:'+pctv+'%"></div></div>'+
    '<div class="muted" style="margin-top:8px">Reward: <b style="color:var(--gold)">'+q.gold+' 🪙</b> + '+q.xp+' XP'+(q.claimed||q.done?'':' · matching actions count automatically')+'</div>'+
    (q.claimable?'<div class="btns" style="margin-top:10px"><button class="p" '+(BUSY?'disabled':'')+' onclick="cmd(\\'quest claim\\')">🎉 Claim reward</button></div>':'')+
  '</div>';
}

// A 1–9 number pad that repeats the last "list" command with a chosen index —
// e.g. after "Recipes", tapping 2 sends "craft 2".
var PADCMD=null;
function numPad(cmds){
  // Show a pad only after one of these list commands was the last thing run.
  var hint = PADCMD && cmds.indexOf(PADCMD)>=0 ? PADCMD : null;
  var pick = hint || cmds[0];
  var nums="";
  for(var i=1;i<=9;i++) nums+='<button class="sm"'+tip("Do: "+pick+" #"+i+" (from the list above)")+' '+(BUSY?'disabled':'')+' onclick="cmd(\\''+pick+' '+i+'\\')">'+i+'</button>';
  return '<div class="barlab" style="margin-top:10px"><span>Make #</span><span class="muted">'+esc(pick)+' &lt;#&gt;</span></div><div class="btns">'+nums+'</div>';
}

// ── Character creation ──────────────────────────────────────────────────────
function renderCreate(){
  document.documentElement.style.setProperty("--accent","#7cc44a");
  var cls=S.classes.map(function(c){
    return '<div class="pk '+(NEWCLS===c.id?'sel':'')+'" onclick="pickCls(\\''+c.id+'\\')"><b>'+(THEME[c.id]?THEME[c.id].e+' ':'')+esc(c.name)+'</b><small>'+esc(c.blurb||'')+'</small></div>';
  }).join("");
  var race=S.races.map(function(r){
    return '<div class="pk '+(NEWRACE===r.id?'sel':'')+'" onclick="pickRace(\\''+r.id+'\\')"><b>'+esc(r.name)+'</b><small>'+esc(r.blurb||'')+'</small></div>';
  }).join("");
  document.getElementById("root").innerHTML=
    '<div class="card"><h3>Choose a class</h3><div class="cls-pick">'+cls+'</div>'+
    '<h3>Choose a race</h3><div class="cls-pick">'+race+'</div>'+
    '<div class="center" style="margin-top:16px">'+
      '<button class="p"'+tip("Create your hero with the chosen class and race.")+' '+(NEWCLS&&NEWRACE&&!BUSY?'':'disabled')+' onclick="doCreate()">🎉 Create Hero</button>'+
    '</div>'+(LASTMSG?'<div class="log" style="margin-top:12px">'+esc(LASTMSG)+'</div>':'')+'</div>';
}
function pickCls(id){ NEWCLS=id; renderCreate(); }
function pickRace(id){ NEWRACE=id; renderCreate(); }
async function doCreate(){ if(!NEWCLS||!NEWRACE)return; await cmd("create "+NEWCLS+" "+NEWRACE); }

// Track which list command was last run so the craft/more number pads target it.
var _cmd=cmd;
cmd=function(c){
  var head=c.split(" ")[0];
  if(head==="attack") autoArmed=true;   // the first Attack click hands control to auto
  if(["recipes","brew","enchant","lootbox","quest"].indexOf(head)>=0) PADCMD=(head==="recipes"?"craft":head);
  return _cmd(c);
};

if(!TOKEN){ fail("No play link — type 'tt web' in chat to get your personal link."); }
else refresh();
</script>
</body></html>`;
