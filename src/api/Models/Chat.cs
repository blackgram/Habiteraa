using System.ComponentModel.DataAnnotations;

namespace Habitera.Models
{
    public class Conversation
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid AgentId { get; set; }
        public Guid? PropertyId { get; set; }

        [MaxLength(200)]
        public string? LastMessagePreview { get; set; }

        public DateTime? LastMessageAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApplicationUser User { get; set; } = null!;
        public ApplicationUser Agent { get; set; } = null!;
        public Property? Property { get; set; }
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessage
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid SenderId { get; set; }

        [MaxLength(4000)]
        public string Body { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }

        public Conversation Conversation { get; set; } = null!;
    }
}
