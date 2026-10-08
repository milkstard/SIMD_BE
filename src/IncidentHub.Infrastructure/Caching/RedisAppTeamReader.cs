using IncidentHub.Application.Abstractions;
using IncidentHub.Infrastructure.Persistence;
using IncidentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Caching;

/// <summary>Team membership reader: Redis first (30 s TTL), SQL on miss.</summary>
public sealed class RedisAppTeamReader(AppDbContext db, IConnectionMultiplexer redis) : IAppTeamReader
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public async Task<bool> IsMemberAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("team.is_member");
        var cache = redis.GetDatabase();
        var key = MemberKey(applicationId, userId);

        var cached = await cache.StringGetAsync(key);
        if (cached.HasValue)
        {
            activity?.SetTag("cache.hit", true);
            return (bool)cached;
        }

        activity?.SetTag("cache.hit", false);
        var isMember = await db.AppTeamMembers
            .AsNoTracking()
            .AnyAsync(m => m.ApplicationId == applicationId && m.UserId == userId, cancellationToken);

        await cache.StringSetAsync(key, isMember, Ttl);
        return isMember;
    }

    public async Task<IReadOnlyList<Guid>> GetApplicationIdsForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.Source.StartActivity("team.apps_for_user");
        return await db.AppTeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.ApplicationId)
            .ToListAsync(cancellationToken);
    }

    public async Task InvalidateAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().KeyDeleteAsync(MemberKey(applicationId, userId));
    }

    private static RedisKey MemberKey(Guid applicationId, Guid userId) => $"team:{applicationId:N}:{userId:N}";
}
