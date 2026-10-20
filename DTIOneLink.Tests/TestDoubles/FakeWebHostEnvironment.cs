using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace DTIOneLink.Tests.TestDoubles;

// Minimal IWebHostEnvironment so controller/service code that checks
// IsProduction()/IsEnvironment(...) can run in a unit test without a real host.
public class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "DTIOneLink.Tests";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
