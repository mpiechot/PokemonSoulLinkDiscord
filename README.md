# PokemonSoulLinkDiscord
This project contains a discord bot for Soul Link runs with Pokemon

## Shiny rule

- `/shiny-catch route:<catch location> player:<owner> pokemon:<name>` registers an extra shiny in the active run. It does not consume or change a normal route encounter, including on already caught or lost routes. Multiple shinies of the same species are allowed.
- `/shiny-swap player:<owner> shiny:<selection> route:<link route>` exchanges the shiny with that player's living Pokémon on a team or box route. Select the shiny using autocomplete; its unique ID also works. Link partners and team positions stay the same.
- Omit `route` from `/shiny-swap` to bench the shiny and restore the original Pokémon. An assigned shiny can also move directly to another living route. If another shiny occupies the destination, it returns to the free shiny pool. The original route Pokémon are preserved.
- An assigned shiny shares its link's death. A dead shiny cannot be moved or returned, and a dead/lost route cannot receive a shiny. `/death-undo` restores the link, including its shiny. Benched Pokémon and free shinies are unaffected by link deaths.
- `/status` lists every shiny with its owner, original catch location, current link route (or no route), life/assignment state and unique ID. Assigned shinies also appear with ✨ in the team, box or dead routes. Shiny catches and assignments are saved with the run.
- `/catch-edit` edits the currently assigned Pokémon and retains its shiny identity. Return an assigned shiny before using `/catch-remove` on the original catch.
