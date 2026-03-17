using FluentAssertions;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.Tests.Validation;

public class ConnectionValidatorTests
{
    private readonly ConnectionValidator validator = new(new AllowedComponentRegistry());

    [Fact]
    public void accepts_known_legal_port_pair()
    {
        validator.IsValid(V0ComponentKeys.XyPlane, "P", V0ComponentKeys.Rectangle, "P").Should().BeTrue();
    }

    [Fact]
    public void rejects_unknown_output_name()
    {
        validator.IsValid(V0ComponentKeys.XyPlane, "bad", V0ComponentKeys.Rectangle, "P").Should().BeFalse();
    }

    [Fact]
    public void rejects_unknown_input_name()
    {
        validator.IsValid(V0ComponentKeys.XyPlane, "P", V0ComponentKeys.Rectangle, "bad").Should().BeFalse();
    }
}
