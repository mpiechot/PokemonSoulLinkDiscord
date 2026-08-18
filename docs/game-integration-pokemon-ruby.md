# Pokémon Ruby memory compatibility

The first game adapter recognizes the German ROM shown in the project
requirements:

- title: `POKEMON RUBY`;
- game code: `AXVD`;
- revision: read from the GBA header;
- CRC32: deliberately ignored, because a randomized ROM changes it.

The adapter is header-based and does not write RAM. It returns a diagnostic-only
profile for `AXVD` until a captured memory fixture validates the live address.
That keeps a randomized ROM recognizable without pretending that an address
from another revision or language is already proven for the target cartridge.

The structural reference comes from the [pret/pokeruby Pokémon header](https://github.com/pret/pokeruby/blob/master/include/pokemon.h):

- `BoxPokemon` is `0x50` bytes;
- a party `Pokemon` is `0x64` bytes;
- the party contains six entries;
- the storage layout contains 14 boxes of 30 `BoxPokemon` entries.

The reference party address `0x03004360` is recorded only as diagnostic metadata
and is sourced from the [Generation III data-structure reference](https://bulbapedia.bulbagarden.net/wiki/Pok%C3%A9mon_data_structure_in_the_GBA).
The address is not exposed as a write target and must be confirmed by an AXVD
fixture before live polling is promoted to `Supported`.

The adapter seam is `IRomCompatibilityAdapter`. A future edition adds another
adapter/profile instead of extending a Ruby-specific CRC or address switch.
