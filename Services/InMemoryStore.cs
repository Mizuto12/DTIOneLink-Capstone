using System.Collections.Concurrent;
using DTIOneLink.Models;

namespace DTIOneLink.Services
{
    public static class InMemoryStore
    {
        private static readonly ConcurrentDictionary<int, TaskItem> _tasks = new();
        private static readonly ConcurrentDictionary<int, UserItem> _users = new();
        private static int _nextTaskId = 1;
        private static int _nextUserId = 1;

        // ── Tasks ──────────────────────────────────────────

        public static List<TaskItem> GetAllTasks()
        {
            return _tasks.Values
                .OrderByDescending(t => t.CreatedAt)
                .ToList();
        }

        public static TaskItem? GetTaskById(int id)
        {
            _tasks.TryGetValue(id, out var task);
            return task;
        }

        public static TaskItem AddTask(TaskItem task)
        {
            var id = Interlocked.Increment(ref _nextTaskId) - 1;
            task.Id = id;
            task.CreatedAt = DateTime.UtcNow;
            _tasks[id] = task;
            return task;
        }

        // ── Users ──────────────────────────────────────────

        public static List<UserItem> GetAllUsers()
        {
            return _users.Values
                .OrderByDescending(u => u.CreatedAt)
                .ToList();
        }

        public static UserItem? GetUserById(int id)
        {
            _users.TryGetValue(id, out var user);
            return user;
        }

        public static UserItem AddUser(UserItem user)
        {
            var id = Interlocked.Increment(ref _nextUserId) - 1;
            user.Id = id;
            user.CreatedAt = DateTime.UtcNow;
            _users[id] = user;
            return user;
        }
    }
}
