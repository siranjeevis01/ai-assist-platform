using AiAgentBackend.Services.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VoiceController : ControllerBase
    {
        private readonly IVoiceService _voice;
        private readonly ILogger<VoiceController> _logger;

        public VoiceController(IVoiceService voice, ILogger<VoiceController> logger)
        {
            _voice = voice;
            _logger = logger;
        }

        [HttpPost("transcribe")]
        [RequestSizeLimit(25 * 1024 * 1024)]
        public async Task<IActionResult> Transcribe(IFormFile audio)
        {
            if (audio == null || audio.Length == 0)
                return BadRequest(new { error = "No audio file provided" });

            if (audio.Length > 25 * 1024 * 1024)
                return BadRequest(new { error = "Audio file too large (max 25MB)" });

            var allowedTypes = new[] { "audio/webm", "audio/wav", "audio/mp3", "audio/ogg", "audio/m4a", "audio/mp4" };
            if (!allowedTypes.Contains(audio.ContentType))
                return BadRequest(new { error = $"Unsupported audio format: {audio.ContentType}. Use webm, wav, mp3, or ogg." });

            using var ms = new MemoryStream();
            await audio.CopyToAsync(ms);
            var audioBytes = ms.ToArray();

            var transcription = await _voice.TranscribeAudioAsync(audioBytes, audio.ContentType);

            _logger.LogInformation("Voice transcription: {Transcription}", transcription[..Math.Min(100, transcription.Length)]);

            return Ok(new { text = transcription });
        }

        [HttpPost("synthesize")]
        public async Task<IActionResult> Synthesize([FromBody] SynthesizeRequest request)
        {
            if (string.IsNullOrEmpty(request.Text))
                return BadRequest(new { error = "No text provided" });

            var audio = await _voice.TextToSpeechAsync(request.Text, request.Language ?? "en");
            return File(audio, "audio/wav", "speech.wav");
        }
    }

    public class SynthesizeRequest
    {
        public string Text { get; set; } = string.Empty;
        public string? Language { get; set; }
    }
}
