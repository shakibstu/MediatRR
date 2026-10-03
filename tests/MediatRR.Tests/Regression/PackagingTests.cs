namespace MediatRR.Tests.Regression;

public class PackagingTests
{
    [Fact]
    public void Runtime_assembly_types_can_be_enumerated()
    {
        var types = typeof(IMediator).Assembly.GetTypes();

        Assert.NotEmpty(types);
        Assert.DoesNotContain(types, type => type.FullName == "MediatRR.RegisterRequestHandlers");
    }
}
