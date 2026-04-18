using System.Runtime.CompilerServices;

namespace BackgroundEmailSenderSample.Models.ValueTypes;

/// <summary>
/// Lightweight non-null wrapper for SQL text values. Using a dedicated type makes intent explicit
/// and reduces accidental misuse of plain strings.
/// </summary>
public sealed class Sql(string value)
{
    /// <summary>
    /// The SQL value. Guaranteed non-null for instances created via the explicit operator or the ctor.
    /// </summary>
    public string Value { get; } = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Create a <see cref="Sql"/> from a <see cref="string"/> explicitly.
    /// </summary>
    public static explicit operator Sql(string value)
        => new(value);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => Value;
}