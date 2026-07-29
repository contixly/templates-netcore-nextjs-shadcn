using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Template.Api.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}
