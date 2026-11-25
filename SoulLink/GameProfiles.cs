namespace SoulLink;

/// <summary>Resolves a cartridge or a protocol identity to its supported game profile.</summary>
public static class GameProfiles
{
    public const string DefaultGameCode = RubyGameProfile.Code;
    public const string DefaultEdition = RubyGameProfile.EditionId;

    private static readonly GameProfile[] Supported = [new RubyGameProfile()];

    public static string RomFilePattern { get; } = string.Join(';',
        Supported.Select(profile => "*" + profile.RomExtension).Distinct(StringComparer.OrdinalIgnoreCase));

    public static bool SupportsRomExtension(string extension) =>
        Supported.Any(profile => profile.RomExtension.Equals(extension, StringComparison.OrdinalIgnoreCase));

    public static GameProfile? ForGameCode(string? code) =>
        Supported.FirstOrDefault(profile => profile.GameCode == code);

    public static GameProfile? ForEdition(string? edition) =>
        Supported.FirstOrDefault(profile => profile.MatchesEdition(edition));

    public static string AreaName(string? edition, int area) => ForEdition(edition)?.AreaName(area) ??
        (area == Encounter.StarterArea ? "Starter" : "Unbekannter Ort");

    public static PokemonRomDetails ReadPokemonDetails(Stream rom, int species) =>
        (Identify(rom) ?? throw new InvalidDataException("Für diese ROM sind keine Pokémon-Tabellen hinterlegt.")).ReadPokemonDetails(rom, species);

    public static GameProfile? Identify(Stream rom)
    {
        // This window covers both the GBA and the original Game Boy cartridge headers.
        Span<byte> header = stackalloc byte[0x150];
        long position = rom.Position;
        try
        {
            rom.Position = 0;
            int length = rom.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            foreach (var profile in Supported)
                if (profile.MatchesHeader(header[..length])) return profile;
            return null;
        }
        finally { rom.Position = position; }
    }
}

/// <summary>Owns the interpretation of raw frames and cartridge data for a supported game.</summary>
public abstract class GameProfile
{
    public abstract string GameCode { get; }

    public abstract string Edition { get; }

    public abstract string RomExtension { get; }

    internal abstract bool MatchesEdition(string? edition);

    internal abstract bool MatchesHeader(ReadOnlySpan<byte> header);

    internal abstract GameSnapshot Decode(BridgeFrame frame);

    internal abstract void ValidateObservation(GameSnapshot snapshot);

    internal abstract bool IsStarter(GameSnapshot snapshot, PokemonState pokemon);

    public abstract string AreaName(int area);

    public abstract bool SupportsLocalActions(string layoutId);

    public abstract bool SupportsTrade { get; }

    internal abstract PokemonState DecodeTrade(RawPokemon pokemon);

    internal abstract PokemonRomDetails ReadPokemonDetails(Stream rom, int species);
}
