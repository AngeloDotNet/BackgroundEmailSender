namespace BackgroundEmailSenderSample.Models.Enums;

/// <summary>
/// Represents the lifecycle status of an outgoing mail item.
/// </summary>
/// <remarks>
/// Use this enum to track message state in storage and background processing pipelines.
/// The underlying type is <see cref="System.Byte"/> for compact storage.
/// </remarks>
public enum MailStatus : byte
{
    /// <summary>
    /// The message is currently being processed/sent.
    /// </summary>
    InProgress = 0,

    /// <summary>
    /// The message was sent successfully.
    /// </summary>
    Sent = 1,

    /// <summary>
    /// The message was deleted or discarded and will not be sent.
    /// </summary>
    Deleted = 2
}