namespace MediPOS.E2E;

public sealed class BrowserPreflightTests
{
    [Theory]
    [InlineData("ConnectionStrings__MediPosDatabase")]
    [InlineData("CUSTOMCONNSTR_MediPosDatabase")]
    [InlineData("Authentication:Google:ClientSecret")]
    [InlineData("AspNetCore_Urls")]
    [InlineData("DOCKER_HOST")]
    [InlineData("TESTCONTAINERS_HOST_OVERRIDE")]
    [InlineData("PGHOST")]
    [InlineData("ForwardedHeaders_Enabled")]
    public void RefusesAmbientOverrides(string key) =>
        Assert.Throws<InvalidOperationException>(() => BrowserHost.RejectAmbientConfiguration([key]));

    [Fact]
    public void PermitsRunnerInputsWithoutOperationalConfiguration() =>
        BrowserHost.RejectAmbientConfiguration(["MEDIPOS_E2E_BUILD", "MEDIPOS_E2E_SHA", "PATH", "DOTNET_NOLOGO"]);
}
