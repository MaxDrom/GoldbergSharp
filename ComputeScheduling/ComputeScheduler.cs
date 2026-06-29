using System.Collections.Concurrent;
using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public class ComputeScheduler
{
    private static readonly Lock SyncRoot = new();
    private static ComputeScheduler _instance;


    private readonly ComputeRecorder _computeRecorder = new([], []);

    private readonly DependencyGraphBuilder _dependencyGraphBuilder =
        new();

    private readonly Kahn<int> _graphUtils = new();
    private readonly List<int> _topological = [];

    private ConcurrentDictionary<int, IComputeTask> _tasksId = new();

    private List<IComputeTask> _sortedTask = new();
    public static ComputeScheduler Instance
    {
        get
        {
            if (_instance != null)
                return _instance;
            lock (SyncRoot)
            {
                _instance ??= new ComputeScheduler();
            }

            return _instance;
        }
    }

    private readonly ConcurrentQueue<(int, IComputeTask)> _tasks = new();
    private int _lastId;
    public void AddTask(IComputeTask task)
    {
        var idx = Interlocked.Increment(ref _lastId) - 1;
        _tasksId[idx] = task;
        _tasks.Enqueue((idx, task));
    }

    public async Task RecordAll(
        VkCommandRecordingScope scope)
    {
        _lastId = 0;
        _topological.Clear();
        _sortedTask.Clear();
        foreach (var (id, task) in _tasks)
            _dependencyGraphBuilder.AddTask(id, task);
        var neighbours = _dependencyGraphBuilder.BuildGraph();
        var noCycles = await Task.Run(() =>
            _graphUtils.TopologicalSort(
                _dependencyGraphBuilder.Tasks,
                z =>
                    neighbours[z], _topological));

#if DEBUG
        if (!noCycles)
            throw new Exception("Compute graph has cycles.");
#endif


        foreach (var id in _topological)
            _sortedTask.Add(_tasksId[id]);
        _computeRecorder.Clear();
        _computeRecorder.Record(scope,
            _sortedTask);
        _dependencyGraphBuilder.Clear();
        _tasks.Clear();
        _tasksId.Clear();

    }
}

public class ComputeRecorder(
    Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)> bufferAccessFlags,
    Dictionary<VkImage, (AccessFlags, ImageLayout, PipelineStageFlags)> imageAccessFlags
) :
    IComputeResourceVisitor<(BufferMemoryBarrier?,
        VkImageMemoryBarrier?, PipelineStageFlags)>
{
    private readonly Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
        _bufferAccessFlags = bufferAccessFlags;

    private readonly Dictionary<VkImage, (AccessFlags, ImageLayout, PipelineStageFlags)>
        _imageAccessFlags = imageAccessFlags;

    private PipelineStageFlags _pipelineStage =
        PipelineStageFlags.TopOfPipeBit;

    private PipelineStageFlags _targetPipeline;

    public (BufferMemoryBarrier?, VkImageMemoryBarrier?, PipelineStageFlags) Visit(
        BufferResource resource)
    {
        var buffer = resource.Buffer;
        if (!_bufferAccessFlags.TryGetValue(buffer,
                out var val))
        {
            _bufferAccessFlags[buffer] = (resource.AccessFlags, _pipelineStage);
            //if (_pipelineStage != PipelineStageFlags.ConditionalRenderingBitExt)
                return (null, null, PipelineStageFlags.None);
            
            //val = (resource.AccessFlags, _pipelineStage);
        }

        var (srcAccessFlag, pipelineStageFlags) = val;
        if (srcAccessFlag == AccessFlags.ShaderReadBit &&
            resource.AccessFlags == AccessFlags.ShaderReadBit)
            return (null, null, PipelineStageFlags.None);

        var bufferMemoryBarrier = new BufferMemoryBarrier
        {
            Buffer = buffer.Buffer,
            DstAccessMask = resource.AccessFlags,
            SrcAccessMask =
                CheckSupport(_pipelineStage, srcAccessFlag)
                    ? srcAccessFlag
                    : AccessFlags.None,
            Offset = 0,
            SType = StructureType.BufferMemoryBarrier,
            Size = buffer.Size,
        };

        _bufferAccessFlags[buffer] = (resource.AccessFlags, _targetPipeline);

        return (bufferMemoryBarrier, null, pipelineStageFlags);
    }

    public (BufferMemoryBarrier?, VkImageMemoryBarrier?, PipelineStageFlags) Visit(
        ImageResource resource)
    {
        var image = resource.Image;
        if (!_imageAccessFlags.TryGetValue(image,
                out var val))
        {
            _imageAccessFlags[image] = (AccessFlags.None,
                image.LastLayout, _pipelineStage);
            val = _imageAccessFlags[image];
        }

        var (srcAccessFlags, srcLayout, pipelineStageFlags) = val;
        if (srcAccessFlags == AccessFlags.ShaderReadBit &&
            resource.AccessFlags == AccessFlags.ShaderReadBit &&
            srcLayout == resource.Layout)
            return (null, null, PipelineStageFlags.None);
        var imageMemoryBarrier = new VkImageMemoryBarrier
        {
            Image = image,
            DstAccessMask = resource.AccessFlags,
            SrcAccessMask =
                CheckSupport(_pipelineStage, srcAccessFlags)
                    ? srcAccessFlags
                    : AccessFlags.None,
            NewLayout = resource.Layout,
            SubresourceRange = new ImageSubresourceRange(
                ImageAspectFlags.ColorBit,
                0, 1, 0, 1),
        };

        _imageAccessFlags[image] =
            (resource.AccessFlags, resource.Layout, _targetPipeline);
        return (null, imageMemoryBarrier, pipelineStageFlags);
    }

    public void Clear()
    {
        _pipelineStage = PipelineStageFlags.TopOfPipeBit;
        _bufferAccessFlags.Clear();
        _imageAccessFlags.Clear();
    }

    public PipelineStageFlags Record(VkCommandRecordingScope scope,
        List<IComputeTask> tasks)
    {

        var bufferBarriers = new List<BufferMemoryBarrier>();
        var imageBarriers = new List<VkImageMemoryBarrier>();
        var hashset =
            new HashSet<IComputeResource>(bufferBarriers.Count +
                                          imageBarriers.Count);

        foreach (var task in tasks)
        {
            _targetPipeline = task.InitialPipelineStage;
            var srcPipelineFlag = _pipelineStage;
            bufferBarriers.Clear();
            imageBarriers.Clear();
            hashset.Clear();
            imageBarriers.Capacity = bufferBarriers.Capacity =
                task.Reads.Count + task.Writes.Count;

            foreach (var resource in task.Reads)
                hashset.Add(resource);

            foreach (var resource in task.Writes)
                hashset.Add(resource);

            foreach (var resource in hashset)
            {
                var (bufferMemoryBarrier, imageBarrier, pipelineStageFlags) =
                    resource.Accept(this);

                if (imageBarrier != null)
                    imageBarriers.Add(imageBarrier.Value);
                if (bufferMemoryBarrier != null)
                    bufferBarriers.Add(bufferMemoryBarrier.Value);

                srcPipelineFlag |= pipelineStageFlags;
            }

            if (bufferBarriers.Count > 0 || imageBarriers.Count > 0)
            {
                scope.PipelineBarrier(srcPipelineFlag,
                    task.InitialPipelineStage,
                    DependencyFlags.None,
                    bufferMemoryBarriers: [.. bufferBarriers],
                    imageMemoryBarriers: [.. imageBarriers]);
            }
            _pipelineStage = task.InvokeRecord(scope, _bufferAccessFlags, _imageAccessFlags);
        }

        return _pipelineStage;
    }

    private static bool CheckSupport(
        PipelineStageFlags pipelineStageFlags,
        AccessFlags accessFlags)
    {
        return pipelineStageFlags switch
        {
            PipelineStageFlags.TopOfPipeBit => false,
            PipelineStageFlags.TransferBit => accessFlags is
                AccessFlags.TransferReadBit
                or AccessFlags.TransferWriteBit,
            PipelineStageFlags.ConditionalRenderingBitExt => accessFlags is
                AccessFlags.ConditionalRenderingReadBitExt,
            _ => true,
        };
    }
}