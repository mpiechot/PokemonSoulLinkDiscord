namespace PokeSoulLinkBot.Bot.Helpers;

public static class PokemonTypeVisualizer
{
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
