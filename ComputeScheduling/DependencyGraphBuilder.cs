using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public class DependencyGraphBuilder
{
    private readonly Dictionary<ulong, int> _lastWriter = [];

    private readonly Dictionary<int, List<int>> _neighbours =
        [];

    private readonly Dictionary<ulong, List<int>> _lastReaders = [];

    private readonly ResourceHasher _resourceHasher = new();
    public List<int> Tasks { get; } = [];

    public void AddTask(int taskId, IComputeTask task)
    {
        Tasks.Add(taskId);
        _neighbours[taskId] = new List<int>();
        foreach (var resource in task.Writes)
        {
            var id = resource.Accept(_resourceHasher);
            if (_lastReaders.TryGetValue(id, out var readers))
            {
                foreach (var reader in readers)
                    _neighbours[taskId].Add(reader);
                _lastReaders.Remove(id);
            }

            if (_lastWriter.TryGetValue(id, out var lastWriter))
                _neighbours[taskId].Add(lastWriter);

            _lastWriter[id] = taskId;
        }

        foreach (var resource in task.Reads)
        {
            var id = resource.Accept(_resourceHasher);
            if (_lastWriter.TryGetValue(id, out var lastWriter) &&
                lastWriter != taskId)
                _neighbours[taskId].Add(lastWriter);
            if (!_lastReaders.ContainsKey(id))
                _lastReaders[id] = [];
            _lastReaders[id].Add(taskId);
        }
    }

    public Dictionary<int, List<int>> BuildGraph()
    {
        return _neighbours;
    }

    public void Clear()
    {
        Tasks.Clear();
        _lastReaders.Clear();
        _lastWriter.Clear();
        _neighbours.Clear();
    }
}

public class ResourceHasher : IComputeResourceVisitor<ulong>
{
    public ulong Visit(BufferResource resource)
    {
        return resource.Buffer.Buffer.Handle;
    }

    public ulong Visit(ImageResource resource)
    {
        return resource.Image.Image.Handle;
    }
}