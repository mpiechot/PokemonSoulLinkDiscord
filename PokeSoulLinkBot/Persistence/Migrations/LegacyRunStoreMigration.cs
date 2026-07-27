namespace PokeSoulLinkBot.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migrates the legacy unversioned run collection to the first versioned document.
/// </summary>
internal sealed class LegacyRunStoreMigration : IRunStoreMigration
{
    /// <inheritdoc />
    public int SourceVersion => 0;

    /// <inheritdoc />
    public int TargetVersion => 1;

    /// <inheritdoc />
    public RunStoreDocument Migrate(RunStoreDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new RunStoreDocument
        {
            SchemaVersion = this.TargetVersion,
            Runs = document.Runs ?? new (),
        };
    }
}
