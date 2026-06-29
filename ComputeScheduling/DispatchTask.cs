using GoldbergSharp.ComputeScheduling.Executors;
using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public readonly struct DispatchTask : IComputeTask
{
    public IDispatchExecutor Executor { get; init; }
    public List<IComputeResource> Reads { get; init; }
    public List<IComputeResource> Writes { get; init; }


    public PipelineStageFlags InvokeRecord(
        VkCommandRecordingScope scope,
        Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
            bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout,
            PipelineStageFlags)> imageAccessFlags)
    {
        Executor.RecordDispatch(scope);
        return PipelineStageFlags.ComputeShaderBit;
    }

    public PipelineStageFlags InitialPipelineStage =>
        PipelineStageFlags.ComputeShaderBit;
}