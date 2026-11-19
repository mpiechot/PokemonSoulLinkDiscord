using Microsoft.Data.Sqlite;

namespace SoulLink;

/// <summary>Transactions persist a single run, its credentials, receipts and notification outbox together.</summary>
public sealed class AutomationStore
{
    private readonly string connectionString;
    private readonly object gate = new();

    public AutomationStore(string path)
    {
        string fullPath = Path.GetFullPath(path);
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
        command.CommandText = "PRAGMA user_version";
        int version = Convert.ToInt32(command.ExecuteScalar());
        if (version > 1) throw new InvalidDataException("SoulLink database schema is newer than this application.");
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY, guild TEXT NOT NULL, selected INTEGER NOT NULL, document TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS selected_run ON runs(guild) WHERE selected=1;
            CREATE TABLE IF NOT EXISTS registrations(hash TEXT PRIMARY KEY, run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, player TEXT NOT NULL, client TEXT NOT NULL UNIQUE);
            CREATE TABLE IF NOT EXISTS receipts(player TEXT NOT NULL, event TEXT NOT NULL, sequence INTEGER NOT NULL, PRIMARY KEY(player,event), UNIQUE(player,sequence));
            CREATE TABLE IF NOT EXISTS outbox(id TEXT PRIMARY KEY, run TEXT NOT NULL REFERENCES runs(id) ON DELETE CASCADE, channel TEXT NOT NULL, message TEXT NOT NULL, delivered INTEGER NOT NULL DEFAULT 0);
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
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
        }

        public void Delete(Guid id)
        {
            using var command = this.Command("DELETE FROM runs WHERE id=$id", ("$id", id.ToString()));
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
