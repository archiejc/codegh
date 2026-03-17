using System.Collections.Concurrent;

namespace LiveCanvas.AgentHost.ToolState;

public sealed class ComponentSessionState
{
    private readonly ConcurrentDictionary<string, string> componentKeysById = new(StringComparer.Ordinal);

    public void Track(string componentId, string componentKey)
    {
        componentKeysById[componentId] = componentKey;
    }

    public bool TryGetComponentKey(string componentId, out string? componentKey) =>
        componentKeysById.TryGetValue(componentId, out componentKey);

    public void Remove(string componentId)
    {
        componentKeysById.TryRemove(componentId, out _);
    }

    public void Clear()
    {
        componentKeysById.Clear();
    }
}
