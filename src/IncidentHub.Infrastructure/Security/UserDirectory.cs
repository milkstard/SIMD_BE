using IncidentHub.Application.Abstractions;
using IncidentHub.Domain.Users;
using IncidentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace IncidentHub.Infrastructure.Security;

/// <summary>Maps an Entra <c>oid</c> to the internal user id, creating the user on first sight.</summary>
public sealed class UserDirectory(AppDbContext db, IConnectionMultiplexer redis, TimeProvider clock) : IUserDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public async Task<Guid> EnsureUserAsync(
        string entraObjectId,
        string displayName,
        string email,
        CancellationToken cancellationToken)
    {
        var cache = redis.GetDatabase();
        var key = (RedisKey)$"user:{entraObjectId}";

        var cached = await cache.StringGetAsync(key);
        if (cached.HasValue && Guid.TryParse((string?)cached, out var cachedId))
        {
            return cachedId;
        }

        var now = clock.GetUtcNow();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, cancellationToken);

        if (user is null)
        {
            user = User.Create(entraObjectId, displayName, email, now);
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost a race with a concurrent first request for the same user; the unique index protects us.
                db.Entry(user).State = EntityState.Detached;
                user = await db.Users.AsNoTracking().FirstAsync(u => u.EntraObjectId == entraObjectId, cancellationToken);
            }
        }
        else
        {
            user.Touch(displayName, email, now);
            await db.SaveChangesAsync(cancellationToken);
        }

        await cache.StringSetAsync(key, user.Id.ToString(), Ttl);
        return user.Id;
    }
}
