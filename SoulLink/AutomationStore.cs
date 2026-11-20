using Microsoft.Data.Sqlite;

namespace SoulLink;

/// <summary>Transactions persist a single run, its credentials, receipts and notification outbox together.</summary>
public sealed class AutomationStore
{
    private const int SchemaVersion = 2;
    private readonly string connectionString;
    private readonly object gate = new();

    public event Action<Guid>? RunChanged;

    public AutomationStore(string path)
    {
        string fullPath = Path.GetFullPath(path);
        ValidateDatabasePath(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = false,
        }.ToString();
        using var connection = this.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version";
        int version = Convert.ToInt32(command.ExecuteScalar());
        if (version > SchemaVersion) throw new InvalidDataException("SoulLink database schema is newer than this application.");
        if (version < 1)
        {
            command.CommandText = """
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, guild TEXT NOT NULL, selected INTEGER NOT NULL, document TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS selected_run ON runs(guild) WHERE selected=1;
            CREATE TABLE IF NOT EXISTS registrations(hash TEXT PRIMARY KEY, run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, player TEXT NOT NULL, client TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS receipts(player TEXT NOT NULL, event TEXT NOT NULL, sequence INTEGER NOT NULL, PRIMARY KEY(player,event), UNIQUE(player,sequence));
            CREATE TABLE IF NOT EXISTS outbox(id TEXT PRIMARY KEY, run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, channel TEXT NOT NULL, message TEXT NOT NULL, delivered INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS map_history(run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, player TEXT NOT NULL, sequence INTEGER NOT NULL, at TEXT NOT NULL, area INTEGER NOT NULL, map_group INTEGER NOT NULL, map_number INTEGER NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, width INTEGER NOT NULL, height INTEGER NOT NULL, outdoor INTEGER NOT NULL, PRIMARY KEY(run,player,sequence));
            CREATE TABLE IF NOT EXISTS replay_events(run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, id TEXT NOT NULL, at TEXT NOT NULL, document TEXT NOT NULL, PRIMARY KEY(run,id));
            PRAGMA user_version=1;
            """;
            command.ExecuteNonQuery();
        }
        if (version < 2)
        {
            command.CommandText = """
                CREATE TABLE run_groups (id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE);
                PRAGMA user_version=2;
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static void Backup(string source, string destination)
    {
        string input = Path.GetFullPath(source);
        string output = Path.GetFullPath(destination);
        ValidateDatabasePath(input);
        ValidateDatabasePath(output);
        if (!File.Exists(input)) throw new FileNotFoundException("SoulLink database not found.", input);
        using var from = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = input,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        from.Open();
        using (File.Open(output, FileMode.CreateNew)) { }
        try
        {
            using var to = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = output,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            to.Open();
            from.BackupDatabase(to);
        }
        catch
        {
            File.Delete(output);
            throw;
        }
    }

    private static void ValidateDatabasePath(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("JSON archives cannot be used as the automation database.");
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return;
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[16];
        if (source.Read(header) != header.Length || !header.SequenceEqual("SQLite format 3\0"u8))
            throw new InvalidDataException("The existing file is not a SQLite database; it will not be modified.");
    }

    public T Execute<T>(Func<StoreTransaction, T> action)
    {
        lock (this.gate)
        {
            using var connection = this.Open();
            using var transaction = connection.BeginTransaction();
            var context = new StoreTransaction(connection, transaction);
            T result = action(context);
            transaction.Commit();
            foreach (Guid id in context.ChangedRuns) this.RunChanged?.Invoke(id);
            return result;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(this.connectionString);
        connection.Open();
        return connection;
    }

    public sealed class StoreTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        internal HashSet<Guid> ChangedRuns { get; } = [];

        public RunGroup[] Groups()
        {
            using var command = this.Command("SELECT id,name FROM run_groups ORDER BY name");
            using var reader = command.ExecuteReader();
            var groups = new List<RunGroup>();
            while (reader.Read()) groups.Add(new RunGroup(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
            return groups.ToArray();
        }

        public RunGroup Group(Guid id)
        {
            using var command = this.Command("SELECT name FROM run_groups WHERE id=$id", ("$id", id.ToString()));
            return command.ExecuteScalar() is string name ? new RunGroup(id, name) : throw new InvalidOperationException("Gruppe nicht gefunden.");
        }

        public void AddGroup(RunGroup group)
        {
            using var command = this.Command("INSERT INTO run_groups VALUES($id,$name)", ("$id", group.Id.ToString()), ("$name", group.Name));
            command.ExecuteNonQuery();
        }

        public List<AutomationRun> Runs(string? guild = null)
        {
            using var command = this.Command(guild is null ? "SELECT document FROM runs ORDER BY rowid" : "SELECT document FROM runs WHERE guild=$guild ORDER BY rowid", ("$guild", guild ?? ""));
            using var reader = command.ExecuteReader();
            var runs = new List<AutomationRun>();
            while (reader.Read()) runs.Add(Protocol.Deserialize<AutomationRun>(reader.GetString(0)));
            return runs;
        }

        public AutomationRun Run(Guid id)
        {
            using var command = this.Command("SELECT document FROM runs WHERE id=$id", ("$id", id.ToString()));
            string json = command.ExecuteScalar() as string ?? throw new InvalidOperationException("Run not found.");
            return Protocol.Deserialize<AutomationRun>(json);
        }

        public void Save(AutomationRun run)
        {
            using var command = this.Command("INSERT INTO runs VALUES($id,$guild,$selected,$document) ON CONFLICT(id) DO UPDATE SET selected=$selected,document=$document",
                ("$id", run.Id.ToString()), ("$guild", run.GuildId), ("$selected", run.Selected ? 1 : 0), ("$document", Protocol.Serialize(run)));
            command.ExecuteNonQuery();
            this.ChangedRuns.Add(run.Id);
        }

        public void Delete(Guid id)
        {
            using var command = this.Command("DELETE FROM runs WHERE id=$id", ("$id", id.ToString()));
            command.ExecuteNonQuery();
            this.ChangedRuns.Add(id);
        }

        public void RecordPosition(Guid run, Guid player, long sequence, DateTimeOffset at, GameSnapshot snapshot, MapPosition position)
        {
            using var command = this.Command(
                "INSERT INTO map_history VALUES($run,$player,$sequence,$at,$area,$group,$number,$x,$y,$width,$height,$outdoor)",
                ("$run", run.ToString()), ("$player", player.ToString()), ("$sequence", sequence), ("$at", at.ToString("O")),
                ("$area", snapshot.Area), ("$group", snapshot.MapGroup), ("$number", snapshot.MapNumber), ("$x", position.X), ("$y", position.Y),
                ("$width", position.Width), ("$height", position.Height), ("$outdoor", position.Outdoor));
            command.ExecuteNonQuery();
        }

        public void RecordReplayEvent(Guid run, AutomationEvent item)
        {
            using var command = this.Command("INSERT INTO replay_events VALUES($run,$id,$at,$document)",
                ("$run", run.ToString()), ("$id", item.Id.ToString()), ("$at", item.At.ToString("O")),
                ("$document", Protocol.Serialize(item)));
            command.ExecuteNonQuery();
        }

        public Registration? Registration(string hash)
        {
            using var command = this.Command("SELECT run,player,client FROM registrations WHERE hash=$hash", ("$hash", hash));
            using var reader = command.ExecuteReader();
            return reader.Read() ? new Registration(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2))) : null;
        }

        public void Register(string hash, Registration registration)
        {
            using var command = this.Command("INSERT INTO registrations VALUES($hash,$run,$player,$client)",
                ("$hash", hash), ("$run", registration.RunId.ToString()), ("$player", registration.PlayerId.ToString()), ("$client", registration.ClientId.ToString()));
            command.ExecuteNonQuery();
        }

        public bool HasReceipt(Guid player, Guid eventId, long sequence)
        {
            using var command = this.Command("SELECT COUNT(*) FROM receipts WHERE player=$player AND event=$event AND sequence=$sequence",
                ("$player", player.ToString()), ("$event", eventId.ToString()), ("$sequence", sequence));
            return Convert.ToInt32(command.ExecuteScalar()) != 0;
        }

        public void Receipt(Guid player, Guid eventId, long sequence)
        {
            using var command = this.Command("INSERT INTO receipts VALUES($player,$event,$sequence)",
                ("$player", player.ToString()), ("$event", eventId.ToString()), ("$sequence", sequence));
            command.ExecuteNonQuery();
        }

        public void Notify(AutomationRun run, AutomationEvent item)
        {
            if (run.ChannelId == 0) return;
            using var command = this.Command("INSERT OR IGNORE INTO outbox(id,run,channel,message) VALUES($id,$run,$channel,$message)",
                ("$id", item.Id.ToString()), ("$run", run.Id.ToString()), ("$channel", run.ChannelId.ToString()), ("$message", item.Message));
            command.ExecuteNonQuery();
        }

        public List<Notification> Notifications()
        {
            using var command = this.Command("SELECT id,run,channel,message FROM outbox WHERE delivered=0 ORDER BY rowid LIMIT 20");
            using var reader = command.ExecuteReader();
            var result = new List<Notification>();
            while (reader.Read()) result.Add(new Notification(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), ulong.Parse(reader.GetString(2)), reader.GetString(3)));
            return result;
        }

        public void Delivered(Guid id)
        {
            using var command = this.Command("UPDATE outbox SET delivered=1 WHERE id=$id", ("$id", id.ToString()));
            command.ExecuteNonQuery();
        }

        private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return command;
        }
    }
}
