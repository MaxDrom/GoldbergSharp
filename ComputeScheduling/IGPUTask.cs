using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public interface IComputeTask
{
    List<IComputeResource> Reads { get; }
    List<IComputeResource> Writes { get; }

    PipelineStageFlags InitialPipelineStage { get; }

    PipelineStageFlags InvokeRecord(VkCommandRecordingScope scope, Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)> bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout, PipelineStageFlags)> imageAccessFlags);
}

public static class IComputeTaskExtensions
{
    public static IComputeTask WithConditionalRendering(
        this IComputeTask gpuTask,
        IVkBuffer controlBuffer,
        uint offset = 0,
        ConditionalRenderingFlagsEXT flagsExt =
            ConditionalRenderingFlagsEXT.None)
    {
        return new ConditionalTask(gpuTask, controlBuffer, offset,
            flagsExt);
    }
}