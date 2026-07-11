using Xunit;
using FluentAssertions;
using AiAgentBackend.Services.NLP;

namespace AiAgentBackend.Tests.NLP
{
    public class NlpServiceTests
    {
        private readonly NlpService _sut;

        public NlpServiceTests()
        {
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<NlpService>.Instance;
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            _sut = new NlpService(logger, config);
        }

        [Theory]
        [InlineData("schedule meeting tomorrow at 3pm", "CreateEvent")]
        [InlineData("create task buy groceries", "CreateTask")]
        [InlineData("remind me to call mom at 2pm", "CreateReminder")]
        [InlineData("show my tasks", "CheckTasks")]
        [InlineData("what's on my calendar today", "CheckCalendar")]
        [InlineData("check my emails", "EmailAction")]
        [InlineData("mark task done", "UpdateTask")]
        public async Task ParseAsync_RecognizesIntent(string input, string expectedIntent)
        {
            var result = await _sut.ParseAsync(input, "UTC");
            result.Intent.Should().Be(expectedIntent);
        }

        [Theory]
        [InlineData("tommorow meeting", "CreateEvent")]
        [InlineData("creat a task", "CreateTask")]
        [InlineData("schedual call", "CreateEvent")]
        [InlineData("remnder for report", "CreateReminder")]
        [InlineData("emial inbox", "EmailAction")]
        [InlineData("evnt tomorrow", "CreateEvent")]
        public async Task ParseAsync_HandlesTypos(string input, string expectedIntent)
        {
            var result = await _sut.ParseAsync(input, "UTC");
            result.Intent.Should().Be(expectedIntent);
        }

        [Fact]
        public async Task ParseAsync_CreateEvent_ExtractsTitle()
        {
            var result = await _sut.ParseAsync("schedule meeting tomorrow at 3pm", "UTC");
            result.Intent.Should().Be("CreateEvent");
            result.Entities.Should().ContainKey("title");
            result.Entities["title"].Should().Contain("meeting");
        }

        [Fact]
        public async Task ParseAsync_EmptyText_ReturnsUnknown()
        {
            var result = await _sut.ParseAsync("", "UTC");
            result.Intent.Should().Be("Unknown");
        }

        [Fact]
        public async Task ParseAsync_WhitespaceText_ReturnsUnknown()
        {
            var result = await _sut.ParseAsync("   ", "UTC");
            result.Intent.Should().Be("Unknown");
        }

        [Fact]
        public async Task ParseAsync_CheckCalendar_DoesNotExtractTitle()
        {
            var result = await _sut.ParseAsync("show my events today", "UTC");
            result.Intent.Should().Be("CheckCalendar");
            result.Entities.Should().NotContainKey("title");
        }

        [Fact]
        public async Task ParseAsync_CheckTasks_DoesNotExtractTitle()
        {
            var result = await _sut.ParseAsync("what are my tasks", "UTC");
            result.Intent.Should().Be("CheckTasks");
            result.Entities.Should().NotContainKey("title");
        }
    }
}
