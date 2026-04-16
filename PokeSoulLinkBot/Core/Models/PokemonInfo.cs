using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokeSoulLinkBot.Core.Models;

public sealed class PokemonInfo
{
    public string? ImageUrl { get; set; }

    public IReadOnlyList<string> Types { get; set; } = new List<string>();
}
