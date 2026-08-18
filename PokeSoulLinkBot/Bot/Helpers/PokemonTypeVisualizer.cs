namespace PokeSoulLinkBot.Bot.Helpers;

public static class PokemonTypeVisualizer
{
    public static string FormatTypeLabel(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var normalized = type.Trim().ToLowerInvariant();
        var germanName = normalized switch
        {
            "normal" => "Normal",
            "fire" => "Feuer",
            "water" => "Wasser",
            "grass" => "Pflanze",
            "electric" => "Elektro",
            "ice" => "Eis",
            "fighting" => "Kampf",
            "poison" => "Gift",
            "ground" => "Boden",
            "flying" => "Flug",
            "psychic" => "Psycho",
            "bug" => "Käfer",
            "rock" => "Gestein",
            "ghost" => "Geist",
            "dragon" => "Drache",
            "dark" => "Unlicht",
            "steel" => "Stahl",
            "fairy" => "Fee",
            _ => type,
        };

        return $"{FormatType(normalized)} {germanName}";
    }

    public static string FormatType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "normal" => "⚪",
            "fire" => "🔥",
            "water" => "💧",
            "grass" => "🌿",
            "electric" => "⚡",
            "ice" => "❄️",
            "fighting" => "🥊",
            "poison" => "☠️",
            "ground" => "🌍",
            "flying" => "🕊️",
            "psychic" => "🔮",
            "bug" => "🐛",
            "rock" => "🪨",
            "ghost" => "👻",
            "dragon" => "🐉",
            "dark" => "🌑",
            "steel" => "⚙️",
            "fairy" => "✨",
            _ => type,
        };
    }
}
