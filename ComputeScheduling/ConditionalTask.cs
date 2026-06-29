using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public class PredicateTask : IComputeTask
{
    public List<IComputeResource> Reads => throw new NotImplementedException();

    public List<IComputeResource> Writes => throw new NotImplementedException();

    public PipelineStageFlags InitialPipelineStage => PipelineStageFlags.ConditionalRenderingBitExt;

    public PipelineStageFlags InvokeRecord(VkCommandRecordingScope scope, Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)> bufferAccessFlags, Dictionary<VkImage, (AccessFlags, ImageLayout, PipelineStageFlags)> imageAccessFlags)
    {
        return PipelineStageFlags.ConditionalRenderingBitExt;
    }
}

public class ConditionalTask : IComputeTask
{
    private IComputeTask _innerTask;
    private IVkBuffer _controlBuffer;
    private uint _offset;
    private ConditionalRenderingFlagsEXT _flags;

    public ConditionalTask(IComputeTask innerTask,
        IVkBuffer buffer,
        uint offset,
        ConditionalRenderingFlagsEXT flags =
            ConditionalRenderingFlagsEXT.None)
    {
        _innerTask = innerTask;
        var reads =
            new List<IComputeResource>();

        foreach (var t in _innerTask.Reads)
            reads.Add(t);

        reads.Add(new BufferResource()
        {
            AccessFlags = AccessFlags.ConditionalRenderingReadBitExt,
            Buffer = buffer,
        });
        Reads = reads;

        _offset = offset;
        _controlBuffer = buffer;
        _flags = flags;
    }

    public List<IComputeResource> Reads { get; private set; }
    public List<IComputeResource> Writes => _innerTask.Writes;

    public PipelineStageFlags InitialPipelineStage => 
        _innerTask.InitialPipelineStage;

    public PipelineStageFlags InvokeRecord(
        VkCommandRecordingScope scope,
        Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
            bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout,
            PipelineStageFlags)> imageAccessFlags)
    {

        var barrier = new BufferMemoryBarrier()
        {
          SType = StructureType.BufferMemoryBarrier,
          SrcAccessMask =  AccessFlags.None,
          DstAccessMask = AccessFlags.ConditionalRenderingReadBitExt,
          Offset = 0,
          Buffer = _controlBuffer.Buffer,
          Size = _controlBuffer.Size,
        };
        bufferAccessFlags[_controlBuffer] = (AccessFlags.ConditionalRenderingReadBitExt, PipelineStageFlags.ConditionalRenderingBitExt);
        scope.PipelineBarrier(bufferAccessFlags[_controlBuffer].Item2,
                    PipelineStageFlags.ConditionalRenderingBitExt,
                    DependencyFlags.None,
                    bufferMemoryBarriers: [ barrier]);
        using var condRendering = scope.BeginConditionalRendering(
            _controlBuffer,
            _offset, _flags);
        
        // scope.PipelineBarrier(PipelineStageFlags.ConditionalRenderingBitExt,
        //             _innerTask.InitialPipelineStage,
        //             DependencyFlags.None);

         var pipelineStageFlags = _innerTask.InvokeRecord(scope,
             bufferAccessFlags, imageAccessFlags);

        return pipelineStageFlags;
    }
}