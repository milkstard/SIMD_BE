namespace IncidentHub.Application.Abstractions;

/// <summary>Strips scripts, event handlers and unsafe URLs from user-supplied rich text.</summary>
public interface IHtmlSanitizer
{
    string Sanitize(string html);
}
