using FluentAssertions;

namespace LiveCanvas.Contracts.Tests;

public class SolutionReferenceTests
{
    [Fact]
    public void contracts_assembly_exports_marker_interface()
    {
        typeof(LiveCanvas.Contracts.IToolRequest).Should().NotBeNull();
    }
}
