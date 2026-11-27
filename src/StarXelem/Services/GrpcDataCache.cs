using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using StarXelem.Models;

namespace StarXelem.Services;

/// <summary>
/// Cache des réponses gRPC dans un fichier SQLite dédié (<c>cache.db</c>). Il est distinct de <c>database.db</c>,
/// qui est supprimée et reconstruite à chaque nouvelle version du jeu.
/// </summary>
public sealed class GrpcDataCache : IGrpcDataCache
{
    /// <summary>Version du format des données enregistrées : une entrée d'une autre version est ignorée.</summary>
    public const int SchemaVersion = 1;

    private readonly IP4kService _p4kService;
    private readonly IGrpcClientService _grpcClientService;
    private readonly ILogger<GrpcDataCache> _logger;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    /// <summary>
    /// Initialise le cache dans le dossier de données local de l'application.
    /// </summary>
    public GrpcDataCache(IP4kService p4kService, IGrpcClientService grpcClientService, ILogger<GrpcDataCache> logger)
    {
        _p4kService = p4kService;
        _grpcClientService = grpcClientService;
        _logger = logger;

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarXelem");
        Directory.CreateDirectory(folder);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "cache.db") }.ToString();
    }

    /// <inheritdoc />
    public async Task<CachedDataset<byte[]>?> LoadAsync(string datasetKey, CancellationToken token = default)
    {
        var channel = _p4kService.SelectedP4KFile?.ChannelName;
        if (string.IsNullOrEmpty(channel))
        {
            return null;
        }

        try
        {
            await using var connection = await OpenAsync(token).ConfigureAwait(false);
            var accountKey = GetConnectedAccountKey() ?? await GetLastAccountKeyAsync(connection, channel, token).ConfigureAwait(false);
            if (accountKey == null)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT FetchedAtUtc, SchemaVersion, Payload FROM GrpcDataset
                WHERE Channel = $channel AND AccountKey = $account AND DatasetKey = $dataset
                """;
            command.Parameters.AddWithValue("$channel", channel);
            command.Parameters.AddWithValue("$account", accountKey);
            command.Parameters.AddWithValue("$dataset", datasetKey);

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false) || reader.GetInt32(1) != SchemaVersion)
            {
                return null;
            }

            var fetchedAt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return new CachedDataset<byte[]>((byte[])reader.GetValue(2), fetchedAt, accountKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Lecture du cache impossible pour {Dataset}", datasetKey);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string datasetKey, byte[] payload, DateTimeOffset fetchedAt, CancellationToken token = default)
    {
        var channel = _p4kService.SelectedP4KFile?.ChannelName;
        var accountKey = GetConnectedAccountKey();
        if (string.IsNullOrEmpty(channel) || accountKey == null)
        {
            _logger.LogInformation("Cache non enregistré pour {Dataset} : environnement ou compte inconnu", datasetKey);
            return;
        }

        try
        {
            await using var connection = await OpenAsync(token).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO GrpcDataset (Channel, AccountKey, DatasetKey, FetchedAtUtc, SchemaVersion, Payload)
                    VALUES ($channel, $account, $dataset, $fetchedAt, $version, $payload)
                    ON CONFLICT (Channel, AccountKey, DatasetKey) DO UPDATE SET
                        FetchedAtUtc = excluded.FetchedAtUtc, SchemaVersion = excluded.SchemaVersion, Payload = excluded.Payload
                    """;
                command.Parameters.AddWithValue("$channel", channel);
                command.Parameters.AddWithValue("$account", accountKey);
                command.Parameters.AddWithValue("$dataset", datasetKey);
                command.Parameters.AddWithValue("$fetchedAt", fetchedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$version", SchemaVersion);
                command.Parameters.AddWithValue("$payload", payload);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO CacheAccount (Channel, AccountKey) VALUES ($channel, $account)
                    ON CONFLICT (Channel) DO UPDATE SET AccountKey = excluded.AccountKey
                    """;
                command.Parameters.AddWithValue("$channel", channel);
                command.Parameters.AddWithValue("$account", accountKey);
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            _logger.LogInformation("Cache {Dataset} enregistré ({Size} octets)", datasetKey, payload.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Écriture du cache impossible pour {Dataset}", datasetKey);
        }
    }

    // Le geid n'est connu qu'une fois le client gRPC connecté au jeu.
    private string? GetConnectedAccountKey()
    {
        return _grpcClientService.CurrentPlayerGeid?.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<string?> GetLastAccountKeyAsync(SqliteConnection connection, string channel, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AccountKey FROM CacheAccount WHERE Channel = $channel";
        command.Parameters.AddWithValue("$channel", channel);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) as string;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, token).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken token)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS GrpcDataset (
                    Channel       TEXT    NOT NULL,
                    AccountKey    TEXT    NOT NULL,
                    DatasetKey    TEXT    NOT NULL,
                    FetchedAtUtc  TEXT    NOT NULL,
                    SchemaVersion INTEGER NOT NULL,
                    Payload       BLOB    NOT NULL,
                    PRIMARY KEY (Channel, AccountKey, DatasetKey)
                );
                CREATE TABLE IF NOT EXISTS CacheAccount (
                    Channel    TEXT PRIMARY KEY,
                    AccountKey TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
