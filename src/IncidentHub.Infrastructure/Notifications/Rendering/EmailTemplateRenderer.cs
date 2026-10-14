using System.Text;
using System.Text.Encodings.Web;
using IncidentHub.Application.Abstractions;
using IncidentHub.Application.Notifications;
using Microsoft.Extensions.Options;

namespace IncidentHub.Infrastructure.Notifications.Rendering;

/// <summary>Plain string templates (no Razor). Every dynamic value is HTML-encoded in the HTML part.</summary>
public sealed class EmailTemplateRenderer(IOptions<EmailOptions> options) : IEmailTemplateRenderer
{
    private readonly string _baseUrl = options.Value.PublicBaseUrl.Trim().TrimEnd('/');

    public RenderedEmail Render(string headline, IReadOnlyList<(string Label, string Value)> facts, string incidentNumber)
    {
        var link = _baseUrl.Length == 0 ? null : $"{_baseUrl}/incidents/{Uri.EscapeDataString(incidentNumber)}";
        var encoder = HtmlEncoder.Default;

        var text = new StringBuilder();
        text.AppendLine(headline).AppendLine();
        foreach (var (label, value) in facts)
        {
            text.Append(label).Append(": ").AppendLine(value);
        }

        var html = new StringBuilder();
        html.Append("<html><body><h2>").Append(encoder.Encode(headline)).Append("</h2><table>");
        foreach (var (label, value) in facts)
        {
            html.Append("<tr><td><b>").Append(encoder.Encode(label)).Append("</b></td><td>").Append(encoder.Encode(value)).Append("</td></tr>");
        }

        html.Append("</table>");

        if (link is not null)
        {
            text.AppendLine().Append("Open ").Append(incidentNumber).Append(": ").AppendLine(link);
            html.Append("<p><a href=\"").Append(encoder.Encode(link)).Append("\">Open ").Append(encoder.Encode(incidentNumber)).Append("</a></p>");
        }

        html.Append("</body></html>");
        return new RenderedEmail(text.ToString(), html.ToString());
    }
}
