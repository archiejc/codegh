using FluentAssertions;
using LiveCanvas.Bridge.Protocol.Serialization;
using LiveCanvas.Contracts.Session;

namespace LiveCanvas.Bridge.Protocol.Tests;

public class BridgeProtocolTests
{
    [Fact]
    public void json_rpc_envelope_round_trips_request_ids()
    {
        var json = BridgeJsonSerializer.SerializeRequest("req-123", "gh_session_info", new GhSessionInfoRequest());
        var envelope = BridgeJsonSerializer.DeserializeRequest(json);

        envelope.Id.Should().Be("req-123");
        envelope.Method.Should().Be("gh_session_info");
    }

    [Fact]
    public void bridge_error_maps_to_error_shape()
    {
        var json = BridgeJsonSerializer.SerializeError("req-123", "session_unavailable", "Rhino is not running.");
        var envelope = BridgeJsonSerializer.DeserializeResponse(json);

        envelope.Error.Should().NotBeNull();
        envelope.Error!.Code.Should().Be("session_unavailable");
    }
}
