using Microsoft.EntityFrameworkCore;

namespace IncidentHub.Infrastructure.Persistence;

/// <summary>SQL <c>SEQUENCE</c> definitions. <c>Incident.Number</c> (displayed as <c>INC-1001</c>) comes from <see cref="IncidentNumbers"/>.</summary>
public static class Sequences
{
    public const string Schema = "dbo";
    public const string IncidentNumbers = "IncidentNumbers";
    public const int IncidentNumbersStart = 1001;

    public static string NextIncidentNumberSql => $"NEXT VALUE FOR {Schema}.{IncidentNumbers}";

    public static ModelBuilder HasIncidentNumberSequence(this ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<int>(IncidentNumbers, Schema)
            .StartsAt(IncidentNumbersStart)
            .IncrementsBy(1);
        return modelBuilder;
    }
}
