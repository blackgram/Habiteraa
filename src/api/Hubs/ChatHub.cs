using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Habitera.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IChatService _chatService;
        private readonly IChatPresenceService _presence;

        public ChatHub(IChatService chatService, IChatPresenceService presence)
        {
            _chatService = chatService;
            _presence = presence;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = GetUserId();
            if (userId == null)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId.Value));
            var becameOnline = await _presence.MarkConnectedAsync(userId.Value);
            if (becameOnline)
            {
                await BroadcastPresenceAsync(userId.Value, true);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserId();
            if (userId != null)
            {
                var wentOffline = await _presence.MarkDisconnectedAsync(userId.Value);
                if (wentOffline)
                {
                    await BroadcastPresenceAsync(userId.Value, false);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinConversation(Guid conversationId)
        {
            var userId = RequireUserId();
            var membership = await _chatService.GetMembershipAsync(userId, conversationId);
            if (membership == null)
            {
                throw new HubException("Conversation not found");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

            var delivered = await _chatService.MarkDeliveredAsync(userId, conversationId);
            if (delivered.Success && delivered.Data is { Count: > 0 })
            {
                await Clients.Group(ConversationGroup(conversationId)).SendAsync(
                    "MessageDelivered",
                    new
                    {
                        conversationId,
                        messageIds = delivered.Data,
                        deliveredAt = DateTime.UtcNow
                    });
            }
        }

        public async Task LeaveConversation(Guid conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
        }

        public async Task<ChatMessageDTO> SendMessage(Guid conversationId, string body)
        {
            var userId = RequireUserId();
            var result = await _chatService.SendMessageAsync(userId, conversationId, body);
            if (!result.Success || result.Data == null)
            {
                throw new HubException(result.Message);
            }

            var membership = await _chatService.GetMembershipAsync(userId, conversationId);
            var recipientId = membership == null
                ? (Guid?)null
                : membership.UserId == userId ? membership.AgentId : membership.UserId;

            await Clients.Group(ConversationGroup(conversationId)).SendAsync("MessageReceived", result.Data);

            if (recipientId.HasValue)
            {
                await Clients.Group(UserGroup(recipientId.Value)).SendAsync("MessageReceived", result.Data);

                if (await _presence.IsOnlineAsync(recipientId.Value))
                {
                    var delivered = await _chatService.MarkDeliveredAsync(recipientId.Value, conversationId);
                    if (delivered.Success && delivered.Data is { Count: > 0 })
                    {
                        await Clients.Group(UserGroup(userId)).SendAsync(
                            "MessageDelivered",
                            new
                            {
                                conversationId,
                                messageIds = delivered.Data,
                                deliveredAt = DateTime.UtcNow
                            });
                    }
                }
            }

            return result.Data;
        }

        public async Task Typing(Guid conversationId, bool isTyping)
        {
            var userId = RequireUserId();
            var membership = await _chatService.GetMembershipAsync(userId, conversationId);
            if (membership == null)
            {
                throw new HubException("Conversation not found");
            }

            await Clients.OthersInGroup(ConversationGroup(conversationId)).SendAsync(
                "Typing",
                new { conversationId, userId, isTyping });
        }

        public async Task MarkRead(Guid conversationId, Guid? upToMessageId = null)
        {
            var userId = RequireUserId();
            var result = await _chatService.MarkReadAsync(userId, conversationId, upToMessageId);
            if (!result.Success)
            {
                throw new HubException(result.Message);
            }

            if (result.Data is { Count: > 0 })
            {
                await Clients.Group(ConversationGroup(conversationId)).SendAsync(
                    "MessageRead",
                    new
                    {
                        conversationId,
                        readerId = userId,
                        messageIds = result.Data,
                        readAt = DateTime.UtcNow
                    });
            }
        }

        private async Task BroadcastPresenceAsync(Guid userId, bool online)
        {
            var partners = await _chatService.GetPartnerIdsAsync(userId);
            var payload = new { userId, online };
            foreach (var partnerId in partners)
            {
                await Clients.Group(UserGroup(partnerId)).SendAsync("PresenceChanged", payload);
            }
        }

        private Guid RequireUserId()
        {
            return GetUserId() ?? throw new HubException("Unauthorized");
        }

        private Guid? GetUserId()
        {
            var raw = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }

        private static string UserGroup(Guid userId) => $"user:{userId:N}";
        private static string ConversationGroup(Guid conversationId) => $"conversation:{conversationId:N}";
    }
}
