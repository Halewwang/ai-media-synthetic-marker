using Emke.AiMarker.Integration.Tests.TestSupport;

namespace Emke.AiMarker.Integration.Tests;

public sealed class IntegrationServicesTests
{
    [Fact]
    public async Task Missing_executable_configuration_is_a_hard_failure()
    {
        foreach (string? executable in new string?[] { null, "", " " })
        {
            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => IntegrationServices.CreateAsync(executable));

            Assert.Equal("EMKE_EXIFTOOL is required.", exception.Message);
        }
    }
}
