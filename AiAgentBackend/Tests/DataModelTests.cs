using Xunit;
using FluentAssertions;
using AiAgentBackend.Services.Orchestration;

namespace AiAgentBackend.Tests.Orchestration
{
    public class DataModelTests
    {
        [Fact]
        public void EventCreationData_DefaultValues()
        {
            var data = new EventCreationData();
            data.Title.Should().Be(string.Empty);
            data.Attendees.Should().BeEmpty();
        }

        [Fact]
        public void TaskCreationData_DefaultValues()
        {
            var data = new TaskCreationData();
            data.Title.Should().Be(string.Empty);
            data.Priority.Should().Be("medium");
            data.Labels.Should().BeEmpty();
        }

        [Fact]
        public void EmailActionData_DefaultValues()
        {
            var data = new EmailActionData();
            data.To.Should().Be(string.Empty);
            data.Subject.Should().Be(string.Empty);
            data.Body.Should().Be(string.Empty);
        }
    }
}
