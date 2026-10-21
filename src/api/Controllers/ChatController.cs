using Habitera.DTOs;
using Habitera.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Habitera.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost("Conversations")]
        public async Task<IActionResult> StartConversation([FromBody] StartConversationRequestDTO request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var callerId = GetUserId();
            if (callerId == null)
            {
                return Unauthorized(new { message = "Invalid user" });
            }

            var result = await _chatService.GetOrCreateConversationAsync(callerId.Value, request);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Conversations")]
        public async Task<IActionResult> GetInbox()
        {
            var callerId = GetUserId();
            if (callerId == null)
            {
                return Unauthorized(new { message = "Invalid user" });
            }

            var result = await _chatService.GetInboxAsync(callerId.Value);
            return StatusCode(result.StatusCode, result);
        }

        [HttpGet("Conversations/{conversationId:guid}/Messages")]
        public async Task<IActionResult> GetMessages(
            Guid conversationId,
            [FromQuery] int take = 50,
            [FromQuery] DateTime? before = null)
        {
            var callerId = GetUserId();
            if (callerId == null)
            {
                return Unauthorized(new { message = "Invalid user" });
            }

            var result = await _chatService.GetMessagesAsync(callerId.Value, conversationId, take, before);
            return StatusCode(result.StatusCode, result);
        }

        private Guid? GetUserId()
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
