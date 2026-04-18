namespace BackgroundEmailSenderSample.Models.Services.Infrastructure;

/// <summary>
/// Defines methods for executing SQL queries and commands asynchronously against a database.
/// </summary>
/// <remarks>Implementations of this interface provide a way to execute parameterized SQL queries and commands
/// using interpolated strings, which helps prevent SQL injection. The methods support cancellation via a <see
/// cref="CancellationToken"/>. Typical usage involves passing a <see cref="FormattableString"/> representing the SQL
/// statement with embedded parameters.</remarks>
public interface IDatabaseAccessor
{
    /// <summary>
    /// Executes the specified SQL query asynchronously and returns the results as a DataSet.
    /// </summary>
    /// <remarks>Use this method to execute parameterized SQL queries in an asynchronous manner. The method
    /// automatically handles parameterization of values within the provided FormattableString, which helps prevent SQL
    /// injection vulnerabilities. The returned DataSet will contain one or more DataTable objects corresponding to the
    /// result sets produced by the query.</remarks>
    /// <param name="formattableQuery">A formattable SQL query string to execute. Parameters within the string are automatically parameterized to
    /// prevent SQL injection.</param>
    /// <param name="token">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a DataSet with the results of the
    /// query.</returns>
    Task<DataSet> QueryAsync(FormattableString formattableQuery, CancellationToken token = default);

    /// <summary>
    /// Asynchronously executes a scalar SQL query and returns the result as a value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the value to return from the scalar query result.</typeparam>
    /// <param name="formattableQuery">A SQL query represented as a FormattableString. The query should return a single value.</param>
    /// <param name="token">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the value returned by the scalar
    /// query, cast to the specified type.</returns>
    Task<T> QueryScalarAsync<T>(FormattableString formattableQuery, CancellationToken token = default);

    /// <summary>
    /// Executes the specified command asynchronously against the underlying data source.
    /// </summary>
    /// <remarks>Use this method to execute commands that do not return result sets, such as INSERT, UPDATE,
    /// or DELETE statements. The command text should use interpolation for parameters, which will be safely
    /// parameterized by the implementation.</remarks>
    /// <param name="formattableCommand">A composite format string representing the command to execute. Parameters within the string are automatically
    /// parameterized to help prevent SQL injection.</param>
    /// <param name="token">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the number of rows affected by the
    /// command.</returns>
    Task<int> CommandAsync(FormattableString formattableCommand, CancellationToken token = default);
}