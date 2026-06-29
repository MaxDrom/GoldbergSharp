using GoldbergSharp.Vulkan;
using Silk.NET.Vulkan;

namespace GoldbergSharp.ComputeScheduling;

public interface IComputeResource : IEquatable<IComputeResource>
{
    public bool IsOverlap(IComputeResource other);
    public T Accept<T>(IComputeResourceVisitor<T> visitor);
}

public interface IComputeResourceVisitor<out T>
{
    T Visit(BufferResource resource);

    T Visit(ImageResource resource);
}