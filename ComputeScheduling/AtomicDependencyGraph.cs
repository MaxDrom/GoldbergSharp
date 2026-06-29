using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public class AtomicDependencyGraph
{
    private DependencyGraphBuilder _graphBuilder = new();
    private readonly Kahn<int> _graphUtils = new();
    private readonly List<int> _topological = [];
    private readonly ResourceHasher _resourceHasher = new();

    private Dictionary<ulong, IComputeResource> _reads = new();
    private Dictionary<ulong, IComputeResource> _writes = new();
    private int _lastId;
    private Dictionary<int, IComputeTask> _tasks = new();
    public void AddTask(IComputeTask task)
    {
        
        _graphBuilder.AddTask(_lastId, task);
        _tasks[_lastId] = task;
        _lastId++;
    }

    public void Clear()
    {
        _graphBuilder.Clear();
        _topological.Clear();
        _reads.Clear();
        _writes.Clear();
        _lastId = 0;
        _tasks.Clear();
    }
    
    public Task<ComplexTask> Compile()
    {
        var neigns = _graphBuilder.BuildGraph();
        _graphUtils.TopologicalSort(_graphBuilder.Tasks,
            task => neigns[task], _topological);

        var tasksTopological = new List<IComputeTask>();
        foreach (var id in _topological)
        {
            var task = _tasks[id];
            tasksTopological.Add(task);
            foreach (var read in task.Reads)
            {
                var hash = read.Accept(_resourceHasher);

                _reads.TryAdd(hash, read);
            }

            foreach (var write in task.Writes)
            {
                var hash = write.Accept(_resourceHasher);
                _writes[hash] = write;
            }
        }
        
        
        return Task.FromResult(new ComplexTask(_reads.Values.ToList(),
            _writes.Values.ToList(),
            PipelineStageFlags.AllGraphicsBit, tasksTopological));
    }
}

public class ComplexTask(List<IComputeResource> reads,
    List<IComputeResource> writes,
    PipelineStageFlags pipelineStage,
    List<IComputeTask> tasks
)
    : IComputeTask
{
    public List<IComputeResource> Reads { get; } = reads;
    public List<IComputeResource> Writes { get; } = writes;

    public PipelineStageFlags InitialPipelineStage { get; } =
        pipelineStage;

    public PipelineStageFlags InvokeRecord(
        VkCommandRecordingScope scope,
        Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
            bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout,
            PipelineStageFlags)> imageAccessFlags)
    {
        var computeRecorder =
            new ComputeRecorder(bufferAccessFlags, imageAccessFlags);
        return computeRecorder.Record(scope, tasks);
    }
}