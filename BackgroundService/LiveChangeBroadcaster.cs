using Microsoft.AspNetCore.SignalR;

namespace DTIOneLink.Services
{
    // Pushes live updates to open pages through SignalR (LiveHub).
    //
    // Once a minute, while anyone is connected, it reads the data
    // fingerprints once for everybody (LiveVersionService, a few ms) and
    // pushes only what changed: "dataChanged" to all pages, and
    // "notificationsChanged" to just the user who got a notification.
    // Watching the database rather than each save means every change is
    // caught — EF, raw SQL, background jobs — without touching the code
    // that saves it.
    //
    // Deliberately a minute, not a few seconds: this also coalesces bursts —
    // if everyone updates their task progress around the same time (e.g.
    // right before a deadline), that only ever costs one check and one
    // broadcast per minute, not one for every save, so the server and
    // everyone's browser aren't fighting to keep up with each other.
    public class LiveChangeBroadcaster : BackgroundService
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

        private readonly LiveVersionService _versions;
        private readonly IHubContext<LiveHub> _hub;
        private readonly ILogger<LiveChangeBroadcaster> _logger;

        private string? _lastData;
        private Dictionary<int, string> _lastNotifications = new();

        public LiveChangeBroadcaster(LiveVersionService versions, IHubContext<LiveHub> hub, ILogger<LiveChangeBroadcaster> logger)
        {
            _versions = versions;
            _hub = hub;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                if (!LiveHub.AnyoneConnected)
                {
                    // Start fresh next time: pages check the version
                    // themselves when they connect, so nothing is missed.
                    _lastData = null;
                    continue;
                }

                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Live update check failed; will retry.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task CheckAsync(CancellationToken ct)
        {
            var (data, notifications) = await _versions.GetAsync(null, ct);

            if (_lastData == null)
            {
                _lastData = data;
                _lastNotifications = notifications;
                return;
            }

            if (data != _lastData)
            {
                _lastData = data;
                await _hub.Clients.All.SendAsync("dataChanged", data, ct);
            }

            foreach (var userId in notifications.Keys.Union(_lastNotifications.Keys))
            {
                notifications.TryGetValue(userId, out var now);
                _lastNotifications.TryGetValue(userId, out var before);
                if (now != before)
                {
                    await _hub.Clients.Group(LiveHub.UserGroup(userId)).SendAsync("notificationsChanged", ct);
                }
            }
            _lastNotifications = notifications;
        }
    }
}
