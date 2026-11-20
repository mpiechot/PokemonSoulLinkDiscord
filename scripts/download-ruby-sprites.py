"""Populate a local, untracked sprite cache for the Ruby player."""
import argparse
import concurrent.futures
import pathlib
import time
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = "https://raw.githubusercontent.com/PokeAPI/sprites/c0542f7c51215d9672439012838c99f520596cbd/"
BASE = SOURCE + "sprites/pokemon/versions/generation-iii/ruby-sapphire/"
PNG_MAGIC = b"\x89PNG\r\n\x1a\n"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--directory", type=pathlib.Path, default=ROOT / "artifacts/player/sprites")
arguments = parser.parse_args()
directory = arguments.directory.resolve()
artifact_root = (ROOT / "artifacts").resolve()
if artifact_root not in directory.parents:
    parser.error("Use a folder below artifacts so sprite files stay out of Git.")
directory.mkdir(parents=True, exist_ok=True)


def download(item):
    number, shiny = item
    name = f"{number}{'-shiny' if shiny else ''}.png"
    path = directory / name
    if path.exists() and path.read_bytes().startswith(PNG_MAGIC):
        return None
    url = BASE + ("shiny/" if shiny else "") + f"{number}.png"
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


items = [(number, shiny) for number in range(1, 387) for shiny in (False, True)]
with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    failures = [error for error in pool.map(download, items) if error]
with urllib.request.urlopen(SOURCE + "LICENCE.txt", timeout=15) as response:
    (directory / "POKEAPI-LICENCE.txt").write_bytes(response.read())
print(f"Available locally: {len(items) - len(failures)}/{len(items)} sprites.")
for failure in failures[:20]:
    print(failure)
if failures:
    raise SystemExit(1)
