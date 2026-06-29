using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public readonly struct CopyBufferToImageTask(IVkBuffer buffer,
    VkImage image,
    BufferImageCopy[] regions
)
    : IComputeTask
{
    public List<IComputeResource> Reads { get; } =
    [
        new BufferResource
        {
            AccessFlags = AccessFlags.TransferReadBit,
            Buffer = buffer,
        },
    ];

    public List<IComputeResource> Writes { get; } =
    [
        new ImageResource
        {
            AccessFlags = AccessFlags.TransferWriteBit,
            Image = image,
            Layout = ImageLayout.TransferDstOptimal,
        },
    ];

    public PipelineStageFlags InvokeRecord(
        VkCommandRecordingScope scope,
        Dictionary<IVkBuffer, (AccessFlags, PipelineStageFlags)>
            bufferAccessFlags,
        Dictionary<VkImage, (AccessFlags, ImageLayout,
            PipelineStageFlags)> imageAccessFlags)
    {
        scope.CopyBufferToImage(buffer, image, [.. regions]);
        return PipelineStageFlags.TransferBit;
    }

    public PipelineStageFlags InitialPipelineStage =>
        PipelineStageFlags.TransferBit;
}