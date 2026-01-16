namespace BackgroundEmailSenderSample.Models.Exceptions.Infrastructure;

public class ConstraintViolationException(Exception innerException)
    : Exception($"A violation occurred for a database constraint", innerException)
{ }