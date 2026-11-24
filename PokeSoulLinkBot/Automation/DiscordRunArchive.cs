using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using PokeSoulLinkBot.Core.Models;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

internal sealed class DiscordRunArchive(AutomationStore store)
{
    private static readonly JsonSerializerOptions LegacyJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static async Task<DiscordRunImportResult> ImportAsync(
        Guid id, HttpRequest request, DiscordRunArchive archive, CancellationToken cancellationToken)
    {
        var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = DiscordRunImportResult.MaxArchiveBytes;
        }

        using var reader = new StreamReader(request.Body, leaveOpen: true);
        return archive.Import(id, await reader.ReadToEndAsync(cancellationToken));
    }

    public static RunStatistics Statistics(ArchivedDiscordRun entry)
    {
        var run = JsonSerializer.Deserialize<LegacyStatistics>(entry.Document, LegacyJson) ??
            throw new InvalidDataException("Ein archivierter Discord-Run ist leer.");
        var links = run.LinkGroups?.OfType<LegacyLink>().ToArray();
        var catchAreas = links?.Where(link => !string.Equals(link.Route?.Trim(), "Starter", StringComparison.OrdinalIgnoreCase)).ToArray();
        int? catches = catchAreas is not null && catchAreas.All(link => link.Entries is not null)
            ? catchAreas.Sum(link => link.Entries!.OfType<LegacyPokemon>().Count()) : null;
        var pokemon = links is not null && links.All(link => link.Entries is not null)
            ? links.SelectMany(link => link.Entries!).OfType<LegacyPokemon>().ToArray() : null;
        int? deaths = pokemon is not null && pokemon.All(member => member.IsAlive.HasValue || member.DiedAtUtc.HasValue)
            ? pokemon.Count(member => member.IsAlive == false || member.DiedAtUtc.HasValue) : null;
        int? arenas = run.CompletedArenas?.OfType<LegacyArena>().Select(arena => arena.ArenaNumber).Where(number => number > 0).Distinct().Count();
        return new RunStatistics(
            entry.Id,
            run.Name,
            true,
            run.EndedAtUtc.HasValue ? RunPhase.Finished : null,
            run.StartedAtUtc,
            run.EndedAtUtc,
            run.EndReason,
            catches,
            deaths,
            arenas);
    }

    public HostRun[] GroupRuns(Guid groupId)
    {
        var archived = store.Execute(tx => tx.ArchivedDiscordRuns(groupId));
        return archived.Select(entry => JsonSerializer.Deserialize<SoulLinkRun>(entry.Document, LegacyJson) ??
                throw new InvalidDataException("Ein archivierter Discord-Run ist leer."))
            .OrderByDescending(run => run.StartedAtUtc)
            .ThenBy(run => run.Id)
            .Select(run => new HostRun(run.Id, run.Name, RunPhase.Finished, false, string.Empty, run.Players?.Count ?? 0, 0)
            {
                DiscordArchive = new HostDiscordArchive(run.Game, run.StartedAtUtc, run.EndedAtUtc, run.EndReason),
            })
            .ToArray();
    }

    private DiscordRunImportResult Import(Guid groupId, string json)
    {
        var runs = new List<ArchivedDiscordRun>();
        var ids = new HashSet<Guid>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Wähle die runs.json des Discord-Bots. Sie muss eine Liste von Runs enthalten.");
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var run = entry.Deserialize<SoulLinkRun>(LegacyJson);
                if (run is null || run.Id == Guid.Empty || string.IsNullOrWhiteSpace(run.GuildId) ||
                    string.IsNullOrWhiteSpace(run.Name) || run.StartedAtUtc == default)
                {
                    throw new InvalidDataException($"Run {runs.Count + 1} braucht eine ID, einen Discord-Server, einen Namen und ein Startdatum.");
                }

                if (!ids.Add(run.Id))
                {
                    throw new InvalidDataException($"Die Datei enthält den Run {run.Id} mehrfach.");
                }

                runs.Add(new ArchivedDiscordRun(run.Id, entry.GetRawText()));
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Die Datei ist kein gültiger JSON-Export des Discord-Bots.", exception);
        }

        return store.Execute(tx => tx.ImportDiscordRuns(groupId, runs));
    }

    private sealed class LegacyStatistics
    {
        public string Name { get; init; } = string.Empty;

        public DateTimeOffset StartedAtUtc { get; init; }

        public DateTimeOffset? EndedAtUtc { get; init; }

        public string? EndReason { get; init; }

        public List<LegacyLink?>? LinkGroups { get; init; }

        public List<LegacyArena?>? CompletedArenas { get; init; }
    }

    private sealed class LegacyLink
    {
        public string? Route { get; init; }

        public List<LegacyPokemon?>? Entries { get; init; }
    }

    private sealed class LegacyPokemon
    {
        public bool? IsAlive { get; init; }

        public DateTimeOffset? DiedAtUtc { get; init; }
    }

    private sealed class LegacyArena
    {
        public int ArenaNumber { get; init; }
    }
}
