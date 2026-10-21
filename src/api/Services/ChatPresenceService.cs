using StackExchange.Redis;

namespace Habitera.Services
{
    public interface IChatPresenceService
    {
        Task<bool> MarkConnectedAsync(Guid userId);
        Task<bool> MarkDisconnectedAsync(Guid userId);
        Task<bool> IsOnlineAsync(Guid userId);
        Task<Dictionary<Guid, bool>> AreOnlineAsync(IEnumerable<Guid> userIds);
    }

    public class ChatPresenceService : IChatPresenceService
    {
        private readonly IConnectionMultiplexer _redis;
        private static string Key(Guid userId) => $"chat:online:{userId:N}";

        public ChatPresenceService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<bool> MarkConnectedAsync(Guid userId)
        {
            var db = _redis.GetDatabase();
            var count = await db.StringIncrementAsync(Key(userId));
            await db.KeyExpireAsync(Key(userId), TimeSpan.FromHours(12));
            return count == 1;
        }

        public async Task<bool> MarkDisconnectedAsync(Guid userId)
        {
            var db = _redis.GetDatabase();
            var count = await db.StringDecrementAsync(Key(userId));
            if (count <= 0)
            {
                await db.KeyDeleteAsync(Key(userId));
                return true;
            }

            return false;
        }

        public async Task<bool> IsOnlineAsync(Guid userId)
        {
            var value = await _redis.GetDatabase().StringGetAsync(Key(userId));
            return value.HasValue && (long)value > 0;
        }

        public async Task<Dictionary<Guid, bool>> AreOnlineAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var result = new Dictionary<Guid, bool>(ids.Count);
            if (ids.Count == 0) return result;

            var db = _redis.GetDatabase();
            var keys = ids.Select(id => (RedisKey)Key(id)).ToArray();
            var values = await db.StringGetAsync(keys);

            for (var i = 0; i < ids.Count; i++)
            {
                var value = values[i];
                result[ids[i]] = value.HasValue && long.TryParse(value.ToString(), out var count) && count > 0;
            }

            return result;
        }
    }
}
