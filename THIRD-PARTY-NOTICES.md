# Herkunft und Hinweise zu Drittkomponenten

SoulLink verwendet .NET und NuGet-Pakete. In den erzeugten Paketen enthält `notices/` die Lizenz- und Hinweistexte, die deren installierte NuGet-Pakete als Dateien mitliefern. Windows- und Linux-Runtimes haben jeweils eigene Weitergabebedingungen. [Microsoft-Lizenzinformationen für .NET](https://github.com/dotnet/core/blob/main/license-information.md)

## Spieldaten und Grafiken

- `SoulLink/RubyNames.Data.cs` wird aus den CSV-Tabellen für Arten-, Item- und Attackennamen sowie Entwicklungsfamilien erzeugt. Die genaue Version steht in `scripts/update-ruby-names.py`. [PokéAPI-Datensatz in der verwendeten Revision](https://github.com/PokeAPI/pokeapi/tree/193d8d4fbb57cf962e3f7c1ce5299a6fe847b3f8/data/v2/csv) · [PokéAPI-Lizenzhinweis](https://github.com/PokeAPI/pokeapi/blob/193d8d4fbb57cf962e3f7c1ce5299a6fe847b3f8/LICENSE.md)
- `SoulLink/AreaCatalog.cs`, `SoulLink/MapCatalog.cs` und `SoulLink/RubyDashboardCatalog.cs` enthalten Spielreferenzdaten aus dem in `integration/mgba/sources.json` festgelegten pret/pokeruby-Stand. `scripts/update-ruby-catalogs.py` und `scripts/update-ruby-dashboard.py` erzeugen die Tabellen. [pret/pokeruby in der verwendeten Revision](https://github.com/pret/pokeruby/tree/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f)
- `scripts/update-ruby-wiki.py` erzeugt die PokéWiki-Referenztabellen für Entwicklungen, Lernwege und Attackenwerte aus derselben Ruby-Revision. Die deutschen Wirkungsbeschreibungen sind für SoulLink formuliert. Die verwendeten Adressen der deutschen Rubin-ROM sind in der Randomizer-Referenz dokumentiert. [Ruby-Datentabellen](https://github.com/pret/pokeruby/tree/5784633ce4ef7ade1a7f2d2d0c288e3d5e6cdd7f/src/data) · [Randomizer-ROM-Layout](https://github.com/Ajarmar/universal-pokemon-randomizer-zx/blob/7f00eb866ed35c8fe3963f078b6a2e0979dc2b8c/src/com/dabomstew/pkrandom/config/gen3_offsets.ini)
- Der Server und der eigenständige Discord-Bot verwenden PokéAPI für Spielabfragen. Der Katalog `PokeSoulLinkBot/Data/game-data-fallback.json` hält Referenzdaten für den Offline-Start bereit. [PokéAPI](https://pokeapi.co/)
- Die PNG-Symbole für Status, Aktionen, Lager und Platzhalter im aktuellen Quellstand sind für SoulLink erstellt. Die lokale Minimap liest Grafiken aus der vom Spieler gewählten ROM. Der normale lokale Windows-Build kann außerdem Grafiken von PokéAPI und Poképédia abrufen. [PokéAPI zu den Bildrechten](https://github.com/PokeAPI/sprites/blob/master/LICENCE.txt) · [Poképédia zu den Bildrechten](https://www.pokepedia.fr/Pok%C3%A9p%C3%A9dia%3APrincipes_fondateurs)

## Portable mGBA-Variante

Der normale lokale Windows-Build enthält den unveränderten portablen mGBA-Build 9187. mGBA wird als eigener Prozess gestartet. Das Archiv enthält die Hinweise des mGBA-Projekts zu den mitgebauten Bibliotheken. Der Player-Build mit `-PublicBuild` verwendet eine separat installierte portable Kopie des Anwenders. [mGBA-Quellrevision](https://github.com/mgba-emu/mgba/commit/e6c17152dec0caaebd8a4b949c96a24d56debf44) · [MPL 2.0](https://www.mozilla.org/MPL/2.0/)

Der [Stand der Veröffentlichung](docs/soullink/public-release.md) dokumentiert die verbleibenden Prüfungen.
