using AiAgentBackend.Controllers;
using AiAgentBackend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;
using Xunit;

namespace AiAgentBackend.Tests.Controllers;

public class ExportControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ExportController _controller;

    public ExportControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        var logger = new Mock<ILogger<ExportController>>();
        _controller = new ExportController(_db, logger.Object);
    }

    private void SetUserId(int userId)
    {
        var claims = new List<Claim> { new Claim("uid", userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = principal }
        };
    }

    [Fact]
    public async Task ExportTasks_JsonFormat_ReturnsOkResult()
    {
        SetUserId(1);
        var result = await _controller.ExportTasks("json");
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ExportTasks_CsvFormat_ReturnsFileContentResult()
    {
        SetUserId(1);
        var result = await _controller.ExportTasks("csv");
        Assert.IsType<FileContentResult>(result);
    }

    [Fact]
    public async Task ExportTasks_Unauthenticated_ReturnsUnauthorized()
    {
        var controller = new ExportController(_db, Mock.Of<ILogger<ExportController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
        var result = await controller.ExportTasks("json");
        Assert.IsType<UnauthorizedResult>(result);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }
}
