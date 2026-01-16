namespace BackgroundEmailSenderSample.Models.Services.Infrastructure;

/// <summary>
/// Lightweight accessor for executing commands and queries against a SQLite database.
/// Uses parameterized SQL created from <see cref="FormattableString"/> arguments.
/// </summary>
public class SqliteDatabaseAccessor(ILogger<SqliteDatabaseAccessor> logger, IOptionsMonitor<ConnectionStringsOptions> connectionStringOptions)
    : IDatabaseAccessor
{
    /// <inheritdoc />
    public async Task<int> CommandAsync(FormattableString formattableCommand, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(formattableCommand);

        try
        {
            using var conn = await GetOpenedConnectionAsync(token).ConfigureAwait(false);
            using var cmd = GetCommand(formattableCommand, conn);

            var affectedRows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            return affectedRows;
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    /// <inheritdoc />
    public async Task<T> QueryScalarAsync<T>(FormattableString formattableQuery, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(formattableQuery);

        try
        {
            using var conn = await GetOpenedConnectionAsync(token).ConfigureAwait(false);
            using var cmd = GetCommand(formattableQuery, conn);

            var result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);

            if (result is DBNull or null)
            {
                return default!;
            }

            // Convert.ChangeType can be slow; keep it for generality but guard null/DBNULL.
            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    /// <inheritdoc />
    public async Task<DataSet> QueryAsync(FormattableString formattableQuery, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(formattableQuery);

        logger.LogInformation(formattableQuery.Format, formattableQuery.GetArguments());

        using var conn = await GetOpenedConnectionAsync(token).ConfigureAwait(false);
        using var cmd = GetCommand(formattableQuery, conn);

        try
        {
            using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var dataSet = new DataSet();

            // Load each result set into a DataTable until the reader is exhausted.
            do
            {
                var dataTable = new DataTable();
                dataSet.Tables.Add(dataTable);
                dataTable.Load(reader);
            }
            while (!reader.IsClosed);

            return dataSet;
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    /// <summary>
    /// Builds a <see cref="SqliteCommand"/> from a <see cref="FormattableString"/>,

    /// creating parameters for non-<c>Sql</c> arguments.
    /// This implementation avoids extra List allocations by computing the required
    /// count and allocating a single array for parameters.
    /// </summary>
    private static SqliteCommand GetCommand(FormattableString formattableQuery, SqliteConnection conn)
    {
        var queryArguments = formattableQuery.GetArguments();

        // First pass: count how many parameters we need (skip arguments of type Sql).
        var totalArgs = queryArguments.Length;
        var paramCount = 0;

        for (var i = 0; i < totalArgs; i++)
        {
            if (queryArguments[i] is not Sql)
            {
                paramCount++;
            }
        }

        // If there are no parameters, just format the query and return command.
        if (paramCount == 0)
        {
            var queryNoParams = formattableQuery.ToString();
            return new SqliteCommand(queryNoParams, conn);
        }

        // Allocate exactly the number of parameters needed.
        var parameters = new SqliteParameter[paramCount];
        var insertIndex = 0;

        for (var i = 0; i < totalArgs; i++)
        {
            if (queryArguments[i] is Sql)
            {
                // keep SQL fragment inline; leave argument untouched
                continue;
            }

            // Use a stable parameter name. Prefix with "p" to avoid names that start with digits.
            var paramName = "@" + i;
            var value = queryArguments[i] ?? DBNull.Value;
            parameters[insertIndex++] = new SqliteParameter(paramName, value);

            // Replace the original argument with the parameter token to be applied by FormattableString.ToString()
            queryArguments[i] = paramName;
        }

        var query = formattableQuery.ToString();
        var cmd = new SqliteCommand(query, conn);

        // AddRange accepts array; avoid creating another collection.
        cmd.Parameters.AddRange(parameters);

        return cmd;
    }

    private async Task<SqliteConnection> GetOpenedConnectionAsync(CancellationToken token)
    {
        var conn = new SqliteConnection(connectionStringOptions.CurrentValue.Default);

        await conn.OpenAsync(token).ConfigureAwait(false);

        return conn;
    }
}