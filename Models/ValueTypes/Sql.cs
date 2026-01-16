namespace BackgroundEmailSenderSample.Models.ValueTypes;

public sealed class Sql
{
    public string Value { get; }

    private Sql(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public static explicit operator Sql(string value) => new(value);

    public override string ToString() => Value;
}