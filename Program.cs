using System.Globalization;
using Autofac;
using Autofac.Features.AttributeFilters;
using Autofac.Integration.Mef;
using GoldbergSharp.AssetsUtils;
using GoldbergSharp.ImGui;
using GoldbergSharp.Vulkan;
using GoldbergSharp.Vulkan.VkAllocatorSystem;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;

namespace GoldbergSharp;

public class DisplayFormat
{
    public Format Format { get; set; }
    public ColorSpaceKHR ColorSpace { get; set; }

    public WindowOptions WindowOptions { get; set; }
}

internal class Program
{
    private static void Main(string[] args)
    {
        ThreadPool.SetMaxThreads(Environment.ProcessorCount,
            Environment.ProcessorCount);
        CultureInfo.CurrentCulture = new CultureInfo("en-US", false);
        CultureInfo.CurrentCulture.NumberFormat
            .CurrencyDecimalDigits = 28;
        
        
        var windowOptions = WindowOptions.DefaultVulkan;
        windowOptions.WindowState = WindowState.Maximized;
        var window = Window.Create(windowOptions);

        var builder = new ContainerBuilder();

        InitVulkan(builder, window, windowOptions);


        builder.RegisterType<EventHandler>().WithAttributeFiltering()
            .AsSelf().SingleInstance();
        builder.RegisterType<ImGuiController>()
            .WithAttributeFiltering().AsSelf().SingleInstance();
        builder.RegisterType<Editor>().WithAttributeFiltering()
            .As<IEditorComponent>();


        var container = builder.Build();

        //var gameWindow = container.Resolve<GameWindow>();
        //_ = container.Resolve<ImGuiController>();
        
        
        //var model = ModelLoader.LoadMesh(@"gltf/gltf/Barrel.gltf");
        //Console.WriteLine(model[0].Vertices.Length);
        //gameWindow.Run();

        container.Dispose();
    }

    private static void InitVulkan(ContainerBuilder builder,
        IWindow window,
        WindowOptions windowOptions)
    {
        window.Initialize();
        if (window.VkSurface is null)
            throw new Exception(
                "Windowing platform doesn't support Vulkan.");

        string[] extensions;
        unsafe
        {
            var pp =
                window.VkSurface
                    .GetRequiredExtensions(out var count);
            extensions = new string[count];
            //extensions[count] = "VK_EXT_device_address_binding_report";
            //extensions[count+1] =
              //  "VK_EXT_device_address_binding_report";
            SilkMarshal.CopyPtrToStringArray((nint)pp, extensions);
            
        }

        builder.RegisterInstance(window).SingleInstance();

        var ctx
            = new VkContext(window, extensions);
        var physicalDevice = ctx.Api
            .GetPhysicalDevices(ctx.Instance).ToArray()[0];
        string deviceName;
        unsafe
        {
            var property =
                ctx.Api.GetPhysicalDeviceProperty(physicalDevice);
            deviceName =
                SilkMarshal.PtrToString((nint)property.DeviceName)!;

            uint nn;
            ctx.SurfaceApi.GetPhysicalDeviceSurfaceFormats(
                physicalDevice, ctx.Surface, &nn, null);
            var formats = new SurfaceFormatKHR[nn];
            fixed (SurfaceFormatKHR* pFormat = formats)
            {
                ctx.SurfaceApi.GetPhysicalDeviceSurfaceFormats(
                    physicalDevice, ctx.Surface, &nn, pFormat);
            }

            var format = formats[0].Format;
            var colorSpace = formats[0].ColorSpace;
            foreach (var formatCap in formats)
                if (formatCap.Format == Format.R16G16B16A16Sfloat)
                {
                    format = formatCap.Format;
                    colorSpace = formatCap.ColorSpace;
                    //Console.WriteLine(format);
                    //Console.WriteLine(colorSpace);//break;
                    break;
                }

            builder.RegisterInstance(
                    new DisplayFormat
                    {
                        Format = format, ColorSpace = colorSpace,
                        WindowOptions = windowOptions,
                    })
                .SingleInstance();
            Console.WriteLine(format);
            Console.WriteLine(colorSpace);
            
        }


        Console.WriteLine(deviceName);

        builder.RegisterInstance(ctx).SingleInstance();
        builder.RegisterType<VkDevice>().AsSelf()
            .WithParameter("physicalDevice", physicalDevice)
            .WithParameter("enabledExtensionsNames",
                new List<string>
                {
                    KhrSwapchain.ExtensionName,
                    ExtConditionalRendering.ExtensionName
                })
            .SingleInstance();


        builder.RegisterType<StupidAllocator>().As<VkAllocator>()
            .WithMetadata("Type", "DeviceLocal")
            .WithParameter("requiredProperties",
                MemoryPropertyFlags.None)
            .WithParameter("preferredFlags",
                MemoryHeapFlags.DeviceLocalBit).SingleInstance();

        builder.RegisterType<StupidAllocator>().As<VkAllocator>()
            .WithMetadata("Type", "HostVisible")
            .WithParameter("requiredProperties",
                MemoryPropertyFlags.HostVisibleBit |
                MemoryPropertyFlags.HostCoherentBit)
            .WithParameter("preferredFlags",
                MemoryHeapFlags.None).SingleInstance();
        builder.RegisterMetadataRegistrationSources();

        builder.RegisterType<GameWindow>().WithAttributeFiltering()
            .AsSelf().As<IParametrized>().SingleInstance();
    }
}