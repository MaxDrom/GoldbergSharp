using Silk.NET.Vulkan;

namespace GoldbergSharp.Vulkan;

public interface IVkPipeline
{
    Pipeline InternalPipeline { get; }
    PipelineBindPoint BindPoint { get; }
    PipelineLayout PipelineLayout { get; }
}