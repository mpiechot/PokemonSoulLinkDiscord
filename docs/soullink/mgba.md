# mGBA Ruby read-only integration

Use mGBA with its built-in Lua socket support. Start the C# client first; load a real save in mGBA; then load `integration/mgba/soullink.lua` in Tools → Scripting. Default bridge port is 5091. For multiple local clients set `SOULLINK_PORT` in the scripting console before loading the script, and start the corresponding client with `-BridgePort`.

## Reference profile and validation boundary

The profile `ruby-axvd-reference-v1` recognizes the twelve-byte `POKEMON RUBY` title and `AXVD` game code without requiring a ROM CRC. **Recognition is not proof of layout compatibility.** Its fixed addresses follow Ruby-family source/reference layouts. They have not been validated against the user's concrete randomized AXVD ROM or a real emulator memory fixture in this environment.

| Field | Address / offset | Source |
| --- | --- | --- |
| Party count | `0x03004350` | mGBA official Ruby example; Ruby common symbols |
| Party | `0x03004360`, six 100-byte structures | mGBA official Ruby example; `include/pokemon.h` |
| PC storage | `0x020300A0`, box array at +4, 14×30×80 bytes | Ruby `PokemonStorage` reference layout |
| SaveBlock1 | `0x02025734`, map group +4, number +5 | Ruby `include/global.h` |
| SaveBlock2 | `0x02024EA4`, play hours +0x0E, minutes +0x10, seconds +0x11 | Ruby `include/global.h` |
| Main | `0x03001770`, battle flag at +0x43D, bit 1 | Ruby common symbols and `include/main.h` |
| ROM title / code | `0x080000A0` / `0x080000AC` | GBA ROM header |

Before accepting a frame, the bridge validates title/code, party count, current box and playtime ranges. The client decrypts every occupied Pokémon, verifies its checksum, species and party stat ranges, and rejects incomplete data. The server additionally checks identities, unique slots and field ranges. These structural checks help diagnose incorrect layouts; they are **not a substitute for comparing all party/box/map data against a real save**.

If your ROM uses relocated structures, configure a verified layout before loading the Lua file:

```lua
SOULLINK_PROFILE = {
    id = "my-verified-axvd-layout",
    party = 0x03004360,
    partyCount = 0x03004350,
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
- [Ruby species constants](https://github.com/pret/pokeruby/blob/master/include/constants/species.h)
- [Ruby region sections](https://github.com/pret/pokeruby/blob/master/src/data/region_map/region_map_sections.json)

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
