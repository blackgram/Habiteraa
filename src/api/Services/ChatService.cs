using Habitera.Data;
using Habitera.DTOs;
using Habitera.Models;
using Microsoft.EntityFrameworkCore;

namespace Habitera.Services
{
    public interface IChatService
    {
        Task<OperationResult<ConversationSummaryDTO>> GetOrCreateConversationAsync(
            Guid callerId, StartConversationRequestDTO request);

        Task<OperationResult<List<ConversationSummaryDTO>>> GetInboxAsync(Guid callerId);

        Task<OperationResult<List<ChatMessageDTO>>> GetMessagesAsync(
            Guid callerId, Guid conversationId, int take = 50, DateTime? before = null);

        Task<OperationResult<ChatMessageDTO>> SendMessageAsync(
            Guid senderId, Guid conversationId, string body);

        Task<OperationResult<List<Guid>>> MarkDeliveredAsync(Guid readerId, Guid conversationId);

        Task<OperationResult<List<Guid>>> MarkReadAsync(
            Guid readerId, Guid conversationId, Guid? upToMessageId = null);

        Task<Conversation?> GetMembershipAsync(Guid callerId, Guid conversationId);

        Task<List<Guid>> GetPartnerIdsAsync(Guid userId);
    }

    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _db;
        private readonly IChatPresenceService _presence;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            ApplicationDbContext db,
            IChatPresenceService presence,
            ILogger<ChatService> logger)
        {
            _db = db;
            _presence = presence;
            _logger = logger;
        }

        public async Task<OperationResult<ConversationSummaryDTO>> GetOrCreateConversationAsync(
            Guid callerId, StartConversationRequestDTO request)
        {
            var caller = await _db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == callerId);
            if (caller == null)
            {
                return OperationResult<ConversationSummaryDTO>.Failure("User not found", 404);
            }

            if (caller.UserType != UserType.User)
            {
                return OperationResult<ConversationSummaryDTO>.Failure(
                    "Only a user can start a conversation with an agent", 403);
            }

            if (request.AgentId == callerId)
            {
                return OperationResult<ConversationSummaryDTO>.Failure("Cannot chat with yourself", 400);
            }

            var agent = await _db.Users.Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Id == request.AgentId);
            if (agent == null || agent.UserType != UserType.Agent)
            {
                return OperationResult<ConversationSummaryDTO>.Failure("Agent not found", 404);
            }

            if (request.PropertyId == Guid.Empty)
            {
                return OperationResult<ConversationSummaryDTO>.Failure("A listing is required", 400);
            }

            var property = await _db.Properties
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.PropertyId);
            if (property == null)
            {
                return OperationResult<ConversationSummaryDTO>.Failure("Property not found", 404);
            }

            if (property.AgentId != request.AgentId)
            {
                return OperationResult<ConversationSummaryDTO>.Failure(
                    "This agent does not own the listing", 400);
            }

            var existing = await _db.Conversations
                .Include(c => c.User).ThenInclude(u => u.Profile)
                .Include(c => c.Agent).ThenInclude(u => u.Profile)
                .Include(c => c.Property).ThenInclude(p => p!.Images)
                .FirstOrDefaultAsync(c =>
                    c.UserId == callerId &&
                    c.AgentId == request.AgentId &&
                    c.PropertyId == request.PropertyId);

            if (existing == null)
            {
                existing = new Conversation
                {
                    Id = Guid.NewGuid(),
                    UserId = callerId,
                    AgentId = request.AgentId,
                    PropertyId = request.PropertyId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Conversations.Add(existing);
                await _db.SaveChangesAsync();

                existing = await _db.Conversations
                    .Include(c => c.User).ThenInclude(u => u.Profile)
                    .Include(c => c.Agent).ThenInclude(u => u.Profile)
                    .Include(c => c.Property).ThenInclude(p => p!.Images)
                    .FirstAsync(c => c.Id == existing.Id);
            }

            return OperationResult<ConversationSummaryDTO>.Successful(
                await ToSummaryAsync(existing, callerId),
                "Conversation ready");
        }

        public async Task<OperationResult<List<ConversationSummaryDTO>>> GetInboxAsync(Guid callerId)
        {
            var conversations = await _db.Conversations
                .Include(c => c.User).ThenInclude(u => u.Profile)
                .Include(c => c.Agent).ThenInclude(u => u.Profile)
                .Include(c => c.Property).ThenInclude(p => p!.Images)
                .Where(c => c.UserId == callerId || c.AgentId == callerId)
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .ToListAsync();

            var summaries = new List<ConversationSummaryDTO>(conversations.Count);
            foreach (var conversation in conversations)
            {
                summaries.Add(await ToSummaryAsync(conversation, callerId));
            }

            return OperationResult<List<ConversationSummaryDTO>>.Successful(summaries);
        }

        public async Task<OperationResult<List<ChatMessageDTO>>> GetMessagesAsync(
            Guid callerId, Guid conversationId, int take = 50, DateTime? before = null)
        {
            var membership = await GetMembershipAsync(callerId, conversationId);
            if (membership == null)
            {
                return OperationResult<List<ChatMessageDTO>>.Failure("Conversation not found", 404);
            }

            take = Math.Clamp(take, 1, 100);

            var query = _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversationId);

            if (before.HasValue)
            {
                query = query.Where(m => m.CreatedAt < before.Value);
            }

            var messages = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(take)
                .ToListAsync();

            messages.Reverse();

            return OperationResult<List<ChatMessageDTO>>.Successful(
                messages.Select(m => ToMessageDto(m, callerId)).ToList());
        }

        public async Task<OperationResult<ChatMessageDTO>> SendMessageAsync(
            Guid senderId, Guid conversationId, string body)
        {
            var trimmed = body?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return OperationResult<ChatMessageDTO>.Failure("Message cannot be empty", 400);
            }

            if (trimmed.Length > 4000)
            {
                return OperationResult<ChatMessageDTO>.Failure("Message is too long", 400);
            }

            var conversation = await GetMembershipAsync(senderId, conversationId);
            if (conversation == null)
            {
                return OperationResult<ChatMessageDTO>.Failure("Conversation not found", 404);
            }

            var message = new ChatMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                SenderId = senderId,
                Body = trimmed,
                CreatedAt = DateTime.UtcNow
            };

            conversation.LastMessagePreview = trimmed.Length > 180 ? trimmed[..180] : trimmed;
            conversation.LastMessageAt = message.CreatedAt;

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Chat message {MessageId} in conversation {ConversationId} from {SenderId}",
                message.Id, conversationId, senderId);

            return OperationResult<ChatMessageDTO>.Successful(
                ToMessageDto(message, senderId),
                "Message sent",
                201);
        }

        public async Task<OperationResult<List<Guid>>> MarkDeliveredAsync(Guid readerId, Guid conversationId)
        {
            var membership = await GetMembershipAsync(readerId, conversationId);
            if (membership == null)
            {
                return OperationResult<List<Guid>>.Failure("Conversation not found", 404);
            }

            var pending = await _db.ChatMessages
                .Where(m =>
                    m.ConversationId == conversationId &&
                    m.SenderId != readerId &&
                    m.DeliveredAt == null)
                .ToListAsync();

            if (pending.Count == 0)
            {
                return OperationResult<List<Guid>>.Successful(new List<Guid>());
            }

            var now = DateTime.UtcNow;
            foreach (var message in pending)
            {
                message.DeliveredAt = now;
            }

            await _db.SaveChangesAsync();
            return OperationResult<List<Guid>>.Successful(pending.Select(m => m.Id).ToList());
        }

        public async Task<OperationResult<List<Guid>>> MarkReadAsync(
            Guid readerId, Guid conversationId, Guid? upToMessageId = null)
        {
            var membership = await GetMembershipAsync(readerId, conversationId);
            if (membership == null)
            {
                return OperationResult<List<Guid>>.Failure("Conversation not found", 404);
            }

            DateTime? cutoff = null;
            if (upToMessageId.HasValue)
            {
                cutoff = await _db.ChatMessages
                    .Where(m => m.Id == upToMessageId.Value && m.ConversationId == conversationId)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefaultAsync();
            }

            var query = _db.ChatMessages.Where(m =>
                m.ConversationId == conversationId &&
                m.SenderId != readerId &&
                m.ReadAt == null);

            if (cutoff.HasValue)
            {
                query = query.Where(m => m.CreatedAt <= cutoff.Value);
            }

            var pending = await query.ToListAsync();
            if (pending.Count == 0)
            {
                return OperationResult<List<Guid>>.Successful(new List<Guid>());
            }

            var now = DateTime.UtcNow;
            foreach (var message in pending)
            {
                message.DeliveredAt ??= now;
                message.ReadAt = now;
            }

            await _db.SaveChangesAsync();
            return OperationResult<List<Guid>>.Successful(pending.Select(m => m.Id).ToList());
        }

        public Task<Conversation?> GetMembershipAsync(Guid callerId, Guid conversationId)
        {
            return _db.Conversations.FirstOrDefaultAsync(c =>
                c.Id == conversationId && (c.UserId == callerId || c.AgentId == callerId));
        }

        public async Task<List<Guid>> GetPartnerIdsAsync(Guid userId)
        {
            var rows = await _db.Conversations
                .AsNoTracking()
                .Where(c => c.UserId == userId || c.AgentId == userId)
                .Select(c => new { c.UserId, c.AgentId })
                .ToListAsync();

            return rows
                .Select(c => c.UserId == userId ? c.AgentId : c.UserId)
                .Distinct()
                .ToList();
        }

        private async Task<ConversationSummaryDTO> ToSummaryAsync(Conversation conversation, Guid callerId)
        {
            var other = conversation.UserId == callerId ? conversation.Agent : conversation.User;
            var otherId = other?.Id ?? (conversation.UserId == callerId ? conversation.AgentId : conversation.UserId);
            var name = other?.Profile == null
                ? other?.Email ?? "Chat"
                : $"{other.Profile.FirstName} {other.Profile.LastName}".Trim();

            var unread = await _db.ChatMessages.CountAsync(m =>
                m.ConversationId == conversation.Id &&
                m.SenderId != callerId &&
                m.ReadAt == null);

            return new ConversationSummaryDTO
            {
                Id = conversation.Id,
                OtherUserId = otherId,
                OtherUserName = string.IsNullOrWhiteSpace(name) ? "Chat" : name,
                OtherUserPhoto = other?.ProfilePhoto,
                PropertyId = conversation.PropertyId,
                PropertyTitle = conversation.Property?.Title,
                PropertyImageUrl = FirstImageUrl(conversation.Property),
                LastMessagePreview = conversation.LastMessagePreview,
                LastMessageAt = conversation.LastMessageAt,
                UnreadCount = unread,
                OtherUserOnline = await _presence.IsOnlineAsync(otherId)
            };
        }

        private static string? FirstImageUrl(Property? property)
        {
            if (property?.Images == null || property.Images.Count == 0)
            {
                return null;
            }

            var primary = property.Images.FirstOrDefault(image => image.IsPrimary);
            if (!string.IsNullOrWhiteSpace(primary?.ImageUrl))
            {
                return primary.ImageUrl;
            }

            return property.Images
                .OrderBy(image => image.DisplayOrder)
                .Select(image => image.ImageUrl)
                .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
        }

        private static ChatMessageDTO ToMessageDto(ChatMessage message, Guid viewerId)
        {
            var isMine = message.SenderId == viewerId;
            var status = "sent";
            if (isMine)
            {
                if (message.ReadAt != null) status = "read";
                else if (message.DeliveredAt != null) status = "delivered";
            }

            return new ChatMessageDTO
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                SenderId = message.SenderId,
                Body = message.Body,
                CreatedAt = message.CreatedAt,
                DeliveredAt = message.DeliveredAt,
                ReadAt = message.ReadAt,
                Status = status
            };
        }
    }
}
