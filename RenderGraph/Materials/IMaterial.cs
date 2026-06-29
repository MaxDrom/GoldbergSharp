using GoldbergSharp.RenderGraph.Pass;
using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.RenderGraph.Materials;

public interface IMaterial
{
    string Name { get; }
    void Set(string name, IVkBuffer buffer, AccessFlags accessFlags);
    void Set(string name, VkImageView image, AccessFlags accessFlags);
    void Set<T>(string name, T value)
        where T : unmanaged;
    IShaderEffect GetEffectForPath(IPass path);
}