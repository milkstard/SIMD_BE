using System.Collections.Concurrent;

namespace IncidentHub.Api.Hubs;

/// <summary>
/// At most one <c>DashboardChanged</c> per second per team (API-CONTRACT §7). The first change sends at once and opens a one-second
/// window; changes inside the window are coalesced into one trailing send when it ends. A quiet team sends nothing. The cap is per
/// API instance (decided 2026-10-13); the timers come from <see cref="TimeProvider"/> so tests control time.
/// </summary>
public sealed class DashboardThrottle(TimeProvider time, IDashboardBroadcaster broadcaster, ILogger<DashboardThrottle> logger)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<Guid, Slot> _slots = new();

    public void Notify(Guid teamId)
    {
        var slot = _slots.GetOrAdd(teamId, _ => new Slot());

        lock (slot)
        {
            if (slot.IsOpen)
            {
                slot.HasPending = true;
                return;
            }

            slot.IsOpen = true;
            slot.Timer = time.CreateTimer(_ => OnWindowEnd(teamId, slot), null, Window, Timeout.InfiniteTimeSpan);
        }

        _ = SendAsync(teamId);
    }

    private void OnWindowEnd(Guid teamId, Slot slot)
    {
        lock (slot)
        {
            if (!slot.HasPending)
            {
                slot.IsOpen = false;
                slot.Timer?.Dispose();
                slot.Timer = null;
                return;
            }

            // The trailing send opens the next window, so the cap holds across back-to-back bursts.
            slot.HasPending = false;
            slot.Timer?.Change(Window, Timeout.InfiniteTimeSpan);
        }

        _ = SendAsync(teamId);
    }

    private async Task SendAsync(Guid teamId)
    {
        try
        {
            await broadcaster.SendAsync(teamId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DashboardChanged for team {TeamId} failed", teamId);
        }
    }

    private sealed class Slot
    {
        public bool IsOpen { get; set; }

        public bool HasPending { get; set; }

        public ITimer? Timer { get; set; }
    }
}
