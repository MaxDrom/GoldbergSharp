using System.Numerics;
using System.Runtime.InteropServices;
using Autofac.Features.AttributeFilters;
using GoldbergSharp.ComputeScheduling;
using GoldbergSharp.Vulkan;
using GoldbergSharp.Vulkan.Builders;
using GoldbergSharp.Vulkan.VkAllocatorSystem;
using ImGuiNET;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace GoldbergSharp.ImGui;

using ImGui = ImGuiNET.ImGui;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImGuiVertex : IVertexData<ImGuiVertex>
{
    [VertexInputDescription(0, Format.R32G32Sfloat)]
    public Vector2D<float> position;

    [VertexInputDescription(1, Format.R32G32Sfloat)]
    public Vector2D<float> uv;

    [VertexInputDescription(2, Format.R8G8B8A8Unorm)]
    public uint color;
}

public struct ImGuiPushConstant
{
    public Vector2D<float> Scale;
    public Vector2D<float> Translate;
}

public class ImGuiController : IDisposable
{
    private readonly VkAllocator _allocator;
    private readonly IEditorComponent[] _components;
    private readonly VkDescriptorPool _descriptorPool;
    private readonly VkImageView _fontImageView;
    private readonly VkSampler _fontSampler;
    private readonly VkFrameBuffer _framebuffer;
    private readonly GameWindow _gameWindow;
    private readonly VkBuffer<ushort>[] _indexBuffer;
    private readonly VkMappedMemory<ushort>[] _indexBufferMapped;
    private readonly VkGraphicsPipeline _pipeline;
    private readonly VkRenderPass _renderPass;
    private readonly VkAllocator _stagingAllocator;
    private readonly VkBuffer<ImGuiVertex>[] _vertexBuffer;
    private readonly VkMappedMemory<ImGuiVertex>[] _vertexMapped;
    private VkTexture _fontTexture;
    private float _fbWidth;
    private float _fbHeight;

    public ImGuiController(VkContext ctx,
        VkDevice device,
        [MetadataFilter("Type", "DeviceLocal")]
        VkAllocator allocator,
        [MetadataFilter("Type", "HostVisible")]
        VkAllocator stagingAllocator,
        GameWindow gameWindow,
        EventHandler eventHandler,
        IWindow window,
        IEditorComponent[] components)
    {
        ImGui.CreateContext();
        var inputContext = eventHandler.InputContext;
        ImGui.StyleColorsClassic();
        _components = components;

        var window1 = window;
        _gameWindow = gameWindow;
        var renderTarget = gameWindow.RenderTarget;
        var extent = gameWindow.WindowSize;
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_gameWindow.WindowSize.Width,
            _gameWindow.WindowSize.Height);
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;
        io.FontGlobalScale =
            window1.FramebufferSize.X / (float)window1.Size.X;
        inputContext.Mice[0].MouseMove += (_, x) =>
        {
            var newX = x.X / window1.Size.X *
                       _gameWindow.WindowSize.Width;
            var newY = x.Y / window1.Size.Y *
                       _gameWindow.WindowSize.Height;
            ImGui.GetIO()
                .AddMousePosEvent(newX, newY);
        };

        inputContext.Mice[0].MouseDown += (_, x) =>
        {
            ImGui.GetIO()
                .AddMouseButtonEvent((int)x,
                    true);
        };

        inputContext.Mice[0].MouseUp += (_, x) =>
        {
            ImGui.GetIO()
                .AddMouseButtonEvent((int)x,
                    false);
        };

        gameWindow.OnRenderEnd += RecordImGuiRender;
        gameWindow.OnUpdate += Update;
        _allocator = allocator;
        _stagingAllocator = stagingAllocator;


        UploadFonts();

        _fontImageView = new VkImageView(ctx, device,
            _fontTexture.Image, new ComponentMapping(
                ComponentSwizzle.Identity,
                ComponentSwizzle.Identity,
                ComponentSwizzle.Identity,
                ComponentSwizzle.Identity),
            new ImageSubresourceRange(ImageAspectFlags.ColorBit,
                0, 1, 0, 1));

        _fontSampler = new VkSampler(ctx, device,
            SamplerCreateFlags.None, Filter.Linear, Filter.Linear,
            SamplerMipmapMode.Linear);


        var imageInfo = new DescriptorImageInfo(_fontSampler.Sampler,
            _fontImageView.ImageView,
            ImageLayout.ShaderReadOnlyOptimal);

        using var vertModule = new VkShaderModule(ctx, device,
            "shader_objects/imgui.vert.spv");
        using var fragModule = new VkShaderModule(ctx, device,
            "shader_objects/imgui.frag.spv");


        var subPass = new VkSubpassInfo(PipelineBindPoint.Graphics, [
            new AttachmentReference
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            },
        ]);

        var attachmentDescription = new AttachmentDescription
        {
            Format = renderTarget.Image.Format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.ColorAttachmentOptimal,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = ~0u,
            DstSubpass = 0u,
            SrcStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags
                .ColorAttachmentOutputBit,
        };

        _renderPass = new VkRenderPass(ctx, device, [subPass],
            [dependency], [attachmentDescription]);

        _framebuffer =
            new VkFrameBuffer(ctx, device, _renderPass,
                extent.Width,
                extent.Height, 1, [renderTarget]);

        var pushConstantRange = new PushConstantRange(
            ShaderStageFlags.VertexBit, 0,
            (uint)Marshal.SizeOf<ImGuiPushConstant>());


        var viewport = new Viewport
        {
            X = 0.0f,
            Y = 0.0f,
            Width = extent.Width,
            Height = extent.Height,
        };

        Rect2D scissor = new(new Offset2D(0, 0), extent);

        PipelineColorBlendAttachmentState colorBlend = new()
        {
            ColorWriteMask =
                ColorComponentFlags.RBit |
                ColorComponentFlags.GBit |
                ColorComponentFlags.BBit |
                ColorComponentFlags.ABit,
            BlendEnable = true,
            ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = BlendFactor.One,
            DstAlphaBlendFactor = BlendFactor.Zero,
            AlphaBlendOp = BlendOp.Add,
            SrcColorBlendFactor = BlendFactor.SrcAlpha,
            DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        };

        var fontAtlasBinding =
            new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
                PImmutableSamplers = null,
            };

        using var layout =
            new VkSetLayout(ctx, device, [fontAtlasBinding]);

        _descriptorPool = new VkDescriptorPool(ctx, device, [
            new DescriptorPoolSize
            {
                DescriptorCount = 1,
                Type = DescriptorType.CombinedImageSampler,
            },
        ], 1);
        var descriptorSet =
            _descriptorPool.AllocateDescriptors(layout, 1)[0];
        new VkDescriptorSetUpdater(ctx, device)
            .AppendWrite(descriptorSet, 0,
                DescriptorType.CombinedImageSampler,
                [imageInfo]).Update();

        ImGui.GetIO().Fonts
            .SetTexID((IntPtr)descriptorSet.Handle);

        _pipeline = new GraphicsPipelineBuilder()
            .ForRenderPass(_renderPass)
            .WithDynamicStages([
                DynamicState.Viewport, DynamicState.Scissor,
            ]).WithFixedFunctions(z =>
                z.ColorBlending([colorBlend]).Rasterization(y =>
                        y.WithSettings(PolygonMode.Fill,
                            CullModeFlags.BackBit,
                            FrontFace.Clockwise,
                            1.0f))
                    .Multisampling(SampleCountFlags.Count1Bit))
            .WithVertexInput(z => z
                .AddBindingFor<ImGuiVertex>(0,
                    VertexInputRate.Vertex))
            .WithInputAssembly(PrimitiveTopology.TriangleList)
            .WithViewportAndScissor(viewport, scissor)
            .WithPipelineStages(z =>
                z.Vertex(new VkShaderInfo(vertModule, "main"))
                    .Fragment(
                        new VkShaderInfo(fragModule, "main")))
            .WithLayouts([layout], [pushConstantRange])
            .Build(ctx, device, 0);

        _vertexBuffer =
            new VkBuffer<ImGuiVertex>[GameWindow.FramesInFlight];
        _vertexMapped = new VkMappedMemory<ImGuiVertex>[_vertexBuffer.Length];
        for (var i = 0; i < _vertexBuffer.Length; i++)
        {
            _vertexBuffer[i] = new VkBuffer<ImGuiVertex>(
                128 * 1024,
                BufferUsageFlags.VertexBufferBit,
                SharingMode.Exclusive,
                _stagingAllocator);
            _vertexMapped[i] = _vertexBuffer[i].Map(0, 128 * 1024);
        }

        _indexBuffer = new VkBuffer<ushort>[_vertexBuffer.Length];
        _indexBufferMapped =
            new VkMappedMemory<ushort>[_indexBuffer.Length];
        for (var i = 0; i < _indexBuffer.Length; i++)
        {
            _indexBuffer[i] = new VkBuffer<ushort>(128 * 1024,
                BufferUsageFlags.IndexBufferBit,
                SharingMode.Exclusive,
                _stagingAllocator);
            _indexBufferMapped[i] = _indexBuffer[i].Map(0, 128 * 1024);
            
        }

    }

    public void Dispose()
    {
        _fontSampler.Dispose();
        _fontTexture.Dispose();
        _fontImageView.Dispose();
        _renderPass.Dispose();
        _framebuffer.Dispose();
        _descriptorPool.Dispose();
        foreach (var vm in _vertexMapped)
            vm.Dispose();
        foreach (var vb in _vertexBuffer)
            vb.Dispose();
        foreach (var vm in _indexBufferMapped)
            vm.Dispose();
        foreach (var vb in _indexBuffer)
            vb.Dispose();
        _pipeline.Dispose();
    }

    private void Update(double a, double b)
    {
        ImGui.GetIO().DeltaTime = (float)a;
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_gameWindow.WindowSize.Width,
            _gameWindow.WindowSize.Height);

        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;


        ImGui.NewFrame();


        //ImGui.SetNextWindowDockID();
        float startPos = 0;
        foreach (var component in _components)
        {
            ImGui.SetNextWindowCollapsed(true, ImGuiCond.Once);
            ImGui.SetNextWindowSize(
                io.DisplaySize with
                {
                    X = io.DisplaySize.X /
                        Math.Max(3, _components.Length),
                }, ImGuiCond.Once);
            ImGui.SetNextWindowPos(new Vector2(startPos, 0));
            ImGui.Begin(
                $"{component.Name}###{component.Guid}",
                ImGuiWindowFlags.NoMove);
            component.UpdateGui();
            startPos += ImGui.GetWindowSize().X;
            ImGui.End();
        }


        ImGui.EndFrame();
    }

    private int _frame;

    private void RecordImGuiRender(VkCommandRecordingScope recording,
        Rect2D renderArea)
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        _fbWidth = (int)(drawData.DisplaySize.X *
                         drawData.FramebufferScale.X);
        _fbHeight = (int)(drawData.DisplaySize.Y *
                          drawData.FramebufferScale.Y);

        unsafe
        {
            var offset = 0;
            for (var i = 0; i < drawData.CmdListsCount; i++)
            {
                var cmdList = drawData.CmdLists[i];
                var verticesLength = cmdList.VtxBuffer.Size;
                var vertices =
                    (ImGuiVertex*)cmdList.VtxBuffer.Data.ToPointer();

                for (var k = 0; k < verticesLength; k++)
                    _vertexMapped[_frame][offset + k] = vertices[k];

                offset += verticesLength;
            }

            offset = 0;
            for (var i = 0; i < drawData.CmdListsCount; i++)
            {
                var cmdList = drawData.CmdLists[i];
                var idxBufferSize = cmdList.IdxBuffer.Size;
                var indices =
                    (ushort*)cmdList.IdxBuffer.Data.ToPointer();

                for (var k = 0; k < idxBufferSize; k++)
                    _indexBufferMapped[_frame][offset + k] = indices[k];

                offset += idxBufferSize;
            }
        }

        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = _fbWidth,
            Height = _fbHeight,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };
        if (_fontTexture.Image
                .LastLayout != ImageLayout.ShaderReadOnlyOptimal)

            recording.PipelineBarrier(
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.FragmentShaderBit |
                PipelineStageFlags.VertexInputBit,
                DependencyFlags.None,
                imageMemoryBarriers:
                [
                    new VkImageMemoryBarrier
                    {
                        DstAccessMask = AccessFlags.ShaderReadBit,
                        Image = _fontTexture.Image,
                        NewLayout = ImageLayout
                            .ShaderReadOnlyOptimal,
                        SrcAccessMask = AccessFlags.None,
                        SubresourceRange =
                            new ImageSubresourceRange
                            {
                                AspectMask = ImageAspectFlags
                                    .ColorBit,
                                BaseArrayLayer = 0,
                                BaseMipLevel = 0,
                                LevelCount = 1,
                                LayerCount = 1,
                            },
                    },
                ]);

        using var pass =
            recording.BeginRenderPass(_renderPass, _framebuffer,
                renderArea, [new(new(0f, 0, 0, 1))]);
        recording.BindPipeline(_pipeline);

        pass.SetViewport(ref viewport);
        recording.BindVertexBuffers(0,
            [_vertexBuffer[_frame]], [0]);
        recording.BindIndexBuffer(_indexBuffer[_frame], 0,
            IndexType.Uint16);
        var pushConstant = new ImGuiPushConstant
        {
            Scale = new Vector2D<float>(
                2.0f / drawData.DisplaySize.X,
                2.0f / drawData.DisplaySize.Y),
            Translate =
                new Vector2D<float>(-1.0f -
                                    drawData.DisplayPos.X * 2.0f /
                                    drawData.DisplaySize.Y,
                    -1.0f - drawData.DisplayPos.Y * 2.0f /
                    drawData.DisplaySize.X),
        };
        recording.SetPushConstant(
            _pipeline,
            ShaderStageFlags.VertexBit, ref pushConstant);
        var vtxOffset = 0;
        var idxOffset = 0;
        for (var i = 0; i < drawData.CmdListsCount; i++)
        {
            var cmdList = drawData.CmdLists[i];

            for (var j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                var cmdBuffer = cmdList.CmdBuffer[j];
                Rect2D scissor = new()
                {
                    Offset = new Offset2D
                    {
                        X = (int)(cmdBuffer.ClipRect.X *
                                  drawData.FramebufferScale.X),
                        Y = (int)(cmdBuffer.ClipRect.Y *
                                  drawData.FramebufferScale.X),
                    },
                    Extent = new Extent2D
                    {
                        Width = (uint)(cmdBuffer.ClipRect.Z -
                                       cmdBuffer.ClipRect.X) *
                                (uint)drawData.FramebufferScale.X,
                        Height = (uint)(cmdBuffer.ClipRect.W -
                                        cmdBuffer.ClipRect.Y) *
                                 (uint)drawData.FramebufferScale.X,
                    },
                };


                pass.SetScissor(ref scissor);
                var desc =
                    new DescriptorSet((ulong)cmdBuffer.TextureId);
                recording.BindDescriptorSets(
                    PipelineBindPoint.Graphics,
                    _pipeline.PipelineLayout,
                    [desc]);

                pass.DrawIndexed(cmdBuffer.ElemCount, 1,
                    (uint)idxOffset + cmdBuffer.IdxOffset, 0,
                    (uint)vtxOffset + cmdBuffer.VtxOffset);
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _frame = (++_frame) % GameWindow.FramesInFlight;
    }

    private unsafe void UploadFonts()
    {
        ImGui.GetIO().Fonts.AddFontFromFileTTF(
            "ImGui/JetBrainsMonoNerdFont-Regular.ttf", 18.0f,
            null, ImGui.GetIO().Fonts.GetGlyphRangesCyrillic());
        ImGui.GetIO().Fonts.Build();
        ImGui.GetIO().Fonts
            .GetTexDataAsRGBA32(out byte* pFonts, out var width,
                out var height);
        var uploadSize = (ulong)(width * height * 4);
        var stagingBuffer = new VkBuffer<byte>(uploadSize,
            BufferUsageFlags.TransferSrcBit,
            SharingMode.Exclusive, _stagingAllocator);

        _fontTexture = new VkTexture(ImageType.Type2D,
            new Extent3D((uint)width, (uint)height, 1), 1, 1,
            Format.R8G8B8A8Unorm, ImageTiling.Linear,
            ImageLayout.Undefined,
            ImageUsageFlags.SampledBit |
            ImageUsageFlags.TransferDstBit,
            SampleCountFlags.Count1Bit, SharingMode.Exclusive,
            _allocator);

        using (var map = stagingBuffer.Map(0, uploadSize))
        {
            for (ulong i = 0; i < uploadSize; i++) map[i] = pFonts[i];
        }

        var region = new BufferImageCopy
        {
            BufferImageHeight = 0,
            BufferRowLength = 0,
            BufferOffset = 0,
            ImageExtent =
                new Extent3D((uint)width, (uint)height, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = 1,
                MipLevel = 0,
            },
        };
        ComputeScheduler.Instance.AddTask(
            new CopyBufferToImageTask(stagingBuffer,
                _fontTexture.Image, [region]));
    }
}