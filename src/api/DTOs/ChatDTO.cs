using System.ComponentModel.DataAnnotations;

namespace Habitera.DTOs
{
    public class StartConversationRequestDTO
    {
        [Required]
        public Guid AgentId { get; set; }

        [Required]
        public Guid PropertyId { get; set; }
    }

    public class ConversationSummaryDTO
    {
        public Guid Id { get; set; }
        public Guid OtherUserId { get; set; }
        public string OtherUserName { get; set; } = string.Empty;
        public string? OtherUserPhoto { get; set; }
        public Guid? PropertyId { get; set; }
        public string? PropertyTitle { get; set; }
        public string? PropertyImageUrl { get; set; }
        public string? LastMessagePreview { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public int UnreadCount { get; set; }
        public bool OtherUserOnline { get; set; }
    }

    public class ChatMessageDTO
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }

        /// <summary>sent | delivered | read — meaningful for the sender's own messages.</summary>
        public string Status { get; set; } = "sent";
    }

    public class SendMessageRequestDTO
    {
        [Required]
        public Guid ConversationId { get; set; }

        [Required, MaxLength(4000)]
        public string Body { get; set; } = string.Empty;
    }
}
