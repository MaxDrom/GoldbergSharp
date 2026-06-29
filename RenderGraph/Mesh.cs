using System.Runtime.InteropServices;
using GoldbergSharp.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace GoldbergSharp.RenderGraph;

public struct MeshGpuProvider
{
    public IVkBuffer VertexBuffer { get; init; }
    public IVkBuffer IndexBuffer { get; init; }
    
    public int IndexCount { get; init; }
    public int VertexOffset { get; init; }
}

public interface IMesh
{
    public string Id { get; }
    Type VertexType { get; }
    Type IndicesType { get; }
    
    Span<byte> VertexData { get; }
    Span<byte> IndexData { get; }
    PrimitiveTopology PrimitiveTopology { get; }
}

public class Mesh<T> : IMesh
where T : unmanaged, IVertexData<T>
{
    public string Id { get; init; }
    
    public T[] Vertices { get; init; }
    public ushort[] Indices { get; init; }
    
    public PrimitiveTopology PrimitiveTopology { get; init; }
    public Type VertexType => typeof(T);
    public Type IndicesType => typeof(ushort);
    public Span<byte> VertexData => MemoryMarshal.AsBytes(Vertices.AsSpan());

    public Span<byte> IndexData =>
        MemoryMarshal.AsBytes(Indices.AsSpan());
}