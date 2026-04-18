namespace BackgroundEmailSenderSample.Models.Services.Infrastructure;

/// <summary>
/// Lightweight accessor for executing commands and queries against a SQLite database.
/// </summary>
/// <param name="logger">Logger used to record executed SQL and capture errors.</param>
/// <param name="connectionStringOptions">Options monitor providing connection strings (used to open connections on each call).</param>
/// <remarks>
/// This type provides a minimal, low-dependency helper to execute SQL using <see cref="FormattableString"/> instances.
/// The implementation:
/// - Creates a new <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> for each operation and opens it for the duration of the call.
/// - Converts non-<c>Sql</c> arguments of the <see cref="FormattableString"/> into SQLite parameters to avoid SQL injection.
/// - Preserves inline SQL fragments by allowing arguments of type <c>Sql</c> to pass through into the formatted command text.
/// - Translates SQLite constraint violations (error code 19) into <see cref="ConstraintViolationException"/> for clearer error handling by callers.
/// Use this accessor for simple scenarios; if you require streaming reads, transactions spanning multiple commands, or connection pooling,
/// consider a higher-level abstraction instead.
/// </remarks>
public class SqliteDatabaseAccessor(ILogger<SqliteDatabaseAccessor> logger, IOptionsMonitor<ConnectionStringsOptions> connectionStringOptions) : IDatabaseAccessor
{
    /// <summary>
    /// Executes a non-query SQL command (INSERT, UPDATE, DELETE, etc.) built from a <see cref="FormattableString"/>.
    /// </summary>
    /// <param name="formattableCommand">A <see cref="FormattableString"/> representing the SQL command. Non-<c>Sql</c> arguments become parameters.</param>
    /// <param name="token">Cancellation token observed while opening the connection and executing the command.</param>
    /// <returns>
    /// A <see cref="Task{Int32}"/> whose result is the number of rows affected by the command.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="formattableCommand"/> is <see langword="null" />.</exception>
    /// <exception cref="ConstraintViolationException">Thrown when the underlying SQLite provider reports a constraint violation (SQLite error code 19).</exception>
    /// <remarks>
    /// The method opens a connection for the lifetime of the call, creates a <see cref="Microsoft.Data.Sqlite.SqliteCommand"/> via <see cref="GetCommand"/>, 
    /// and executes it with proper parameterization. Use this for independent, atomic commands. Transactions are not provided by this method.
    /// </remarks>
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

    /// <summary>
    /// Executes a query that returns a single scalar value and converts it to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The expected CLR type of the scalar result.</typeparam>
    /// <param name="formattableQuery">A <see cref="FormattableString"/> representing the SQL query. Non-<c>Sql</c> arguments become parameters.</param>
    /// <param name="token">Cancellation token observed while opening the connection and executing the command.</param>
    /// <returns>
    /// A <see cref="Task{T}"/> with the converted scalar value. If the database value is <c>NULL</c>, returns the default value for <typeparamref name="T"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="formattableQuery"/> is <see langword="null" />.</exception>
    /// <exception cref="ConstraintViolationException">Thrown when the underlying SQLite provider reports a constraint violation (SQLite error code 19).</exception>
    /// <remarks>
    /// Conversion rules:
    /// - If the result is <c>NULL</c> or <see cref="DBNull"/>, the default value for <typeparamref name="T"/> is returned.
    /// - If the returned object is already of type <typeparamref name="T"/>, it is returned directly (fast-path).
    /// - Enum target types are supported by converting the raw database value to the enum underlying type first.
    /// - Fallback conversions use <see cref="Convert.ChangeType(object, Type)"/> to preserve generality.
    /// </remarks>
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

            // Fast-path when types already match to avoid Convert.ChangeType overhead.
            if (result is T typed)
            {
                return typed;
            }

            // Handle enums explicitly (common case), numeric conversions and other convertible types.
            var targetType = typeof(T);

            if (targetType.IsEnum)
            {
                // Underlying type may be numeric; convert to the enum's underlying type first.
                var underlying = Enum.ToObject(targetType, Convert.ChangeType(result, Enum.GetUnderlyingType(targetType)));
                return (T)underlying;
            }

            // Fallback to Convert.ChangeType for general conversions.
            return (T)Convert.ChangeType(result, targetType);
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    /// <summary>
    /// Executes a query that may return one or more result sets and returns them as a <see cref="DataSet"/>.
    /// </summary>
    /// <param name="formattableQuery">A <see cref="FormattableString"/> representing the SQL query. Non-<c>Sql</c> arguments become parameters.</param>
    /// <param name="token">Cancellation token observed while opening the connection and executing the command and while advancing result sets.</param>
    /// <returns>A <see cref="Task{DataSet}"/> containing one <see cref="DataTable"/> per result set returned by the query.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="formattableQuery"/> is <see langword="null" />.</exception>
    /// <exception cref="ConstraintViolationException">Thrown when the underlying SQLite provider reports a constraint violation (SQLite error code 19).</exception>
    /// <remarks>
    /// The method logs the formatted SQL when information-level logging is enabled. It loads each result set into a separate <see cref="DataTable"/>
    /// and adds them to a <see cref="DataSet"/>. This approach is convenient for callers that need multiple result sets but allocates objects;
    /// for high-throughput or streaming scenarios, prefer an IDataReader-based API.
    /// </remarks>
    public async Task<DataSet> QueryAsync(FormattableString formattableQuery, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(formattableQuery);

        // Avoid allocating arguments for logging unless the log level is enabled.
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(formattableQuery.Format, formattableQuery.GetArguments());
        }

        using var conn = await GetOpenedConnectionAsync(token).ConfigureAwait(false);
        using var cmd = GetCommand(formattableQuery, conn);

        try
        {
            using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            var dataSet = new DataSet();

            // Load first result set (if any).
            var firstTable = new DataTable();
            dataSet.Tables.Add(firstTable);
            firstTable.Load(reader);

            // Load remaining result sets by advancing the reader asynchronously.
            while (await reader.NextResultAsync(token).ConfigureAwait(false))
            {
                var dataTable = new DataTable();
                dataSet.Tables.Add(dataTable);
                dataTable.Load(reader);
            }

            return dataSet;
        }
        catch (SqliteException exc) when (exc.SqliteErrorCode == 19)
        {
            throw new ConstraintViolationException(exc);
        }
    }

    /// <summary>
    /// Builds a <see cref="SqliteCommand"/> from a <see cref="FormattableString"/>, creating parameters for non-<c>Sql</c> arguments.
    /// </summary>
    /// <param name="formattableQuery">The formattable SQL text containing mixed SQL fragments and values.</param>
    /// <param name="conn">An open <see cref="SqliteConnection"/> that will be associated with the returned command.</param>
    /// <returns>A configured <see cref="SqliteCommand"/> ready to be executed.</returns>
    /// <remarks>
    /// For each argument in <paramref name="formattableQuery"/>, this method:
    /// - If the argument is of type <c>Sql</c>, leaves the fragment inline in the query text.
    /// - Otherwise, creates a named parameter (e.g. <c>@0</c>, <c>@1</c>) and replaces the argument with the parameter token.
    /// The implementation avoids extra temporary collections by computing the parameter count first and allocating a single <see cref="SqliteParameter"/> array.
    /// </remarks>
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

    /// <summary>
    /// Creates and opens a <see cref="SqliteConnection"/> using the current connection string from <see cref="connectionStringOptions"/>.
    /// </summary>
    /// <param name="token">Cancellation token used while opening the connection.</param>
    /// <returns>An opened <see cref="SqliteConnection"/> instance. Caller is responsible for disposing it.</returns>
    private async Task<SqliteConnection> GetOpenedConnectionAsync(CancellationToken token)
    {
        var conn = new SqliteConnection(connectionStringOptions.CurrentValue.Default);

        await conn.OpenAsync(token).ConfigureAwait(false);

        return conn;
    }
}