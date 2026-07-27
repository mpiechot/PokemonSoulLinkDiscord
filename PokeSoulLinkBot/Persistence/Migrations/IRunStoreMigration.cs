namespace PokeSoulLinkBot.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migrates one persisted run-store document version to the next version.
/// </summary>
internal interface IRunStoreMigration
{
    /// <summary>
    /// Gets the schema version accepted by the migration.
    /// </summary>
    int SourceVersion { get; }

    /// <summary>
    /// Gets the schema version produced by the migration.
    /// </summary>
    int TargetVersion { get; }

    /// <summary>
    /// Migrates the supplied document.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <returns>The migrated document.</returns>
    RunStoreDocument Migrate(RunStoreDocument document);
}
