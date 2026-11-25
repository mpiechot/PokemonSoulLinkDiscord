# Pokémon editions in mGBA and SoulLink

mGBA emulates Game Boy, Game Boy Color and Game Boy Advance cartridges. The list below follows the original cartridge platforms. It is a hardware compatibility assessment, with no individual ROM playtests. Nintendo DS support exists in the separate experimental medusa branch, which mGBA documents as inactive and poorly compatible. [mGBA compatibility FAQ](https://mgba.io/faq.html#how-good-is-compatibility)

| Generation | Original hardware | Main-series editions |
| --- | --- | --- |
| 1 | Game Boy / Game Boy Color | Rot, Blau and Gelb. Japan also received Grün and a distinct Japanese Blau release. [Nintendo Europe](https://www.nintendo.com/de-de/News/2016/August/Was-sind-Pokemon-Erfahre-mehr-uber-das-weltweite-Phanomen-in-unseren-News--1128960.html), [Nintendo Japan](https://www.nintendo.co.jp/kids/sp/160224/pokemon/index.html) |
| 2 | Game Boy / Game Boy Color | Gold and Silber support both systems. Kristall requires Game Boy Color. [Nintendo cartridge catalog](https://www.nintendo.co.jp/n02/dmg/index.html) |
| 3 | Game Boy Advance | Rubin, Saphir, Smaragd, Feuerrot and Blattgrün. [Nintendo Europe](https://www.nintendo.com/de-de/News/2016/August/Was-sind-Pokemon-Erfahre-mehr-uber-das-weltweite-Phanomen-in-unseren-News--1128960.html) |

Pokémon Trading Card Game, the Japanese Pokémon Card GB2, Pokémon Puzzle Challenge and Pokémon Pinball are additional GB/GBC titles. Pokémon Pinball: Rubin & Saphir and Pokémon Mystery Dungeon: Team Rot are GBA titles. Mystery Dungeon: Team Blau needs a DS emulator, as do Diamant, Perl, Platin, HeartGold, SoulSilver, Schwarz, Weiß and their sequels. [The Pokémon Company's game catalog](https://www.pokemon.co.jp/game/), [Nintendo's Mystery Dungeon developer interview](https://iwataasks.nintendo.com/interviews/3ds/pokemon-mysterydungeon-gti/0/0/)

## SoulLink integration

The supported reference profile is German Pokémon Rubin with cartridge code `AXVD` and revisions 0 and 1. It has a checked memory layout, with prior live validation on a randomized revision 0 save and synthetic Windows smoke tests for both revisions. A modified ROM may relocate structures even when its header matches. See the [Ruby bridge guide](mgba.md#reference-profile-and-validation-boundary).

- **Gen 3:** Saphir is the closest candidate given the shared engine and Hoenn setting. Smaragd also uses Hoenn, but its save blocks are reached by pointer. Feuerrot and Blattgrün need Kanto map data and their own game-specific layout. For example, the decompilations show fixed save blocks in Rubin, pointers in Smaragd and Feuerrot, and different item offsets. [Rubin save structures](https://github.com/pret/pokeruby/blob/master/include/global.h#L648), [Smaragd save structures](https://github.com/pret/pokeemerald/blob/master/include/global.h#L950), [Feuerrot save structures](https://github.com/pret/pokefirered/blob/master/include/global.h#L727)
- **Gen 1 and Gen 2:** Their WRAM party records and separate nickname and trainer arrays require different decoders. The Gen 3 parser reads records with personality, trainer ID, checksum and encrypted substructures. A normalized snapshot can feed the same SoulLink rules once each adapter parses its native format. [Rot party layout](https://github.com/pret/pokered/blob/master/ram/wram.asm#L1620), [Kristall party layout](https://github.com/pret/pokecrystal/blob/master/ram/wram.asm#L3276), [Rubin Pokémon structures](https://github.com/pret/pokeruby/blob/master/include/pokemon.h#L125)

To validate another edition, identify its platform, game code, language and revision before selecting memory offsets. Test roster, identities, map transitions, catches, faints and restart against an actual save. Verify native inventory writes, the original trade scene and sprite slots independently before enabling these features. The [Rubin acceptance checklist](mgba.md#manual-acceptance-with-a-disposable-save-copy) gives the current verification sequence.

## Where to add a profile

1. Register a Lua profile in `integration/mgba/games.lua`. The profile in `integration/mgba/profiles/ruby.lua` owns cartridge recognition, memory reads and native game events. `soullink.lua` handles framing, sessions and reconnects. The packaged player embeds the same registry and Lua profiles in the exported script.
2. Register the corresponding C# profile in `SoulLink/GameProfiles.cs`. It validates and decodes the edition's frames to `GameSnapshot`, checks the same edition when a player joins or submits observations, and provides the edition's route names and cartridge details. For Gen III, reuse the encrypted record decoder where the native structures match.
3. Leave item, overworld and trade capabilities disabled until tested for the new reference layout. Supply the appropriate region map and arena data in the desktop client. The existing Hoenn rendering is in `SoulLink.Player/RubyRunDashboard.cs` and its map modules.

Protocol v1 keeps the existing Rubin field names and stored run values. Later profiles can share the engine's run rules and transaction handling. Gen I and II will also need verification of identity stability and their different battle and inventory features before enabling automation.
