"""Regenerate factual PokéWiki tables from the pinned Ruby source."""

import argparse
import concurrent.futures
import json
import pathlib
import re
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
REVISION = json.loads((ROOT / "integration/mgba/sources.json").read_text())["pokeruby"]
PATHS = (
    "include/constants/species.h", "include/constants/moves.h", "include/constants/items.h",
    "include/constants/pokemon.h", "include/constants/battle_move_effects.h", "src/data/pokemon/base_stats.h",
    "src/data/pokemon/evolution.h", "src/data/pokemon/level_up_learnsets.h",
    "src/data/pokemon/level_up_learnset_pointers.h", "src/data/pokemon/tmhm_learnsets.h",
    "src/data/pokemon/egg_moves.h", "src/data/battle_moves.c",
)

EVOLUTION_METHODS = (
    "FRIENDSHIP", "FRIENDSHIP_DAY", "FRIENDSHIP_NIGHT", "LEVEL", "TRADE", "TRADE_ITEM", "ITEM",
    "LEVEL_ATK_GT_DEF", "LEVEL_ATK_EQ_DEF", "LEVEL_ATK_LT_DEF", "LEVEL_SILCOON", "LEVEL_CASCOON",
    "LEVEL_NINJASK", "LEVEL_SHEDINJA", "BEAUTY",
)
EVOLUTION_ITEMS = {
    "FIRE_STONE": "Feuerstein", "LEAF_STONE": "Blattstein", "MOON_STONE": "Mondstein",
    "SUN_STONE": "Sonnenstein", "THUNDER_STONE": "Donnerstein", "WATER_STONE": "Wasserstein",
    "DEEP_SEA_SCALE": "Abyssplatte", "DEEP_SEA_TOOTH": "Abysszahn", "DRAGON_SCALE": "Drachenhaut",
    "KINGS_ROCK": "King-Stein", "METAL_COAT": "Metallmantel", "UP_GRADE": "Up-Grade",
}


def source(path, local):
    if local:
        return (local / pathlib.Path(path).name).read_text(encoding="utf-8", errors="replace")
    url = f"https://raw.githubusercontent.com/pret/pokeruby/{REVISION}/{path}"
    with urllib.request.urlopen(url, timeout=30) as response:
        return response.read().decode("utf-8", errors="replace")


def constants(text, prefix):
    return {name: int(value, 0) for name, value in re.findall(
        rf"^#define {prefix}(\w+)\s+(0x[\dA-Fa-f]+|\d+)\b", text, re.M)}


def named_blocks(text, prefix):
    return dict(re.findall(rf"\[{prefix}(\w+)\]\s*=\s*\{{\n(.*?)\n    \}}", text, re.S))


def collection(values):
    return "[" + ", ".join(map(str, values)) + "]"


def pascal_case(symbol):
    return symbol.title().replace("_", "")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-directory", type=pathlib.Path, help="Read previously downloaded source files by name.")
    options = parser.parse_args()
    with concurrent.futures.ThreadPoolExecutor() as executor:
        texts = list(executor.map(lambda path: source(path, options.source_directory), PATHS))
    (species_source, moves_source, items_source, types_source, effects_source, stats_source,
     evolution_source, levels_source, pointers_source, machines_source, eggs_source, battle_source) = texts

    species_ids = constants(species_source, "SPECIES_")
    dex_ids = constants(species_source, "NATIONAL_DEX_")
    moves = constants(moves_source, "MOVE_")
    types = constants(types_source, "TYPE_")
    effects = constants(effects_source, "EFFECT_")
    species = sorted((dex, name) for name, dex in dex_ids.items() if 1 <= dex <= 386)
    assert [dex for dex, _ in species] == list(range(1, 387))

    stats = named_blocks(stats_source, "SPECIES_")
    evolution_block = re.compile(r"\[SPECIES_(\w+)\]\s*=\s*\{\{(.*?)\}\}", re.S)
    evolutions = {name: re.findall(r"EVO_(\w+),\s*(\w+),\s*SPECIES_(\w+)", block)
                  for name, block in evolution_block.findall(evolution_source)}
    pointers = re.findall(r"^    (g\w+LevelUpLearnset),?$", pointers_source, re.M)
    levels = dict(re.findall(r"const u16 (\w+)\[\] = \{(.*?)\n\};", levels_source, re.S))
    moves_by_machine_name = {name.replace("_", ""): index for name, index in moves.items()}
    machines = {name: re.findall(r"\bTMHM\(([TH]M\d\d_\w+)\)", block) for name, block in
                re.findall(r"\[SPECIES_(\w+)\]\s*=\s*TMHM_LEARNSET\((.*?)\),", machines_source, re.S)}
    eggs = {name: re.findall(r"MOVE_(\w+)", block) for name, block in
            re.findall(r"    egg_moves\((\w+),(.*?)\),", eggs_source, re.S)}

    def evolution_row(method, argument, target):
        method_id = EVOLUTION_METHODS.index(method) + 1
        parameter = "0" if argument.startswith("ITEM_") else argument
        item = json.dumps(EVOLUTION_ITEMS[argument[5:]], ensure_ascii=False) if argument.startswith("ITEM_") else "null"
        return f"new({species_ids[target]}, RubyRomDetails.EvolutionRequirement({method_id}, {parameter}, {item}))"

    def machine_row(machine, move):
        kind = "true" if machine.startswith("HM") else "false"
        move_id = moves_by_machine_name[move.replace("_", "")]
        return f"new({int(machine[2:4])}, {move_id}, {kind})"

    rows = []
    for dex, name in species:
        species_id = species_ids[name]
        values = dict(re.findall(r"\.(base\w+)\s*=\s*(\d+),", stats[name]))
        stat_fields = ", ".join(values["base" + field] for field in ("HP", "Attack", "Defense", "Speed", "SpAttack", "SpDefense"))
        learnset = [f"new({level}, {moves[move]})" for level, move in
                    re.findall(r"LEVEL_UP_MOVE\(\s*(\d+),\s*MOVE_(\w+)\)", levels[pointers[species_id]])]
        chains = [evolution_row(*edge) for edge in evolutions.get(name, [])]
        technical = [machine_row(machine, machine[5:]) for machine in machines[name]]
        inherited = [moves[move] for move in eggs.get(name, [])]
        assert learnset, name
        rows.append(f"        ReferencePokemon({species_id}, {dex}, new({stat_fields}),\n"
                    f"            {collection(learnset)},\n"
                    f"            {collection(chains)},\n"
                    f"            {collection(technical)}, {collection(inherited)}),")

    move_rows = []
    battle = named_blocks(battle_source, "MOVE_")
    by_move_id = {index: name for name, index in moves.items() if 1 <= index <= 354}
    for index in range(1, 355):
        fields = dict(re.findall(r"\.(\w+)\s*=\s*([^,]+),", battle[by_move_id[index]]))
        effect = pascal_case(fields["effect"][7:])
        move_rows.append(
            f"        new({index}, {types[fields['type'][5:]]}, {fields['power']}, {fields['accuracy']}, {fields['pp']}, "
            f"RubyMoveEffect.{effect}, {fields['secondaryEffectChance']}, {fields['priority']}),"
        )

    item_ids = constants(items_source, "ITEM_")
    hidden_machines = sorted((name for name in item_ids if re.match(r"HM\d\d_", name)), key=item_ids.get)
    assert len(hidden_machines) == 8
    content = [
        "namespace SoulLink;", "", "// Generated by scripts/update-ruby-wiki.py.",
        "public sealed partial class RubyWikiCatalog", "{",
        "    private static readonly RubyWikiPokemon[] ReferenceEntries =", "    [", *rows, "    ];", "",
        "    private static readonly RubyWikiMove?[] ReferenceMoves =", "    [", "        null,", *move_rows, "    ];", "",
        "    private static readonly RubyMachineMove[] HiddenMachines =",
        "    " + collection(machine_row(machine, machine[5:]) for machine in hidden_machines) + ";",
        "}", "", "public enum RubyMoveEffect", "{",
        *(f"    {pascal_case(name)} = {value}," for name, value in effects.items()), "}", "",
    ]
    output = ROOT / "SoulLink/RubyWikiCatalog.Data.cs"
    output.write_bytes("\n".join(content).replace("\n", "\r\n").encode("utf-8"))
    print(f"Generated {len(rows)} Pokémon and {len(move_rows)} moves.")


if __name__ == "__main__":
    main()
