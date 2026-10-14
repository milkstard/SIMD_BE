using IncidentHub.Application.Notifications;

namespace IncidentHub.Application.Abstractions;

/// <summary>Turns a headline and facts into the text and HTML parts of an email. Every dynamic value is HTML-encoded in the HTML part.</summary>
public interface IEmailTemplateRenderer
{
    RenderedEmail Render(string headline, IReadOnlyList<(string Label, string Value)> facts, string incidentNumber);
}
