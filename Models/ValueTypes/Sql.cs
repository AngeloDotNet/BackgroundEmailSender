using System.Runtime.CompilerServices;

namespace BackgroundEmailSenderSample.Models.ValueTypes;

public sealed class Sql
{
    /// <summary>
    /// The SQL value. Guaranteed non-null for instances created via the explicit operator or the ctor.
    /// </summary>
    public string Value { get; }

    private Sql(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>
    /// Create a <see cref="Sql"/> from a <see cref="string"/> explicitly.
    /// </summary>
    public static explicit operator Sql(string value)
        => new Sql(value);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
        => Value;
}