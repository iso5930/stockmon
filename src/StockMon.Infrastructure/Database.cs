using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace StockMon.Infrastructure;

public static class DatabaseMigrator
{
    public static async Task ApplyAsync(NpgsqlDataSource source, CancellationToken token = default)
    {
        await using var connection = await source.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await Execute("SELECT pg_advisory_xact_lock(857312049)");
        await Execute("""
            CREATE TABLE IF NOT EXISTS public.stockmon_migrations (
                version integer PRIMARY KEY, sha256 text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now()
            );
            REVOKE ALL ON public.stockmon_migrations FROM PUBLIC;
            """);
        var assembly = typeof(DatabaseMigrator).Assembly;
        using var stream = assembly.GetManifestResourceStream("StockMon.Infrastructure.Migrations.001_initial.sql")
            ?? throw new InvalidOperationException("Migration resource missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sql = (await reader.ReadToEndAsync(token)).ReplaceLineEndings("\n");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        await using var existing = new NpgsqlCommand("SELECT sha256 FROM public.stockmon_migrations WHERE version = 1", connection, transaction);
        var previous = await existing.ExecuteScalarAsync(token);
        if (previous is string previousHash)
        {
            if (previousHash != hash) throw new InvalidOperationException("Applied migration checksum mismatch.");
        }
        else
        {
            await Execute(sql);
            await using var insert = new NpgsqlCommand("INSERT INTO public.stockmon_migrations(version, sha256) VALUES (1, $1)", connection, transaction);
            insert.Parameters.AddWithValue(hash);
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);

        async Task Execute(string commandText)
        {
            await using var command = new NpgsqlCommand(commandText, connection, transaction);
            await command.ExecuteNonQueryAsync(token);
        }
    }
}

public sealed class AccountReader(NpgsqlDataSource source)
{
    public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken token = default)
    {
        await using var command = source.CreateCommand("SELECT id FROM stockmon.accounts ORDER BY id");
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<string>();
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result.AsReadOnly();
    }
}
