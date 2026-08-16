import { CLASS_LIST, RACE_LIST, ZONE_LIST, RARITIES } from '../game/content.js';
import { config } from '../config.js';

export const guideUrl = () => (config.publicUrl ? `${config.publicUrl.replace(/\/+$/, '')}/guide` : null);

const esc = (s) => String(s || '').replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[c]));

// Curated command reference (grouped) — mirrors the in-game `tt help`.
const COMMANDS = [
  ['Getting started', [
    ['tt help', 'the in-chat command menu'],
    ['tt classes / tt races', 'see the options before you roll'],
    ['tt create <class> <race> [name]', 'create your hero'],
    ['tt new <class> <race> [name]', 'start over with a fresh hero'],
    ['tt char', 'your character sheet (stats, gear, professions)'],
    ['tt skills', 'your class abilities'],
  ]],
  ['Adventuring & combat', [
    ['tt zones', 'where you can travel'],
    ['tt adventure [zone]', 'find a fight (costs ⚡ stamina)'],
    ['tt attack', 'start / continue auto-battle'],
    ['tt skill <#>', 'cast an ability by number'],
    ['tt use', 'drink a potion mid-fight'],
    ['tt flee', 'run from a fight'],
    ['tt boss', "challenge the zone's boss to unlock the next zone"],
    ['tt raid join', 'join a shared raid boss when one appears'],
    ['tt rest', 'recover HP / MP / stamina in town'],
  ]],
  ['Gear & items', [
    ['tt inv', 'your bag & gold'],
    ['tt inspect <#>', 'full stats & effects of an item (number from tt inv)'],
    ['tt equip <#>', 'wear a piece of gear'],
    ['tt shop', 'browse gear & potions to buy'],
    ['tt buy <#> [qty]', 'buy from the shop (gear auto-equips)'],
    ['tt sell <#> / tt sell junk', 'sell gear by number, or all materials at once'],
  ]],
  ['Gathering & crafting', [
    ['tt chop / mine / fish / forage / dig / scavenge', 'gather materials (3-min cooldown each) — levels Worker'],
    ['tt recipes', 'crafting recipes (add "brew" for potions)'],
    ['tt craft <#>', 'forge gear from materials (Crafter)'],
    ['tt brew <#>', 'brew potions & flasks (Alchemist)'],
    ['tt enchant <#>', 'empower a gear piece with materials + gold (Enchanter)'],
  ]],
  ['Extras & progression', [
    ['tt quest', 'your daily quest · tt quest claim to collect'],
    ['tt lootbox', 'buy & open mystery boxes (Lootboxer)'],
    ['tt leaderboard', 'top heroes'],
    ['tt ascend', 'prestige at level 30+ for permanent bonuses'],
  ]],
  ['Play from stream chat', [
    ['tt play <your Discord @username>', 'link your chat account — the bot DMs you a code'],
    ['tt confirm <code>', 'confirm the link; your chat & Discord share one hero'],
  ]],
];

const PROFESSIONS = [
  ['⛏️ Worker', 'Gather raw materials from the wild with chop/mine/fish/forage/dig/scavenge.'],
  ['🔨 Crafter', 'Turn materials into weapons, armour and accessories with tt craft.'],
  ['⚗️ Alchemist', 'Brew potions, ethers and elemental flasks with tt brew.'],
  ['✨ Enchanter', 'Permanently boost a gear piece with tt enchant (costs Quartz + gold).'],
  ['💰 Merchant', 'Levels automatically as you buy & sell — better prices the higher it gets.'],
  ['🎁 Lootboxer', 'Open mystery boxes for gold, gear, materials or potions; odds improve with level.'],
];

export function guideHtml() {
  const classes = CLASS_LIST.map((c) =>
    `<tr><td><b>${esc(c.name)}</b></td><td>${esc(c.primary?.toUpperCase() || '')}</td><td>${esc(c.armor_weight || '')}</td><td>${esc(c.blurb || '')}</td></tr>`).join('');
  const races = RACE_LIST.map((r) =>
    `<tr><td><b>${esc(r.name)}</b></td><td>${esc(r.blurb || '')}</td></tr>`).join('');
  const zones = ZONE_LIST.map((z) =>
    `<li><b>${esc(z.name)}</b> <span class="dim">— level ${z.level_required}+</span></li>`).join('');
  const rarities = RARITIES.map((r) =>
    `<span class="chip r-${esc(r.id)}">${esc(r.name || r.id)}</span>`).join(' ');
  const professions = PROFESSIONS.map(([n, d]) => `<li><b>${esc(n)}</b> — ${esc(d)}</li>`).join('');
  const cmdGroups = COMMANDS.map(([title, rows]) => `
    <h3>${esc(title)}</h3>
    <table class="cmd">${rows.map(([c, d]) => `<tr><td class="c"><code>${esc(c)}</code></td><td>${esc(d)}</td></tr>`).join('')}</table>`).join('');

  return `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1"/>
<title>Tavern Tales — Player Guide</title>
<style>
  :root { --bg:#0c140f; --card:#101c14; --accent:#7cc44a; --ink:#e8e0c4; --dim:#8aa07c; --line:#254a2e; }
  * { box-sizing:border-box; }
  body { margin:0; background:var(--bg); color:var(--ink); font-family:'Segoe UI',system-ui,Arial,sans-serif; line-height:1.5; }
  .wrap { max-width:900px; margin:0 auto; padding:24px 18px 60px; }
  header { text-align:center; padding:26px 0 10px; }
  header h1 { margin:0; font-size:34px; color:var(--accent); letter-spacing:0.5px; }
  header p { margin:6px 0 0; color:var(--dim); }
  section { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:18px 20px; margin:16px 0; }
  h2 { color:var(--accent); border-bottom:1px solid var(--line); padding-bottom:8px; margin:0 0 12px; font-size:22px; }
  h3 { color:#a8c488; margin:16px 0 6px; font-size:15px; text-transform:uppercase; letter-spacing:0.04em; }
  code { background:#08110b; color:#bfe39a; padding:2px 6px; border-radius:5px; font-family:Consolas,monospace; font-size:0.92em; white-space:nowrap; }
  table { width:100%; border-collapse:collapse; }
  table.cmd td { padding:5px 8px; vertical-align:top; border-bottom:1px solid rgba(37,74,46,0.4); }
  table.cmd td.c { width:44%; }
  table.data th, table.data td { text-align:left; padding:6px 8px; border-bottom:1px solid rgba(37,74,46,0.5); }
  table.data th { color:var(--dim); font-size:12px; text-transform:uppercase; }
  ul { margin:6px 0; padding-left:20px; }
  .dim { color:var(--dim); }
  .chip { display:inline-block; padding:2px 10px; border-radius:20px; font-size:12px; font-weight:700; margin:2px; border:1px solid var(--line); }
  .r-common{color:#cfcfcf;} .r-uncommon{color:#7cc44a;} .r-rare{color:#4aa3ff;} .r-epic{color:#b06bff;} .r-legendary{color:#f0c040;}
  .lead { color:var(--dim); }
  .steps { counter-reset:s; list-style:none; padding-left:0; }
  .steps li { counter-increment:s; position:relative; padding:6px 0 6px 34px; }
  .steps li::before { content:counter(s); position:absolute; left:0; top:6px; width:22px; height:22px; background:var(--accent); color:#08110b; border-radius:50%; text-align:center; font-weight:700; font-size:13px; line-height:22px; }
  footer { text-align:center; color:var(--dim); font-size:12px; margin-top:24px; }
  @media (max-width:560px){ table.cmd td.c{ width:100%; display:block; } table.cmd td{ display:block; border:0; padding:2px 0; } table.cmd tr{ display:block; border-bottom:1px solid rgba(37,74,46,0.4); padding:6px 0; } }
</style></head>
<body><div class="wrap">
  <header>
    <h1>🍺 Tavern Tales</h1>
    <p>A cross-platform text RPG you play right in chat — on Discord and in the stream.</p>
  </header>

  <section>
    <h2>How to play</h2>
    <p class="lead">Every command starts with <code>tt</code> — type it in Discord, or in the stream chat once you've linked your account.</p>
    <ol class="steps">
      <li>Pick a class &amp; race, then roll your hero: <code>tt create knight human Bob</code></li>
      <li>Go fight things: <code>tt adventure</code> then <code>tt attack</code>. Everyone in the channel sees the battle.</li>
      <li>Gather materials (<code>tt chop</code>, <code>tt mine</code>…), craft &amp; enchant gear, and <code>tt equip</code> it.</li>
      <li>Beat each zone's boss (<code>tt boss</code>) to travel further, team up for raids, and climb the <code>tt leaderboard</code>.</li>
      <li><b>Watching on stream?</b> Link once with <code>tt play &lt;your Discord @username&gt;</code> → the bot DMs you a code → <code>tt confirm &lt;code&gt;</code>. Now your chat and Discord share the same hero.</li>
    </ol>
  </section>

  <section>
    <h2>All commands</h2>
    ${cmdGroups}
  </section>

  <section>
    <h2>Classes</h2>
    <table class="data"><tr><th>Class</th><th>Main stat</th><th>Armor</th><th>Style</th></tr>${classes}</table>
    <h2 style="margin-top:18px;">Races</h2>
    <table class="data"><tr><th>Race</th><th>Trait</th></tr>${races}</table>
  </section>

  <section>
    <h2>Professions</h2>
    <ul>${professions}</ul>
  </section>

  <section>
    <h2>Gear, rarities &amp; items</h2>
    <p>Loot drops in rarities — higher rarity rolls more affixes and bigger stats:</p>
    <p>${rarities}</p>
    <p class="dim">See any item's full stats and special effects with <code>tt inspect &lt;#&gt;</code> (the number next to it in <code>tt inv</code>), or when it drops and on your <code>tt char</code> sheet. Weapons add <b>PWR</b>, armour adds <b>DEF</b>/<b>RES</b>, and affixes grant stat bonuses (STR, CRIT%, etc.). Enchanting adds a permanent <b>✨+N</b>.</p>
  </section>

  <section>
    <h2>The world</h2>
    <p>Clear each zone's boss to unlock the next:</p>
    <ol>${zones}</ol>
    <p class="dim">A shared <b>raid</b> boss appears every 6–12 hours — <code>tt raid join</code> to team up; everyone who joins shares the loot. Reach level 30 (or clear every zone) and you can <code>tt ascend</code> to prestige for permanent bonuses.</p>
  </section>

  <footer>Tavern Tales · run <code>tt help</code> in chat any time · 🦎 LazerGuanas Game Hunter</footer>
</div></body></html>`;
}
