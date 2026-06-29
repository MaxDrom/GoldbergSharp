using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public struct FillBufferTask : IComputeTask
{
    public List<IComputeResource> Reads { get; }
    public List<IComputeResource> Writes { get; }

    public PipelineStageFlags InvokeRecord(
        VkCommandRecordingScope scope,
        Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
            bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout,
            PipelineStageFlags)> imageAccessFlags)
    {
        throw new NotImplementedException();
    }

    public PipelineStageFlags InitialPipelineStage { get; }
}