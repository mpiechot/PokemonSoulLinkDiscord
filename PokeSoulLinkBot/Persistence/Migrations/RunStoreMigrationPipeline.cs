using System.Text.Json;

namespace PokeSoulLinkBot.Infrastructure.Persistence.Migrations;

/// <summary>
/// Applies ordered migrations to persisted run-store documents.
/// </summary>
internal sealed class RunStoreMigrationPipeline
{
    /// <summary>
    /// The schema version written by the current application.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private readonly IReadOnlyDictionary<int, IRunStoreMigration> migrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStoreMigrationPipeline"/> class.
    /// </summary>
    public RunStoreMigrationPipeline()
    {
        IRunStoreMigration[] knownMigrations =
        {
            new LegacyRunStoreMigration(),
        };

        this.migrations = knownMigrations.ToDictionary(migration => migration.SourceVersion);
    }

    /// <summary>
    /// Migrates a document to <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    /// <param name="document">The document to migrate.</param>
    /// <returns>The current document.</returns>
    /// <exception cref="JsonException">
    /// Thrown when the version is invalid or has no migration path.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the document is newer than this application supports.
    /// </exception>
    public RunStoreDocument Migrate(RunStoreDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion > CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"Run-store schema version {document.SchemaVersion} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (document.SchemaVersion < 0)
        {
            throw new JsonException($"Unsupported run-store schema version {document.SchemaVersion}.");
        }

        RunStoreDocument currentDocument = document;
        while (currentDocument.SchemaVersion < CurrentSchemaVersion)
        {
            if (!this.migrations.TryGetValue(currentDocument.SchemaVersion, out IRunStoreMigration? migration))
            {
                throw new JsonException(
                    $"No run-store migration exists for schema version {currentDocument.SchemaVersion}.");
            }

            currentDocument = migration.Migrate(currentDocument);
            if (currentDocument.SchemaVersion != migration.TargetVersion)
            {
                throw new JsonException(
                    $"Run-store migration from version {migration.SourceVersion} did not produce version {migration.TargetVersion}.");
            }
        }

        if (currentDocument.Runs is null)
        {
            throw new JsonException("The run-store document does not contain a runs collection.");
        }

        return currentDocument;
    }
}
