namespace BackgroundEmailSenderSample.Models.Exceptions.Infrastructure;

/// <summary>
/// Represents an exception that is thrown when a database constraint is violated.
/// </summary>
/// <remarks>This exception is typically thrown when an operation fails due to a violation of a database
/// constraint, such as a unique key or foreign key constraint. Use this exception to distinguish constraint violations
/// from other types of database errors.</remarks>
/// <param name="innerException">The exception that caused the current exception. This value cannot be null.</param>
public class ConstraintViolationException(Exception innerException) : Exception($"A violation occurred for a database constraint", innerException)
{ }