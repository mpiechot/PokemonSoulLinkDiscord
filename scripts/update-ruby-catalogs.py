"""Regenerate factual Ruby catalogs from the pinned pret/pokeruby source revision (stdlib only)."""
import concurrent.futures
import json
import pathlib
import re
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
LOCK = json.loads((ROOT / "integration/mgba/sources.json").read_text(encoding="utf-8"))
BASE = "https://raw.githubusercontent.com/pret/pokeruby/" + LOCK["pokeruby"] + "/"


def fetch(path):
    with urllib.request.urlopen(BASE + path, timeout=30) as response:
        return response.read().decode("utf-8")


def write_catalog(name, entries, value_type, method):
    content = ("namespace SoulLink;\n\n"
               "// Generated from pret/pokeruby; see integration/mgba/sources.json and scripts/update-ruby-catalogs.py.\n"
               f"public static class {name}\n{{\n"
               f"    private static readonly Dictionary<int, {value_type}> Values = new()\n    {{\n"
               + "\n".join(entries) + "\n    };\n\n" + method + "\n}\n")
    (ROOT / "SoulLink" / (name + ".cs")).write_bytes(content.replace("\n", "\r\n").encode("utf-8"))


species = []
for name, number in re.findall(r"^#define SPECIES_([A-Z0-9_]+) (\d+)$", fetch("include/constants/species.h"), re.M):
    if 1 <= int(number) <= 411 and "OLD_UNOWN" not in name and name != "EGG":
        species.append(f'        [{number}] = "{name.lower().replace("_", "-")}",')
write_catalog("SpeciesCatalog", species, "string",
              '    public static bool IsValid(int id) => Values.ContainsKey(id);\n\n'
              '    public static string Name(int id) => Values.GetValueOrDefault(id, "Species #" + id);')

sections = json.loads(fetch("src/data/region_map/region_map_sections.json"))["map_sections"]
section_ids = {section["id"]: i for i, section in enumerate(sections)}
areas = [f'        [{i}] = "{item["id"].removeprefix("MAPSEC_").replace("_", " ").title()}",' for i, item in enumerate(sections)]
write_catalog("AreaCatalog", areas, "string", '    public static string Name(int id) => Values.GetValueOrDefault(id, "Area #" + id);')

groups = json.loads(fetch("data/maps/map_groups.json"))
maps = [(group, number, name) for group, label in enumerate(groups["group_order"]) for number, name in enumerate(groups[label])]


def map_row(item):
    group, number, name = item
    data = json.loads(fetch(f"data/maps/{name}/map.json"))
    return f'        [{(group << 8) | number}] = {section_ids[data["region_map_section"]]}, // {name}'


with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
    rows = list(pool.map(map_row, maps))
write_catalog("MapCatalog", rows, "int", '    public static int Section(int group, int number) => Values.GetValueOrDefault((group << 8) | number, 255);')
print(f"Generated {len(species)} species, {len(areas)} areas and {len(maps)} maps.")
