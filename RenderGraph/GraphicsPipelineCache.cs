using GoldbergSharp.AssetsUtils;
using GoldbergSharp.ImGui;
using GoldbergSharp.RenderGraph.Materials;
using GoldbergSharp.RenderGraph.Pass;
using GoldbergSharp.Vulkan;
using GoldbergSharp.Vulkan.Builders;
using Silk.NET.Vulkan;

namespace GoldbergSharp.RenderGraph;

public interface IGeometryType
{
    public string Name { get; }
    public GraphicsPipelineBuilder.AttributeAggregator PushGeometryType(GraphicsPipelineBuilder.AttributeAggregator aggregator);
}

public class VertexGeometryType<TV> : IGeometryType
    where TV : unmanaged, IVertexData<TV>
{
    private static readonly string _name = typeof(TV).Name; 
    public string Name  => _name;
    public GraphicsPipelineBuilder.AttributeAggregator PushGeometryType(GraphicsPipelineBuilder.AttributeAggregator aggregator)
    {
        return aggregator.AddBindingFor<TV>(0, VertexInputRate.Vertex);
    }
}

public class InstancedGeometryType<TV, TI> : IGeometryType
    where TV : unmanaged, IVertexData<TV>
    where TI : unmanaged, IVertexData<TI>
{
    private static readonly string _name = typeof(TV).Name+typeof(TI).Name; 
    public string Name  => _name;
    public GraphicsPipelineBuilder.AttributeAggregator PushGeometryType(GraphicsPipelineBuilder.AttributeAggregator aggregator)
    {
        return aggregator.AddBindingFor<TV>(0, VertexInputRate.Vertex)
            .AddBindingFor<TI>(1, VertexInputRate.Instance);
    }
}

public class GraphicsPipelinesCache(VkContext ctx, VkDevice device)
{
    private Dictionary<(string, string, string), VkGraphicsPipeline> _pipelines = new();

    public VkGraphicsPipeline GetPipeline(IGeometryType geometryType, IMaterial material, IPass pass)
    {
        if (_pipelines.TryGetValue((geometryType.Name, material.Name, pass.Name), out var result))
        {
            return result;
        }

        PipelineColorBlendAttachmentState colorBlend = new();
        Viewport viewport = new();
        Rect2D scissor = new();
        var effect = material.GetEffectForPath(pass);
        result = new GraphicsPipelineBuilder()
            .ForRenderPass(null)
            .WithDynamicStages([
                DynamicState.Viewport, DynamicState.Scissor,
            ]).WithFixedFunctions(z =>
                z.ColorBlending([colorBlend])
                    .Rasterization(y =>
                        y.WithSettings(PolygonMode.Fill,
                            CullModeFlags.BackBit,
                            FrontFace.Clockwise,
                            1.0f))
                    .Multisampling(SampleCountFlags.Count1Bit)
                )
            .WithVertexInput(z => geometryType.PushGeometryType(z))
            .WithInputAssembly(PrimitiveTopology.TriangleList)
            .WithViewportAndScissor(viewport, scissor)
            .WithPipelineStages(z => effect.PushStages(z))
            .WithLayouts(effect.Layouts, effect.PushConstantRanges)
            .Build(ctx, device, 0);
        
        _pipelines[(geometryType.Name, material.Name, pass.Name)] = result;
        return result;
    }
}