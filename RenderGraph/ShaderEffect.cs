using GoldbergSharp.Vulkan;
using GoldbergSharp.Vulkan.Builders;
using Silk.NET.Vulkan;

namespace GoldbergSharp.RenderGraph;

public interface IShaderEffect : IDisposable
{
    GraphicsPipelineBuilder.StageBuilder PushStages(GraphicsPipelineBuilder.StageBuilder stageBuilder);
    PushConstantRange[] PushConstantRanges { get; }
    VkSetLayout[] Layouts { get; }
}

public class ShaderRasterizationEffect : IShaderEffect
{
    public Dictionary<ShaderStageFlags, VkShaderModule> Info { get; } = new();
    public ShaderRasterizationEffect(VkContext ctx, VkDevice device, Dictionary<ShaderStageFlags, string> paths )
    {
        foreach (var (stage, path) in paths)
            Info[stage] = new VkShaderModule(ctx, device, path);
    }

    public GraphicsPipelineBuilder.StageBuilder PushStages(GraphicsPipelineBuilder.StageBuilder stageBuilder)
    {
        Dictionary<ShaderStageFlags, Func<VkShaderInfo, GraphicsPipelineBuilder.StageBuilder>> stages 
            = new()
            {
                [ShaderStageFlags.VertexBit] = stageBuilder.Vertex,
                [ShaderStageFlags.FragmentBit] = stageBuilder.Fragment,
                [ShaderStageFlags.GeometryBit] = stageBuilder.Geometry,
                [ShaderStageFlags.TessellationControlBit] = stageBuilder.TesselationControl,
                [ShaderStageFlags.TessellationEvaluationBit] = stageBuilder.TesselationEvaluation
            };

        foreach (var (stage, module) in Info)
            stageBuilder = stages[stage].Invoke(new VkShaderInfo(module, "main"));
        return stageBuilder;
    }

    public PushConstantRange[] PushConstantRanges { get; } = [];
    public VkSetLayout[] Layouts { get; } = [];

    public void Dispose()
    {
        foreach (var layout in Layouts)
            layout.Dispose();
        
        foreach (var info in Info.Values)
            info.Dispose();
    }
}