namespace IncidentHub.Infrastructure.Security;

/// <summary>
/// <see cref="IHtmlSanitizer"/> over the HtmlSanitizer package with its default allow-list: scripts, event-handler
/// attributes and <c>javascript:</c> URLs are removed. Safe to share across threads once configured.
/// </summary>
public sealed class HtmlSanitizerAdapter : IncidentHub.Application.Abstractions.IHtmlSanitizer
{
    private readonly Ganss.Xss.HtmlSanitizer _sanitizer = new();

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}
