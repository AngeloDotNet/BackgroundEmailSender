namespace BackgroundEmailSenderSample.Models.InputModels;

/// <summary>
/// Represents the input model for a contact form submitted by a user.
/// </summary>
/// <remarks>
/// This model contains validation attributes that are used by model binding and
/// validation in ASP.NET Core Razor Pages. Property annotations include
/// <see cref="System.ComponentModel.DataAnnotations.RequiredAttribute"/>,
/// <see cref="System.ComponentModel.DataAnnotations.StringLengthAttribute"/>,
/// and <see cref="System.ComponentModel.DataAnnotations.EmailAddressAttribute"/>.
/// </remarks>
public class ContactInputModel
{
    /// <summary>
    /// The sender's full name. This field is required and limited to 30 characters.
    /// </summary>
    /// <remarks>
    /// Display name: "Your first and last name".
    /// </remarks>
    //Mandatory, 30 chars maximum
    [Required, StringLength(30), Display(Name = "Your first and last name")]
    public string Name { get; set; }

    /// <summary>
    /// The sender's e-mail address. This field is required and must be a valid e-mail address.
    /// </summary>
    /// <remarks>
    /// Display name: "E-mail address".
    /// </remarks>
    //Mandatory, must be a valid e-mail address
    [Required, EmailAddress, Display(Name = "E-mail address")]
    public string Email { get; set; }

    /// <summary>
    /// How the sender heard about the site or service. This field is required and limited to 100 characters.
    /// </summary>
    /// <remarks>
    /// Display name: "How did you hear about us?".
    /// </remarks>
    [Required, StringLength(100), Display(Name = "How did you hear about us?")]
    public string Source { get; set; }

    /// <summary>
    /// The message content provided by the sender. This field is required and limited to 1000 characters.
    /// </summary>
    /// <remarks>
    /// Display name: "Your message to us".
    /// Consider encoding or sanitizing <see cref="Message"/> before rendering it into HTML to avoid XSS vulnerabilities.
    /// </remarks>
    //Mandatory, 1000 chars maximum
    [Required, StringLength(1000), Display(Name = "Your message to us")]
    public string Message { get; set; }
    
    /// <summary>
    /// Renders the contact input as a simple HTML document fragment.
    /// </summary>
    /// <returns>
    /// A HTML string containing the name, source and message fields.
    /// </returns>
    /// <remarks>
    /// The returned string is not HTML-encoded. If the values of properties may
    /// contain user-provided content, encode them (for example with
    /// <c>System.Net.WebUtility.HtmlEncode</c>) before calling this method or
    /// update this method to perform encoding to prevent cross-site scripting (XSS).
    /// </remarks>
    public string ToHtmlMessage()
    {
        return $@"<html><body>
        <p>Message from: {Name}</p>
        <p>Heard about us from: {Source}</p>
        <p>Message: {Message}</p>
        </body></html>";
    }
}