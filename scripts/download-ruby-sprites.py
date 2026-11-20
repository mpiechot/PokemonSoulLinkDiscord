"""Populate a local, untracked sprite cache for the Ruby player."""
import argparse
import concurrent.futures
import pathlib
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = "https://raw.githubusercontent.com/PokeAPI/sprites/c0542f7c51215d9672439012838c99f520596cbd/"
BASE = SOURCE + "sprites/pokemon/versions/generation-iii/ruby-sapphire/"
ENERGY_SOURCE = "https://www.pokepedia.fr/Sp%C3%A9cial:Chemin_du_fichier/"
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--directory", type=pathlib.Path, default=ROOT / "artifacts/player/sprites")
parser.add_argument("--type-icons-only", action="store_true", help="Download only the type symbols.")
arguments = parser.parse_args()
directory = arguments.directory.resolve()
artifact_root = (ROOT / "artifacts").resolve()
if artifact_root not in directory.parents:
    parser.error("Use a folder below artifacts so sprite files stay out of Git.")
directory.mkdir(parents=True, exist_ok=True)
type_directory = directory / "tcg-icons"
type_directory.mkdir(exist_ok=True)
game_type_directory = directory / "type-icons"
game_type_directory.mkdir(exist_ok=True)


def download(item):
    path, url = item
    name = path.name
    if path.exists() and path.read_bytes().startswith(PNG_MAGIC):
        return None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(url, timeout=15) as response:
                data = response.read(256 * 1024 + 1)
            if not data.startswith(PNG_MAGIC) or len(data) > 256 * 1024:
                return f"{name}: invalid PNG"
            temporary = path.with_suffix(".tmp")
            temporary.write_bytes(data)
            temporary.replace(path)
            return None
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return f"{name}: missing"
            if attempt == 2:
                return f"{name}: HTTP {error.code}"
        except OSError as error:
            if attempt == 2:
                return f"{name}: {error}"
        time.sleep(1 + attempt * 2)
    return None


items = [] if arguments.type_icons_only else [
    (directory / f"{number}{'-shiny' if shiny else ''}.png", BASE + ("shiny/" if shiny else "") + f"{number}.png")
    for number in range(1, 387) for shiny in (False, True)
]
energies = ("Combat", "Dragon", "Eau", "Électrique", "Fée", "Feu", "Incolore", "Métal", "Obscurité", "Plante", "Psy")
items.extend((type_directory / f"{energy}.png",
              ENERGY_SOURCE + urllib.parse.quote(f"Miniature_Type_{energy}_(flat)_JCC.svg") + "?width=120")
             for energy in energies)
game_types = {"flying": 3, "poison": 4, "ground": 5, "rock": 6, "bug": 7, "ghost": 8, "ice": 15}
items.extend((game_type_directory / f"{kind}.png",
              SOURCE + f"sprites/types/generation-viii/legends-arceus/small/{index}.png")
             for kind, index in game_types.items())
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    failures = [error for error in pool.map(download, items) if error]
if not arguments.type_icons_only:
    with urllib.request.urlopen(SOURCE + "LICENCE.txt", timeout=15) as response:
        (directory / "POKEAPI-LICENCE.txt").write_bytes(response.read())
print(f"Available locally: {len(items) - len(failures)}/{len(items)} sprites.")
for failure in failures[:20]:
    print(failure)
if failures:
    raise SystemExit(1)
