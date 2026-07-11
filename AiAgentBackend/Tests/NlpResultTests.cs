using Xunit;
using FluentAssertions;
using AiAgentBackend.Services.NLP;

namespace AiAgentBackend.Tests.NLP
{
    public class NlpResultTests
    {
        [Fact]
        public void NlpResult_DefaultValues()
        {
            var result = new NlpResult();
            result.Intent.Should().Be("Unknown");
            result.Confidence.Should().Be(0.0);
            result.Entities.Should().NotBeNull();
            result.Entities.Should().BeEmpty();
        }

        [Fact]
        public void NlpResult_CanSetEntities()
        {
            var result = new NlpResult
            {
                Intent = "CreateTask",
                Confidence = 0.95,
                Entities = new Dictionary<string, string>
                {
                    ["title"] = "Test Task",
                    ["due"] = "2026-01-01"
                }
            };

            result.Entities.Should().ContainKey("title");
            result.Entities["title"].Should().Be("Test Task");
        }
    }
}
