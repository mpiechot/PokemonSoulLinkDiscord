# mGBA Ruby read-only integration

The Windows player bundles a pinned portable mGBA development build with Lua support. Its settings live in the player's app directory and its bridge uses a dedicated loopback port. The existing stand-alone mGBA installation is independent. For developer use, the web client still accepts a manual load of `integration/mgba/soullink.lua` in Tools → Scripting on port 5091.

The redistributed mGBA bundle retains its license and notices. [mGBA source for build 9187](https://github.com/mgba-emu/mgba/tree/e6c17152dec0caaebd8a4b949c96a24d56debf44) · [mGBA build list](https://mgba.io/builds/1/) · [Source for bundled dependencies](https://github.com/mgba-emu/dependencies).

On an interactive Windows desktop, run `./scripts/Test-SoulLinkPlayer.ps1` in PowerShell 7 after publishing to verify actual mGBA startup, docking, undocking and snapshots for both supported revisions. Hosted Windows CI runs the package and embedded-invitation checks with `-PackageOnly`; live emulator startup timed out on that runner.

## Reference profile and validation boundary

The profile `ruby-axvd-reference-v2` recognizes the twelve-byte `POKEMON RUBY` title and `AXVD` game code on German revisions 0 and 1, without requiring a ROM CRC. The fixed addresses come from the German symbol tables. The packaged player received and parsed six party and seventeen boxed Pokémon plus map coordinates from a local copy of the Run 5 randomized revision 0 save. Modified ROMs can still require a different layout.

| Field | Address / offset | Source |
| --- | --- | --- |
| Party count | `0x03004360` | German Ruby symbols |
| Party | `0x03004370`, six 100-byte structures | German Ruby symbols; `include/pokemon.h` |
| PC storage | `0x020300A0`, box array at +4, 14×30×80 bytes | Ruby `PokemonStorage` reference layout |
| SaveBlock1 | `0x02025734`, map group +4, number +5 | Ruby `include/global.h` |
| SaveBlock2 | `0x02024EA4`, play hours +0x0E, minutes +0x10, seconds +0x11 | Ruby `include/global.h` |
| Main | `0x03001770`, battle flag at +0x43D, bit 1 | Ruby common symbols and `include/main.h` |
| Badges | SaveBlock1 flags +0x1220, bits 0x807–0x80E | Ruby `include/global.h` and `include/constants/flags.h` |
| Base types | `0x0820BBE8`, 28-byte records, types +6/+7 | AXVD Randomizer profile and Ruby `include/pokemon.h` |
| Trainers | `0x081FD478`, 40-byte records, gym leaders 265–272 | AXVD Randomizer profile and Ruby trainer definitions |
| ROM title / code | `0x080000A0` / `0x080000AC` | GBA ROM header |

Before accepting a frame, the bridge validates title/code, party count, current box and playtime ranges. The client decrypts every occupied Pokémon, verifies its checksum, species and party stat ranges, and rejects incomplete data. The server additionally checks identities, unique slots and field ranges. These structural checks help diagnose incorrect layouts; they are **not a substitute for comparing all party/box/map data against a real save**.

If your ROM uses relocated structures, configure a verified layout before loading the Lua file:

```lua
SOULLINK_PROFILE = {
    id = "my-verified-axvd-layout",
    party = 0x03004370,
    partyCount = 0x03004360,
    storage = 0x020300A0,
    save1 = 0x02025734,
    save2 = 0x02024EA4,
    main = 0x03001770
}
```

Only use addresses verified for your ROM; these example values repeat the reference profile. The preexisting `PokemonRubyCompatibilityAdapter` deliberately continues reporting DiagnosticOnly until a real AXVD fixture justifies promotion. The bridge makes that reference test possible without silently claiming verified support.

## Parser and catalogs

The parser implements 80-byte BoxPokemon and 100-byte party Pokémon structures, PID-dependent permutations, `PID XOR OT ID` decryption and the decrypted 16-bit checksum. Identity is OT ID + PID. Internal Gen III species numbers differ from National Pokédex IDs (Ralts is 392 internally); generated catalogs preserve the source IDs. Unknown nickname glyphs are shown as replacement characters instead of guessed text. Box level is left unknown.

The current map's normalized region section comes from the generated mapping of all 394 source maps. Catch provenance uses the Pokémon's stored met-area ID, so an animation/map transition cannot accidentally assign a catch to the destination map. Source revisions are pinned in `integration/mgba/sources.json`; regenerate the factual catalogs with `python scripts/update-ruby-catalogs.py`.

Sources:

- [mGBA scripting API](https://mgba.io/docs/scripting.html)
- [mGBA official Pokémon example](https://github.com/mgba-emu/mgba/blob/master/res/scripts/demos/pokemon.lua)
- [mGBA socket example](https://github.com/mgba-emu/mgba/blob/master/res/scripts/demos/sockettest.lua)
- [Ruby Pokémon structures](https://github.com/pret/pokeruby/blob/master/include/pokemon.h)
- [Ruby save structures](https://github.com/pret/pokeruby/blob/master/include/global.h)
- [Ruby main flags](https://github.com/pret/pokeruby/blob/master/include/main.h)
- [Ruby common symbols](https://github.com/pret/pokeruby/blob/master/sym_common.txt)
- [German Ruby revision 0 symbols](https://github.com/pret/pokeruby/blob/78b19905a760095bc45eab2e36906c6e8c208dce/pokeruby_de.sym)
- [German Ruby revision 1 symbols](https://github.com/pret/pokeruby/blob/78b19905a760095bc45eab2e36906c6e8c208dce/pokeruby_de_rev1.sym)
- [Ruby species constants](https://github.com/pret/pokeruby/blob/master/include/constants/species.h)
- [Ruby region sections](https://github.com/pret/pokeruby/blob/master/src/data/region_map/region_map_sections.json)
- [Ruby badge flags](https://github.com/pret/pokeruby/blob/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/include/constants/flags.h)
- [Randomizer AXVD offsets](https://github.com/Ajarmar/universal-pokemon-randomizer-zx/blob/7f00eb866ed35c8fe3963f078b6a2e0979dc2b8c/src/com/dabomstew/pkrandom/config/gen3_offsets.ini)

The dashboard displays the original PokéNav Hoenn map from the local AXVD ROM. The 28×15-tile crop matches the in-game cursor grid. Its palette and compressed tiles are decoded in memory, with no map image included in the package. [Region map source and cursor grid](https://github.com/pret/pokeruby/blob/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/src/region_map.c)

`python scripts/update-ruby-dashboard.py` regenerates data for area positions, species numbers, standard types and standard gym levels. `python scripts/download-ruby-sprites.py` downloads the 386 standard and 386 shiny views from the pinned PokéAPI sprite source for local use under `artifacts/player-dashboard/sprites`. Keep downloaded graphics out of Git and published player packages. [PokéAPI sprite copyright notice](https://github.com/PokeAPI/sprites/blob/c0542f7c51215d9672439012838c99f520596cbd/LICENCE.txt)

## Manual acceptance with a disposable save copy

1. Compare party count, species, nickname, HP and slots against mGBA; inspect stored Pokémon across at least two boxes.
2. Change route and verify raw map group/number and area display.
3. Start with the baseline imported, catch into an open party slot, and verify one event.
4. Fill party, catch directly to PC, and verify one event with its original met area.
5. Move a Pokémon between party/box and rename/evolve it; confirm no extra catch or new identity.
6. Repeat across three players on the same area; expect one completed group and accurate team conflict status.
7. Disconnect/restart client/server without playing; expect a reconciled baseline. Play offline and reconnect; expect OutOfSync instead of replay.
8. Load an older state/reset; confirm rollback detection. Script callbacks and frame/playtime/experience checks provide complementary evidence; same-state reloads cannot always be distinguished.
9. With the optional death rule, observe HP 0 outside battle twice; confirm group death and explicit undo. Keep the default Disabled for rules requiring human confirmation.

No actual ROM/save belongs in source control. Record only a minimal sanitized memory fixture and ROM header/version metadata when validating a layout.
