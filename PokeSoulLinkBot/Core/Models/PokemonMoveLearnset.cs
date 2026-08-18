namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents the moves a Pokémon can learn.
/// </summary>
public sealed class PokemonMoveLearnset
{
    /// <summary>
    /// Gets or sets the Pokémon name.
    /// </summary>
    public string PokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets moves learned by level.
    /// </summary>
    public List<LevelUpMove> LevelUpMoves { get; set; } = new ();

    /// <summary>
    /// Gets or sets moves learned through TMs or HMs.
    /// </summary>
    public List<MachineMove> MachineMoves { get; set; } = new ();
}

/// <summary>
/// Represents a move learned at a level.
/// </summary>
public sealed class LevelUpMove
{
    /// <summary>
    /// Gets or sets the level.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Gets or sets the move name.
    /// </summary>
    public string MoveName { get; set; } = string.Empty;
}

/// <summary>
/// Represents a move learned from a TM or HM.
/// </summary>
public sealed class MachineMove
{
    /// <summary>
    /// Gets or sets the machine name.
    /// </summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the move name.
    /// </summary>
    public string MoveName { get; set; } = string.Empty;
}
