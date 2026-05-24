using Domain.Connections;

namespace Domain.Tests.Connections;

[TestClass]
public sealed class ConnectionLeaseTests
{
    [TestMethod]
    public void ToRedisValue_RoundTripsThroughParser()
    {
        var lease = new ConnectionLease("realtime-1", "abc123");

        var raw = lease.ToRedisValue();
        var parsed = ConnectionLease.TryParse(raw, out var restored);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(restored);
        Assert.AreEqual("realtime-1", restored.NodeId);
        Assert.AreEqual("abc123", restored.ConnectionId);
    }

    [TestMethod]
    public void TryParse_RejectsInvalidLeaseValues()
    {
        foreach (var value in new string?[] { null, string.Empty, "missing-separator", "node-only:", ":connection-only" })
        {
            var parsed = ConnectionLease.TryParse(value, out var lease);

            Assert.IsFalse(parsed);
            Assert.IsNull(lease);
        }
    }
}
