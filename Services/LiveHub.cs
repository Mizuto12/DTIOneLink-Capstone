using Microsoft.AspNetCore.SignalR;

namespace DTIOneLink.Services
{
    // SignalR hub at /hubs/live. Pages only listen: LiveChangeBroadcaster
    // sends "dataChanged" to everyone and "notificationsChanged" to the one
    // user it concerns (group "user-{id}").
    public class LiveHub : Hub
    {
        private static int _connections;

        // The broadcaster skips its database check while nobody is connected.
        public static bool AnyoneConnected => Volatile.Read(ref _connections) > 0;

        public static string UserGroup(int userId) => $"user-{userId}";

        public override async Task OnConnectedAsync()
        {
            // Same sign-in as the pages: the session set at login.
            var userId = Context.GetHttpContext()?.Session.GetInt32("UserId");
            if (userId == null)
            {
                Context.Abort();
                return;
            }

            Context.Items["counted"] = true;
            Interlocked.Increment(ref _connections);
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));
            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.ContainsKey("counted"))
            {
                Interlocked.Decrement(ref _connections);
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
