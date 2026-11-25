using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTracker.Models
{
    /// <summary>
    /// Ready-made tracker boards for the RPGs people usually randomize on NES, SNES,
    /// PS1 and PS2. These are starting points, not gospel: every randomizer shuffles a
    /// slightly different pool, so a set is copied into the streamer's own list when they
    /// add it, and they can rename, add, remove and regroup anything afterwards.
    ///
    /// Format of a group string: "Group name: item|item|item".
    /// An item ending in "*N" is a countable (e.g. "Mini Medals*110") instead of a
    /// simple on/off tick.
    /// </summary>
    public static class TrackerCatalog
    {
        /// <summary>Consoles covered, in the order the picker lists them.</summary>
        public static readonly string[] Systems = { "NES", "SNES", "PS1", "PS2" };

        public static IReadOnlyList<TrackerSet> All => _all.Value;

        private static readonly Lazy<List<TrackerSet>> _all = new(Build);

        /// <summary>A catalog set by id, or null. The caller clones it before using it.</summary>
        public static TrackerSet? ById(string? id) =>
            All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        // ── builder helpers ───────────────────────────────────────────────────
        private static TrackerSet S(string id, string system, string name, params string[] groups)
        {
            var set = new TrackerSet { Id = id, SourceId = id, System = system, Name = name };
            foreach (var g in groups)
            {
                int c = g.IndexOf(':');
                if (c <= 0) continue;
                var grp = new TrackerGroup { Name = g[..c].Trim() };
                foreach (var raw in g[(c + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var item = TrackerItem.Parse(raw);
                    if (item.Name.Length > 0) grp.Items.Add(item);
                }
                if (grp.Items.Count > 0) set.Groups.Add(grp);
            }
            return set;
        }

        private static List<TrackerSet> Build() => new()
        {
            // ═══════════════════════════ NES ═══════════════════════════
            S("nes-ff1", "NES", "Final Fantasy",
                "Orbs: Earth Orb|Fire Orb|Water Orb|Air Orb",
                "Key Items: Lute|Crown|Crystal Eye|Jolt Tonic|Mystic Key|Nitro Powder|Adamant|Slab|Ruby|Earth Rod|Levistone|Chime|Oxyale|Rat's Tail|Cube|Bottled Faerie|Herb",
                "Vehicles: Ship|Canoe|Airship",
                "Bosses: Garland|Pirates|Astos|Vampire|Lich|Marilith|Kraken|Tiamat|Chaos"),

            S("nes-dw1", "NES", "Dragon Warrior",
                "Key Items: Magic Key|Silver Harp|Staff of Rain|Stones of Sunlight|Rainbow Drop|Erdrick's Token|Erdrick's Tablet|Gwaelin's Love|Fairy Flute|Fairy Water|Cursed Belt",
                "Erdrick's Gear: Erdrick's Sword|Erdrick's Armor|Erdrick's Token",
                "Quests: Rescue Gwaelin|Cross the Bridge|Build the Rainbow Bridge|Enter Charlock",
                "Bosses: Green Dragon|Golem|Axe Knight|Dragonlord"),

            S("nes-dw2", "NES", "Dragon Warrior II",
                "Party: Prince of Midenhall|Prince of Cannock|Princess of Moonbrooke",
                "Crests: Sun Crest|Star Crest|Moon Crest|Water Crest|Life Crest",
                "Key Items: Golden Key|Jailor's Key|Watergate Key|Silver Key|Eye of Malroth|Charm of Rubiss|Echoing Flute|Wind Cloak|Water Flying Cloth|Ship",
                "Bosses: Dragon Fly|Atlas|Bazuzu|Zarlox|Malroth"),

            S("nes-dw3", "NES", "Dragon Warrior III",
                "Orbs: Orbs*6",
                "Key Items: Thief's Key|Magic Key|Final Key|Black Pepper|Royal Scroll|Magic Ball|Dream Ruby|Sailor's Thigh Bone|Echoing Flute|Staff of Change|Lamp of Darkness|Silver Harp|Rainbow Drop|Ship",
                "Quests: Romaly Arena|Bottomless Pot|Pyramid|Necrogond|Open the Rainbow Bridge|Reach Alefgard",
                "Bosses: Kandar|Robbin' Hood|Boss Troll|King Hydra|Baramos|Zoma"),

            S("nes-dw4", "NES", "Dragon Warrior IV",
                "Chapters: Ragnar|Alena, Cristo & Brey|Taloon|Mara & Nara|The Hero",
                "Party: Hero|Ragnar|Alena|Cristo|Brey|Taloon|Mara|Nara|Healie",
                "Key Items: Magic Key|Final Key|Thief's Key|Sphere of Silence|Staff of Transform|Silver Statue|Golden Bracelet|Flying Shoes|Zenithian Sword|Zenithian Armor|Zenithian Helmet|Zenithian Shield",
                "Bosses: Balzack|Keeleon|Marquis de Leon|Aamon|Necrosaro"),

            S("nes-zelda1", "NES", "The Legend of Zelda",
                "Triforce: Triforce Shards*8",
                "Swords: Wooden Sword|White Sword|Magical Sword",
                "Items: Bow|Arrow|Silver Arrow|Blue Candle|Red Candle|Boomerang|Magical Boomerang|Raft|Stepladder|Recorder|Magical Rod|Book of Magic|Power Bracelet|Blue Ring|Red Ring|Magical Key|Letter|Bait|Bombs",
                "Dungeons: Eagle|Moon|Manji|Snake|Lizard|Dragon|Demon|Lion|Death Mountain"),

            S("nes-zelda2", "NES", "Zelda II: The Adventure of Link",
                "Spells: Shield|Jump|Life|Fairy|Fire|Reflect|Spell|Thunder",
                "Items: Candle|Handy Glove|Raft|Boots|Flute|Cross|Magical Key|Hammer",
                "Crystals: Palace Crystals*6",
                "Palaces: Parapa|Midoro|Island|Maze|Sea|Hidden|Great Palace"),

            S("nes-crystalis", "NES", "Crystalis",
                "Swords: Sword of Wind|Sword of Fire|Sword of Water|Sword of Thunder",
                "Orbs & Bracelets: Ball of Wind|Tornado Bracelet|Ball of Fire|Flame Bracelet|Ball of Water|Blizzard Bracelet|Ball of Thunder|Storm Bracelet",
                "Key Items: Statue of Onyx|Insect Flute|Key to Prison|Flute of Lime|Fog Lamp|Shell Flute|Broken Statue|Eye Glasses|Glowing Lamp|Love Pendant|Kirisa Plant|Ivory Statue|Statue of Gold|Bow of Moon|Bow of Sun|Bow of Truth",
                "Bosses: Vampire|Insect|Kelbesque|Sabera|Mado|Karmine|Draygon|Dyna"),

            S("nes-mother", "NES", "EarthBound Beginnings (Mother)",
                "Melodies: Melodies*8",
                "Party: Ninten|Lloyd|Ana|Teddy",
                "Key Items: Franklin Badge|Onyx Hook|Bread|Hamburger|Key to the Tower|Pendant|Cash Card|Canary Chick|Flea Bag|Red Ribbon",
                "Bosses: Starman|Giegue|R7038|Titanic Ant|Mt. Itoi Star Man"),

            // ═══════════════════════════ SNES ═══════════════════════════
            S("snes-alttp", "SNES", "A Link to the Past",
                "Equipment: Bow|Boomerang|Hookshot|Bombs|Mushroom|Magic Powder|Fire Rod|Ice Rod|Lamp|Hammer|Flute|Bug Net|Book of Mudora|Bottle|Cane of Somaria|Cane of Byrna|Magic Cape|Magic Mirror|Pegasus Boots|Flippers|Moon Pearl|Power Glove|Titan's Mitt|Hammer",
                "Medallions: Bombos|Ether|Quake",
                "Pendants & Crystals: Pendant of Courage|Pendant of Power|Pendant of Wisdom|Crystals*7",
                "Dungeons: Eastern Palace|Desert Palace|Tower of Hera|Hyrule Castle Tower|Palace of Darkness|Swamp Palace|Skull Woods|Thieves' Town|Ice Palace|Misery Mire|Turtle Rock|Ganon's Tower|Ganon"),

            S("snes-supermetroid", "SNES", "Super Metroid",
                "Beams: Charge Beam|Ice Beam|Wave Beam|Spazer|Plasma Beam",
                "Suits & Boots: Varia Suit|Gravity Suit|Hi-Jump Boots|Speed Booster|Space Jump|Screw Attack",
                "Morph Ball: Morph Ball|Bombs|Spring Ball|Power Bombs",
                "Misc: Grapple Beam|X-Ray Scope",
                "Bosses: Bomb Torizo|Spore Spawn|Kraid|Crocomire|Phantoon|Botwoon|Draygon|Golden Torizo|Ridley|Mother Brain"),

            S("snes-ff4", "SNES", "Final Fantasy IV",
                "Party: Cecil|Kain|Rydia|Tellah|Edward|Rosa|Yang|Palom|Porom|Cid|Edge|FuSoYa",
                "Crystals: Water Crystal|Fire Crystal|Wind Crystal|Earth Crystal|Dark Crystals*4",
                "Key Items: Package|Baron Key|Magma Stone|Sand Ruby|Twin Harp|Earth Crystal|Rat Tail|Adamant|Pink Tail|Crystal|Pass|Hovercraft|Enterprise|Falcon|Big Whale",
                "Bosses: Mist Dragon|Octomammoth|Antlion|Mom Bomb|Milon|Milon Z|Baigan|Cagnazzo|Dark Elf|Magus Sisters|Valvalis|Calcabrina|Golbez|Dr. Lugae|Rubicante|Evil Wall|Demon Wall|Bahamut|Elements|CPU|Zeromus"),

            S("snes-ff5", "SNES", "Final Fantasy V",
                "Crystals: Wind Crystal|Water Crystal|Fire Crystal|Earth Crystal",
                "Party: Bartz|Lenna|Galuf|Faris|Krile",
                "Summons: Chocobo|Sylph|Remora|Shiva|Ifrit|Ramuh|Titan|Golem|Catoblepas|Carbuncle|Syldra|Odin|Phoenix|Leviathan|Bahamut",
                "Key Items: Adamantite|Hiryuu|Fire Ship|Submarine|Airship|Elder Branch|Gold Hairpin|Mirage Vest",
                "Bosses: Wing Raptor|Karlabos|Siren|Magissa|Garula|Liquid Flame|Iron Claw|Byblos|Sandworm|Cray Claw|Adamantoise|Soul Cannon|Gilgamesh|Atomos|Exdeath|Neo Exdeath"),

            S("snes-ff6", "SNES", "Final Fantasy VI",
                "Characters: Terra|Locke|Edgar|Sabin|Celes|Cyan|Gau|Setzer|Strago|Relm|Mog|Umaro|Gogo|Shadow",
                "Espers: Ramuh|Kirin|Siren|Ifrit|Shiva|Unicorn|Maduin|Shoat|Phantom|Carbunkl|Bismark|Golem|Zoneseek|Sraphim|Palidor|Fenrir|Stray|Tritoch|Terrato|Odin|Raiden|Bahamut|Alexandr|Crusader|Ragnarok|Phoenix|Starlet|Zona Seek",
                "Dragons: Ice Dragon|Storm Dragon|Earth Dragon|Blue Dragon|Red Dragon|Skull Dragon|Gold Dragon|Dirt Dragon",
                "Bosses: Whelk|Ultros|Number 024|Atma Weapon|Doom Gaze|Inferno|Hidon|Doom|Poltrgeist|Goddess|Kefka"),

            S("snes-ct", "SNES", "Chrono Trigger",
                "Characters: Crono|Marle|Lucca|Frog|Robo|Ayla|Magus",
                "Key Items: Pendant|Gate Key|Dreamstone|Ruby Knife|Bent Hilt|Bent Sword|Hero Medal|Masamune|Tools|Jerky|Moon Stone|Sun Stone|Clone|C. Trigger|Rainbow Shell|Prismshard|Toma's Pop|Bike Key|Seed|Naga-ette Bromide",
                "Quests: Trial|Cathedral|Factory|Dreamstone|Masamune|Rainbow Shell|Sun Stone|Ozzie's Fort|Black Omen|Death Peak",
                "Bosses: Dragon Tank|Yakra|Masa & Mune|Heckran|Zombor|Nizbel|Magus|Black Tyrano|Giga Gaia|Golem|Son of Sun|Retinite|Rust Tyrano|Mother Brain|Lavos"),

            S("snes-smrpg", "SNES", "Super Mario RPG",
                "Stars: Star Pieces*7",
                "Party: Mario|Mallow|Geno|Bowser|Peach",
                "Key Items: Rare Frog Coin|Cricket Pie|Cricket Jam|Seed|Fertilizer|Temple Key|Castle Key 1|Castle Key 2|Bambino Bomb|Alto Card|Tenor Card|Soprano Card|Shed Key|Elder Key|Room Key|Carbo Cookie|Fireworks|Mystery Egg|Lucky Jewel|Dry Bones Flag",
                "Bosses: Hammer Bro|Croco|Mack|Belome|Bowyer|Punchinello|Booster|Knife Guy|Yaridovich|Bundt|Johnny|Czar Dragon|Axem Rangers|Exor|Smithy"),

            S("snes-earthbound", "SNES", "EarthBound",
                "Your Sanctuary: Giant Step|Lilliput Steps|Milky Well|Rainy Circle|Magnet Hill|Pink Cloud|Lumine Hall|Fire Spring",
                "Party: Ness|Paula|Jeff|Poo",
                "Key Items: Franklin Badge|Pencil Eraser|Eraser Eraser|Key to the Tower|Shyness Book|Hawk Eye|Carrot Key|Tendakraut|Diamond|Signed Banana|Bicycle|Town Map|Sound Stone|King Banana|Yogurt Machine|Zombie Paper|Hieroglyph Copy|Tiny Ruby|Backstage Pass|Suporma|Bad Key Machine",
                "Bosses: Frank|Titanic Ant|Boogey Tent|Mr. Carpainter|Mondo Mole|Trillionage Sprout|Guardian Digger|Belch|Master Belch|Shrooom!|Plague Rat of Doom|Thunder and Storm|Kraken|Electro Specter|Carbon Dog|Ness's Nightmare|Heavily Armed Pokey|Giygas"),

            S("snes-som", "SNES", "Secret of Mana",
                "Mana Seeds: Mana Seeds*8",
                "Elementals: Undine|Gnome|Sylphid|Salamando|Shade|Lumina|Luna|Dryad",
                "Weapons: Sword|Spear|Axe|Whip|Bow|Boomerang|Glove|Mana Sword",
                "Key Items: Flammie Drum|Moogle Belt|Midge Mallet|Sea Hare's Tail|Gold Tower Key|Cannon Travel Pass|Mana Fortress Access",
                "Bosses: Mantis Ant|Tropicallo|Spikey Tiger|Tonpole|Fire Gigas|Jabberwocky|Spring Beak|Frost Gigas|Snap Dragon|Mech Rider|Doom's Wall|Hexas|Kettle Kin|Dark Lich|Mana Beast"),

            S("snes-ffmq", "SNES", "Final Fantasy Mystic Quest",
                "Crystals: Earth Crystal|Water Crystal|Fire Crystal|Wind Crystal",
                "Coins & Crests: Sand Coin|River Coin|Sun Coin|Sky Coin|Libra Crest|Gemini Crest|Mobius Crest",
                "Key Items: Elixir|Tree Wither|Wakewater|Venus Key|Multi-Key|Mask|Magic Mirror|Thunder Rock|Captain's Cap|Dragon Claw",
                "Weapons: Steel Sword|Knight Sword|Excalibur|Axe|Battle Axe|Giant's Axe|Cat Claw|Charm Claw|Bomb|Jumbo Bomb|Mega Grenade",
                "Spells: Exit|Cure|Heal|Life|Quake|Blizzard|Fire|Aero|Thunder|White|Meteor|Flare",
                "Bosses: Behemoth|Minotaur|Flamerus Rex|Ice Golem|Dualhead Hydra|Gidrah|Dullahan|Pazuzu|Skullrus Rex|Stone Golem|Twinhead Wyvern|Zuh|Dark King"),

            S("snes-illusion-of-gaia", "SNES", "Illusion of Gaia",
                "Mystic Statues: Mystic Statues*6",
                "Red Jewels: Red Jewels*50",
                "Forms: Will|Freedan|Shadow",
                "Key Items: Herb|Diamond Block|Wind Melody|Lola's Letter|Journal|Crystal Ring|Apple|Elevator Key|Mine Key 1|Mine Key 2|Memory Melody|Teapot|Black Glasses|Gorgon Flower|Hieroglyph|Aura|Letter from Lance|Necklace Stones|Will|Father's Journal|Magic Dust|Blue Journal",
                "Areas: South Cape|Edward's Castle|Itory Village|Inca Ruins|Diamond Mine|Sky Garden|Seaside Palace|Mu|Angel Village|Watermia|Great Wall|Euro|Mt. Kress|Native Village|Ankor Wat|Dao|Pyramid|Babel Tower|Jeweler's Mansion"),

            S("snes-lufia2", "SNES", "Lufia II: Rise of the Sinistrals",
                "Party: Maxim|Selan|Guy|Artea|Dekar|Tia|Lexis",
                "Capsule Monsters: Jelze|Flash|Gusto|Zeppy|Darbi|Sully|Blaze",
                "Key Items: Dual Blade|Engine|Ancient Key|Treadmill Key|Light Sword|Zirco Sword|Submarine|Airship|Providence",
                "Sinistrals: Gades|Amon|Erim|Daos"),

            S("snes-bof1", "SNES", "Breath of Fire",
                "Party: Ryu|Nina|Bo|Karn|Gobi|Ox|Bleu|Mogu",
                "Dragon Keys: Goddess Key|Dragon Tear|Rainbow Stone|Light Key|Dark Key",
                "Key Items: Lute|Hunter's Charm|Dragon Blade|Pass|Flute|Ship|Great Bird",
                "Bosses: Pog|Gremlin|Mote|Wisp|Gramor|Mogu's Nightmare|Jade|Zog|Myria"),

            S("snes-bof2", "SNES", "Breath of Fire II",
                "Party: Ryu|Bow|Rand|Katt|Nina|Sten|Jean|Spar|Bleu",
                "Shamans: Shin|Seso|Mina|Sana|Aura|Solo",
                "Key Items: Rune|Great Bird|Township|Dragon Tear|Flute|Fahl's Medal|Prism|Angel Wing",
                "Bosses: Barubary|Augus|Mad Hare|Shupkay|Habaruku|Deathevan"),

            S("snes-soe", "SNES", "Secret of Evermore",
                "Alchemy: Flash|Hard Ball|Acid Rain|Atlas|Barrier|Call Bead|Cure|Defend|Double Drain|Drain|Energize|Escape|Explosion|Fireball|Force Field|Heal|Lance|Levitate|Lightning Storm|Miracle Cure|Nitro|One Up|Reflect|Regrowth|Revealer|Slow Burn|Speed|Sting|Stop|Crush",
                "Weapons: Bone Crusher|Gladiator Sword|Crusader Sword|Neutron Blade|Spider's Claw|Bronze Axe|Knight Basher|Atom Smasher|Horn Spear|Bronze Spear|Lance|Laser Lance",
                "Bosses: Thraxx|Magmar|Megataur|Vigor|Aquagoth|Rimsala|Coleoptera|Mungola|Verminator|Carltron"),

            S("snes-soulblazer", "SNES", "Soul Blazer",
                "Key Items: Flame Pillar|Light Arrow|Magic Bell|Spirit Sword|Zantetsu Sword|Soul Blade|Phoenix|Dream Rod|Goat's Food|Harp String|Air Herb|Mushroom Shoes|Village Flame|Thunder Ring|Mobile Key|Red-Hot Mirror|Red-Hot Ball|Red-Hot Stick",
                "Worlds: Grass Valley|GreenWood|St. Elles|Mountain of Souls|Dr. Leo's Lab|Magridd Castle|World of Evil",
                "Bosses: Metal Mantis|Poseidon|Tin Doll|Demon Bird|Solid Arm|Deathtoll"),

            S("snes-dq5", "SNES", "Dragon Quest V",
                "Zenithian Gear: Zenithian Sword|Zenithian Armour|Zenithian Helm|Zenithian Shield",
                "Key Items: Circle of Water|Circle of Fire|Golden Orb|Magic Carpet|Flying Carpet|Sabrecub|Faerie Horn|Gold Orb|Ship|Flying Bed",
                "Chapters: Childhood|Slavery|Gotha|Choose a Bride|Petrified|Reunion|Nadiria",
                "Bosses: Slon|Kon|Ladja|Bishop Ladja|Grandmaster Nimzo"),

            S("snes-dq6", "SNES", "Dragon Quest VI",
                "Party: Hero|Carver|Milly|Ashlynn|Nevan|Terry|Amos|Goowain",
                "Key Items: Mirror of Ra|Sphere of Silence|Dream Dew|Wagon|Flying Bed|Sacred Amulet|Zenithian Sword|Zenithian Armour|Zenithian Helm|Zenithian Shield",
                "Bosses: Murdaw|Jamirus|Gracos|Mudo|Dhuran|Deathtamoor|Nokturnus"),

            // ═══════════════════════════ PS1 ═══════════════════════════
            S("ps1-ff7", "PS1", "Final Fantasy VII",
                "Characters: Cloud|Barret|Tifa|Aerith|Red XIII|Yuffie|Cait Sith|Vincent|Cid",
                "Summon Materia: Choco/Mog|Shiva|Ifrit|Ramuh|Titan|Odin|Leviathan|Bahamut|Kujata|Alexander|Phoenix|Neo Bahamut|Hades|Typhon|Bahamut ZERO|Knights of the Round",
                "Key Items: Keystone|Black Materia|Huge Materia*4|Tiny Bronco|Buggy|Highwind|Submarine|Key to Sector 5|Basement Key|Member Card|Midgar Parts",
                "Ultimate Weapons: Ultima Weapon|Conformer|Death Penalty|Missing Score|Premium Heart|Limited Moon|HP Shout|Apocalypse|Venus Gospel",
                "Bosses: Air Buster|Reno|Rufus|Jenova BIRTH|Demon Gate|Jenova LIFE|Carry Armor|Diamond Weapon|Jenova SYNTHESIS|Safer Sephiroth"),

            S("ps1-ff8", "PS1", "Final Fantasy VIII",
                "Guardian Forces: Quetzalcoatl|Shiva|Ifrit|Siren|Brothers|Diablos|Carbuncle|Leviathan|Pandemona|Cerberus|Alexander|Doomtrain|Bahamut|Cactuar|Tonberry|Eden",
                "Characters: Squall|Zell|Quistis|Selphie|Irvine|Rinoa|Edea|Seifer",
                "Key Items: Occult Fan I|Occult Fan II|Occult Fan III|Occult Fan IV|Battle Meter|Girl Next Door|Combat King|Weapons Monthly|Pet Pals|Ragnarok",
                "Discs: Disc 1 — Dollet & Timber|Disc 2 — Galbadia Garden|Disc 3 — Esthar & Lunatic Pandora|Disc 4 — Ultimecia's Castle"),

            S("ps1-ff9", "PS1", "Final Fantasy IX",
                "Characters: Zidane|Vivi|Garnet|Steiner|Freya|Quina|Eiko|Amarant",
                "Eidolons: Shiva|Ifrit|Ramuh|Atomos|Odin|Leviathan|Bahamut|Ark|Fenrir|Carbuncle|Phoenix|Madeen",
                "Collectables: Stellazzio*13|Chocographs*24|Mognet Letters*13",
                "Key Items: Dagger's Pendant|Gysahl Greens|Dead Pepper|Blue Narciss|Hilda Garde I|Hilda Garde III|Invincible|Supersoft|Rat Tail|Aquamarine|Holy Bell",
                "Bosses: Plant Brain|Black Waltz 3|Gizamaluke|Beatrix|Ralvurahva|Ark|Hilgigars|Earth Guardian|Meltigemini|Silver Dragon|Kuja|Trance Kuja|Necron"),

            S("ps1-fft", "PS1", "Final Fantasy Tactics",
                "Chapters: Chapter 1 — The Meager|Chapter 2 — The Manipulator and the Subservient|Chapter 3 — The Valiant|Chapter 4 — Somebody to Love",
                "Zodiac Stones: Zodiac Stones*12",
                "Special Characters: Ramza|Delita|Agrias|Mustadio|Rafa|Malak|Orlandeau|Beowulf|Reis|Construct 8|Cloud|Byblos|Meliadoul|Boco",
                "Jobs: Squire|Chemist|Knight|Archer|Monk|Priest|Wizard|Time Mage|Summoner|Thief|Mediator|Oracle|Geomancer|Lancer|Samurai|Ninja|Calculator|Bard|Dancer|Mime",
                "Bosses: Algus|Gafgarion|Wiegraf|Velius|Zalmour|Queklain|Adrammelech|Hashmalum|Altima"),

            S("ps1-chrono-cross", "PS1", "Chrono Cross",
                "Dragon Relics: Red Relic|Blue Relic|Green Relic|Yellow Relic|Black Relic|White Relic",
                "Key Items: Astral Amulet|Dragon Tear|Shark Tooth|Ice Breath|Frozen Flame|Time Egg|Chrono Cross|Mastermune|Einlanzer|Rainbow Shell|Dragoon's Honor|Manor Key",
                "Characters: Serge|Kid|Lynx|Glenn|Razzly|Harle|Norris|Guile|Nikki|Pierre|Fargo|Karsh|Zoah|Orcha|Luccia|Poshul|Leena|Radius|Starky|Sprigg",
                "Bosses: Solt & Peppor|Karsh|Beach Bum|Hydra|Sun of a Gun|Lynx|Dario|Miguel|Garai|Dead Head|Terra Tower|Time Devourer"),

            S("ps1-lod", "PS1", "The Legend of Dragoon",
                "Dragoon Spirits: Red-Eyed Dragoon|Jade Dragoon|Blue-Sea Dragoon|Violet Dragoon|Silver Dragoon|Golden Dragoon|Darkness Dragoon|Divine Dragoon",
                "Party: Dart|Lavitz|Shana|Rose|Haschel|Albert|Meru|Kongol|Miranda",
                "Stardust: Stardust*50",
                "Discs: Disc 1 — Serdio|Disc 2 — Tiberoa|Disc 3 — Mille Seseau|Disc 4 — Death Frontier & Divine Tree",
                "Bosses: Urobolus|Fruegel|Drake the Bandit|Virage|Lenus|Regole|Grand Jewel|Kamuy|Lloyd|Zieg|Melbu Frahma"),

            S("ps1-xenogears", "PS1", "Xenogears",
                "Characters: Fei|Elly|Citan|Bart|Rico|Billy|Maria|Chu-Chu|Emeralda",
                "Gears: Weltall|Vierge|Heimdal|Brigandier|Stier|Renmazuo|Seibzehn|Crescens|Andvari|Xenogears",
                "Key Items: Ether Doubler|Gold Ring|Silver Ring|Yggdrasil|Goliath|Excalibur|Omnigear Parts",
                "Discs: Disc 1 — Lahan to Solaris|Disc 2 — Shevat to Deus"),

            S("ps1-suikoden", "PS1", "Suikoden",
                "Stars of Destiny: Stars of Destiny*108",
                "True Runes: Soul Eater|Gate Rune|Dragon Rune|Night Rune",
                "Key Items: Fire Spear|Mirror Shield|Blinking Mirror|Lion Statue|Name Plate|Opal|Dragon Flute|Wind Mirror|Antique Vase",
                "Bosses: Kwanda Rosman|Milich Oppenheimer|Kasim Hazil|Teo McDohl|Neclord|Windy|Barbarossa"),

            S("ps1-suikoden2", "PS1", "Suikoden II",
                "Stars of Destiny: Stars of Destiny*108",
                "True Runes: Bright Shield Rune|Black Sword Rune|Beast Rune",
                "Key Items: Sindar Treasure|Star Dragon Sword|Blinking Mirror|Fog Ship|Clay Figure|Gate Rune Sword",
                "Bosses: Luca Blight|Neclord|Beast Rune|Jowy|Lord Riou's Rival"),

            S("ps1-bof3", "PS1", "Breath of Fire III",
                "Party: Ryu|Nina|Rei|Momo|Garr|Peco",
                "Dragon Genes: Flame|Thorn|Shadow|Fear|Force|Grace|Eldritch|Mutant|Trance|Miracle|Infinity|Reverse|Radiance|Animal|Fusion|Hurt|Frost|Shed|Insect|Myth",
                "Masters: Bunyan|Mygas|Durandal|D'Lonzo|Emitai|Hondara|Yggdrasil|Giotto|Deis|Fahl|Lang|Lee|Meryleep|Ladon",
                "Bosses: Nue|Balio and Sunder|Claw|Stallion|Gaist|Elder|Myria"),

            S("ps1-bof4", "PS1", "Breath of Fire IV",
                "Party: Ryu|Nina|Cray|Ershin|Scias|Ursula",
                "Dragons: Sheng Long|Shin Long|Yorae Dragon|Sanjiyan|Astral|Behemoth|Punisher|Kaiser",
                "Masters: Kahn|Bunyan|Una|Marlok|Ekaru|Abbess|Stoll|Njomo|Rwolf|Sana|Deis",
                "Bosses: Gendo Puppet|Marda the Ancient|Rasso|Yuna|Fou-Lu"),

            S("ps1-wildarms", "PS1", "Wild Arms",
                "Party: Rudy|Jack|Cecilia",
                "Tools: Bomb|Grapple Hook|Radar|Hand Bomb|Earth Hammer|Mini-Shovel|Duplicator|Hanpan|Pocket Watch|Tindercrest|Change Crest|Freeze Crest|Teardrop",
                "Guardians: Zephyr|Moor Gault|Stare Roe|Dan Dairam|Grudiev|Fengalon|Lucied|Justine|Schturdark|Raftina|Mother|Zeldukes",
                "Bosses: Zed|Belselk|Alhazad|Lady Harken|Boomerang|Zeikfried|Motherfind|Mother"),

            S("ps1-sotn", "PS1", "Castlevania: Symphony of the Night",
                "Movement Relics: Soul of Bat|Fire of Bat|Echo of Bat|Force of Echo|Soul of Wolf|Power of Wolf|Skill of Wolf|Form of Mist|Power of Mist|Gas Cloud|Gravity Boots|Leap Stone|Merman Statue",
                "Key Relics: Cube of Zoe|Spirit Orb|Holy Symbol|Faerie Scroll|Jewel of Open|Eye of Vlad|Heart of Vlad|Tooth of Vlad|Rib of Vlad|Ring of Vlad|Holy Glasses|Silver Ring|Gold Ring",
                "Cards: Bat Card|Ghost Card|Faerie Card|Demon Card|Sword Card|Sprite Card|Nose Devil Card",
                "Bosses: Slogra and Gaibon|Doppelganger 10|Hippogryph|Minotaur and Werewolf|Scylla|Granfaloon|Olrox|Succubus|Karasuman|Death|Medusa|Creature|Akmodan II|Darkwing Bat|Galamoth|Beelzebub|Doppelganger 40|Shaft|Dracula"),

            S("ps1-dq7", "PS1", "Dragon Quest VII",
                "Party: Hero|Maribel|Kiefer|Ruff|Aira|Mervyn",
                "Shards: Tablet Fragments*40",
                "Key Items: Magic Key|Final Key|Thief's Key|Sacred Amulet|Shimmering Dress|Silver Orb|Wagon|Ship",
                "Bosses: Antaeus|Gracos|Orgodemir|Demon King"),

            S("ps1-grandia", "PS1", "Grandia",
                "Party: Justin|Sue|Feena|Gadwin|Rapp|Milda|Guido|Liete",
                "Key Items: Spirit Stone|Icarian Wing|Letter of Introduction|Mogay Lamp|Pot of Icarus|Sword of Light|Angel Wings",
                "Chapters: Parm|New Parm|Dight|End of the World|Zil Padon|Laine|Alent|Savanna|Twin Towers",
                "Bosses: Leen|Gaia Spirit|Mullen|Baal|Gaia"),

            S("ps1-lunar", "PS1", "Lunar: Silver Star Story Complete",
                "Party: Alex|Luna|Nash|Ramus|Jessica|Kyle|Mia|Nall|Laike",
                "Dragons: White Dragon|Red Dragon|Blue Dragon|Black Dragon",
                "Key Items: Dragon Diamond|Dragon Ring|Dragonmaster Sword|Althena's Sword|Grindery Pass|Blue Dragon Wings",
                "Bosses: Laike|Royal Guard|Xenobia|Phacia|Royce|Ghaleon"),

            S("ps1-so2", "PS1", "Star Ocean: The Second Story",
                "Party: Claude|Rena|Celine|Ashton|Opera|Ernest|Precis|Bowman|Dias|Leon|Noel|Chisato|Welch",
                "Key Items: Sacred Orb|Eye of Truth|Passport|Cooking Set|Orchestra Ticket|Mirror|Lezard Flask",
                "Discs: Disc 1 — Expel|Disc 2 — Nede",
                "Bosses: Allen|Gamgee|Cyril|Shin|Indalecio|Gabriel"),

            S("ps1-vagrant-story", "PS1", "Vagrant Story",
                "Key Items: Rood Inverse|Blood-Sin|Gran Grimoire Key|Rosencrantz's Key|Snowfly Forest Pass|Grimoire",
                "Areas: Wine Cellar|Catacombs|Sanctum|Abandoned Mines|Town Centre|Temple of Kiltia|Snowfly Forest|Undercity|Limestone Quarry|Iron Maiden|Great Cathedral|Forgotten Pathway",
                "Bosses: Minotaur|Wyvern|Dullahan|Golem|Dragon|Snow Dragon|Duane|Rosencrantz|Grissom|Sydney|Guildenstern"),

            S("ps1-parasite-eve", "PS1", "Parasite Eve",
                "Days: Day 1 — Carnegie Hall|Day 2 — Central Park|Day 3 — Museum|Day 4 — Chinatown|Day 5 — Hospital|Day 6 — Warehouse|Chrysler Building",
                "Key Items: Museum Key|Chinatown Key|Hospital Key|Soul of the Mitochondria|Officer's ID|Warehouse Key",
                "Bosses: Eve|T-Rex|Sewer Beast|Sea Creature|Sample 3|Ultimate Being"),

            // ═══════════════════════════ PS2 ═══════════════════════════
            S("ps2-kh1", "PS2", "Kingdom Hearts",
                "Worlds: Destiny Islands|Traverse Town|Wonderland|Olympus Coliseum|Deep Jungle|Agrabah|Monstro|Atlantica|Halloween Town|Neverland|Hollow Bastion|End of the World|100 Acre Wood",
                "Keyblades: Kingdom Key|Three Wishes|Crabclaw|Pumpkinhead|Fairy Harp|Wishing Star|Spellbinder|Metal Chocobo|Olympia|Lionheart|Lady Luck|Divine Rose|Oathkeeper|Oblivion|Ultima Weapon",
                "Magic: Fire|Blizzard|Thunder|Cure|Gravity|Stop|Aero",
                "Summons: Simba|Genie|Bambi|Dumbo|Tinker Bell|Mushu",
                "Abilities: High Jump|Glide|Superglide|Dodge Roll|Mermaid Kick|Combo Plus|Second Chance",
                "Trinities & Pages: Trinity Marks*25|Torn Pages*5|Dalmatians*99",
                "Bosses: Darkside|Guard Armor|Opposite Armor|Trickmaster|Cave of Wonders Guardian|Parasite Cage|Ursula|Oogie Boogie|Captain Hook|Dragon Maleficent|Riku-Ansem|Behemoth|Chernabog|Ansem"),

            S("ps2-kh2", "PS2", "Kingdom Hearts II",
                "Drive Forms: Valor Form|Wisdom Form|Limit Form|Master Form|Final Form|Anti Form",
                "Magic: Fire|Blizzard|Thunder|Cure|Magnet|Reflect",
                "Summons: Chicken Little|Genie|Stitch|Peter Pan",
                "Worlds: Twilight Town|Hollow Bastion|Land of Dragons|Beast's Castle|Olympus Coliseum|Disney Castle|Timeless River|Port Royal|Agrabah|Halloween Town|Pride Lands|Space Paranoids|The World That Never Was|Atlantica|100 Acre Wood",
                "Proofs & Reports: Proof of Connection|Proof of Nonexistence|Proof of Peace|Ansem Reports*13",
                "Growth Abilities: High Jump|Quick Run|Dodge Roll|Aerial Dodge|Glide",
                "Organization XIII: Xemnas|Xigbar|Xaldin|Vexen|Lexaeus|Zexion|Saix|Axel|Demyx|Luxord|Marluxia|Larxene|Roxas"),

            S("ps2-khcom", "PS2", "Kingdom Hearts Re:Chain of Memories",
                "Worlds: Traverse Town|Wonderland|Olympus Coliseum|Monstro|Agrabah|Halloween Town|Atlantica|Neverland|Hollow Bastion|100 Acre Wood|Twilight Town|Destiny Islands|Castle Oblivion",
                "Key Cards: Key of Beginnings|Key of Guidance|Key to Truth|Key to Rewards",
                "Summon Cards: Simba|Genie|Bambi|Dumbo|Tinker Bell|Mushu|Cloud",
                "Bosses: Guard Armor|Trickmaster|Parasite Cage|Hades|Jafar|Oogie Boogie|Ursula|Captain Hook|Dragon Maleficent|Riku Replica|Larxene|Vexen|Lexaeus|Axel|Marluxia"),

            S("ps2-ffx", "PS2", "Final Fantasy X",
                "Aeons: Valefor|Ifrit|Ixion|Shiva|Bahamut|Yojimbo|Anima|Magus Sisters",
                "Party: Tidus|Yuna|Auron|Wakka|Lulu|Kimahri|Rikku",
                "Celestial Weapons: Caladbolg|Nirvana|Masamune|World Champion|Onion Knight|Spirit Lance|Godhand",
                "Crests & Sigils: Sun Crest|Sun Sigil|Moon Crest|Moon Sigil|Jupiter Crest|Jupiter Sigil|Mars Crest|Mars Sigil|Saturn Crest|Saturn Sigil|Venus Crest|Venus Sigil|Mercury Crest|Mercury Sigil",
                "Temples: Besaid|Kilika|Djose|Macalania|Bevelle|Zanarkand",
                "Al Bhed Primers: Al Bhed Primers*26",
                "Bosses: Sinspawn Ammes|Klikk|Tros|Sinspawn Echuilles|Sinspawn Geneaux|Extractor|Spherimorph|Crawler|Seymour|Evrae|Seymour Natus|Biran and Yenke|Seymour Flux|Sanctuary Keeper|Yunalesca|Sin|Seymour Omnis|Braska's Final Aeon|Yu Yevon"),

            S("ps2-ffx2", "PS2", "Final Fantasy X-2",
                "Dresspheres: Gunner|Warrior|Thief|Black Mage|White Mage|Alchemist|Songstress|Dark Knight|Berserker|Samurai|Gun Mage|Lady Luck|Mascot|Trainer|Festivalist|Psychic",
                "Chapters: Chapter 1|Chapter 2|Chapter 3|Chapter 4|Chapter 5",
                "Garment Grids: First Steps|Still of Night|Vanguard|Healing Light|Heart Reborn|Mounted Assault|Chaos Maelstrom|Highroad Winds|Pride of the Sword|Treasure Hunt|Unerring Path|Menace of the Deep|Higher Power|Covenant of Growth|Ray of Hope",
                "Bosses: Leblanc|Boris|Chac|Angra Mainyu|Garik|Baralai|Trema|Vegnagun|Shuyin"),

            S("ps2-ff12", "PS2", "Final Fantasy XII",
                "Espers: Belias|Mateus|Adrammelech|Zalera|Cuchulainn|Zeromus|Exodus|Shemhazai|Hashmal|Famfrit|Chaos|Ultima|Zodiark",
                "Party: Vaan|Balthier|Fran|Basch|Ashe|Penelo",
                "Key Items: Goddess's Magicite|Dawn Shard|Sword of Kings|Treaty-Blade|Sunstone|Black Orb|Barheim Key|Dusty Letter|Chops of Exploration",
                "Bosses: Firemane|Judge Ghis|Tiamat|Mandragoras|Vossler|Judge Bergan|Elder Wyrm|Daedalus|Rafflesia|Hydro|Doctor Cid|Gabranth|Vayne|The Undying"),

            S("ps2-dq8", "PS2", "Dragon Quest VIII",
                "Party: Hero|Yangus|Jessica|Angelo|Morrie|Red",
                "Mini Medals: Mini Medals*110",
                "Key Items: Magic Key|Thief's Key|Ultimate Key|Godbird's Soulstone|Venus' Tear|Sacred Sword of Light|Ship|Sabrecub|Argon Heart|Moonshadow Harp",
                "Bosses: Geyzer|Dhoulmagus|Trap Box|Marcello|Rhapthorne|Captain Crow|Dragovian Lord"),

            S("ps2-p3", "PS2", "Persona 3 FES",
                "Tartarus Blocks: Thebel|Arqa|Yabbashah|Tziah|Harabah|Adamah|Monad",
                "Full Moon Shadows: Priestess|Emperor and Empress|Hierophant|Lovers|Chariot and Justice|Hermit|Fortune and Strength|Hanged Man|Judgement",
                "Party: Yukari|Junpei|Mitsuru|Akihiko|Fuuka|Aigis|Ken|Shinjiro|Koromaru",
                "Social Links: Maxed Social Links*22"),

            S("ps2-p4", "PS2", "Persona 4",
                "Dungeons: Yukiko's Castle|Steamy Bathhouse|Marukyu Striptease|Void Quest|Secret Laboratory|Heaven|Magatsu Inaba|Yomotsu Hirasaka|Hollow Forest|Void Quest Depths",
                "Party: Yosuke|Chie|Yukiko|Kanji|Rise|Teddie|Naoto",
                "Social Links: Maxed Social Links*21",
                "Bosses: Shadow Yosuke|Shadow Chie|Shadow Yukiko|Shadow Kanji|Shadow Rise|Shadow Teddie|Shadow Mitsuo|Shadow Naoto|Kunino-sagiri|Ameno-sagiri|Kusumi-no-Okami|Izanami"),

            S("ps2-nocturne", "PS2", "Shin Megami Tensei: Nocturne",
                "Magatama: Magatama*25",
                "Fiends: Matador|Daisoujou|Hell Biker|White Rider|Red Rider|Black Rider|Pale Rider|Trumpeter",
                "Candelabra: Candelabra*6",
                "Bosses: Forneus|Thor|Mizuchi|Mot|Mara|Girimehkala|Aciel|Skadi|Noah|Beelzebub|Metatron|Kagutsuchi|Lucifer"),

            S("ps2-shadow-hearts-2", "PS2", "Shadow Hearts: Covenant",
                "Party: Yuri|Karin|Gepetto|Blanca|Joachim|Lucia|Anastasia|Kurando",
                "Fusion Souls: Amon|Atman|Astaroth|Seraphic Radiance|Dark Seraphim",
                "Key Items: Pocket Watch|Soul Gauge|Mirror|Emigre Document|Ring of Thorns",
                "Bosses: Rasputin|Nicolai|Kato|Astaroth|Grand Papillon|Lenny|Ouka"),

            S("ps2-dark-cloud2", "PS2", "Dark Cloud 2",
                "Chapters: Palm Brinks|Sindain|Balance Valley|Veniccio|Heim Rada|Starlight Canyon|Mount Gundor|Ocean's Roar Cave",
                "Party: Max|Monica|Steve (Ridepod)",
                "Key Items: Atlamillia|Camera|Fishing Rod|Spheda Club|Ridepod|Invention Scoops",
                "Bosses: Linda|Gaspard|Halloween|Jurak|Shigura|Mecha Minotaur|Griffon|Emperor Griffon"),

            S("ps2-rogue-galaxy", "PS2", "Rogue Galaxy",
                "Party: Jaster|Kisala|Simon|Steve|Lilika|Jupis|Deego|Zegram",
                "Planets: Rosa|Zerard|Juraika|Vedan|Mariglenn|Ghost Ship|Eigerd",
                "Key Items: Desert Seagull|Hunter License|Star Chart|Insectron Pass|Factory Blueprint",
                "Bosses: Zombie Soldier|Grand Dalham|Gale|Valkog|Mother Seedling|Ghost Ship Captain|Ladakh"),

            S("ps2-suikoden5", "PS2", "Suikoden V",
                "Stars of Destiny: Stars of Destiny*108",
                "True Runes: Sun Rune|Dawn Rune|Twilight Rune|Circle Rune",
                "Key Items: Falena Crest|Lordlake Key|Ashtwal Mountains Pass|Stone Tablet",
                "Bosses: Childerich|Gizel|Dolph|Euram|Marscal Godwin|Sun Rune"),
        };
    }
}
