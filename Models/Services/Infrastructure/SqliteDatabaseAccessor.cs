namespace BackgroundEmailSenderSample.Models.Services.Infrastructure;

public class SqliteDatabaseAccessor(ILogger<SqliteDatabaseAccessor> logger, IOptionsMonitor<ConnectionStringsOptions> connectionStringOptions) : IDatabaseAccessor
{
    public async Task<int> CommandAsync(FormattableString formattableCommand, CancellationToken token)
    {
        try
        {
            using var conn = await GetOpenedConnectionAsync(token);
            using var cmd = GetCommand(formattableCommand, conn);

            var affectedRows = await cmd.ExecuteNonQueryAsync(token);

            return affectedRows;
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    public async Task<T> QueryScalarAsync<T>(FormattableString formattableQuery, CancellationToken token)
    {
        try
        {
            using var conn = await GetOpenedConnectionAsync(token);
            using var cmd = GetCommand(formattableQuery, conn);

            var result = await cmd.ExecuteScalarAsync(token);

            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    public async Task<DataSet> QueryAsync(FormattableString formattableQuery, CancellationToken token)
    {
        logger.LogInformation(formattableQuery.Format, formattableQuery.GetArguments());

        using var conn = await GetOpenedConnectionAsync(token);
        using var cmd = GetCommand(formattableQuery, conn);

        try
        {
            using var reader = await cmd.ExecuteReaderAsync(token);
            var dataSet = new DataSet();

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

    private static SqliteCommand GetCommand(FormattableString formattableQuery, SqliteConnection conn)
    {
        var queryArguments = formattableQuery.GetArguments();
        var sqliteParameters = new List<SqliteParameter>();

        for (var i = 0; i < queryArguments.Length; i++)
        {
            if (queryArguments[i] is Sql)
            {
                continue;
            }

            var parameter = new SqliteParameter(name: i.ToString(), value: queryArguments[i] ?? DBNull.Value);

            sqliteParameters.Add(parameter);
            queryArguments[i] = "@" + i;
        }

        var query = formattableQuery.ToString();
        var cmd = new SqliteCommand(query, conn);

        cmd.Parameters.AddRange(sqliteParameters);
        return cmd;
    }

    private async Task<SqliteConnection> GetOpenedConnectionAsync(CancellationToken token)
    {
        var conn = new SqliteConnection(connectionStringOptions.CurrentValue.Default);

        await conn.OpenAsync(token);

        return conn;
    }
}