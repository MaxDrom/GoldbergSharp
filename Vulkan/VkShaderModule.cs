using System.Runtime.InteropServices;
using Silk.NET.SPIRV.Reflect;
using Silk.NET.Vulkan;
using DescriptorType = Silk.NET.Vulkan.DescriptorType;
using Result = Silk.NET.Vulkan.Result;

namespace GoldbergSharp.Vulkan;

public class VkShaderModule : IDisposable
{
    private readonly VkContext _ctx;
    private readonly VkDevice _device;

    private readonly ShaderModule _shaderModule;
    private bool _disposedValue;
    
    
    public Dictionary<string, (uint Set, uint Binding, DescriptorType Type, uint Count)> Bindings { get; } = new();

    public uint PushConstantSize { get; private set; } = 0;
    public Dictionary<string, uint> PushConstantMembersOffsets { get; } = new();
    
    public VkShaderModule(VkContext ctx,
        VkDevice device,
        string spirvPath)
    {
        var bytes = File.ReadAllBytes(spirvPath);
        _ctx = ctx;
        _device = device;

        unsafe
        {
            fixed (byte* pcode = bytes)
            {
                ShaderModuleCreateInfo createInfo = new()
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)bytes.Length,
                    PCode = (uint*)pcode,
                };

                if (ctx.Api.CreateShaderModule(device.Device,
                        in createInfo, null, out _shaderModule) !=
                    Result.Success)
                    throw new Exception(
                        $"Failed to create shader module with path {spirvPath}");

                var reflectModule = new ReflectShaderModule();
                var result = _ctx.Reflect.CreateShaderModule((nuint)bytes.Length, pcode, ref reflectModule);
                if (reflectModule.PushConstantBlockCount != 0)
                {
                    PushConstantSize = reflectModule.PushConstantBlocks[0].Size;
                    for(var j = 0; j<reflectModule.PushConstantBlocks[0].MemberCount; j++)
                    {
                        var member = reflectModule.PushConstantBlocks[0].Members[j];
                        PushConstantMembersOffsets[Marshal.PtrToStringAnsi((nint)member.Name)!] = member.Offset;
                    }
                }
                uint bindingCount = reflectModule.DescriptorBindingCount;
                for (var i = 0u; i < bindingCount; i++)
                {
                    var binding =  reflectModule.DescriptorBindings[i];
                    var name = Marshal.PtrToStringAnsi((nint)binding.Name)!;
                    if (string.IsNullOrEmpty(name))
                        name = Marshal.PtrToStringAnsi((nint)binding.Block.Name)!;
                    var setIdx = binding.Set;
                    var bindingIdx = binding.Binding;
                    var type = binding.DescriptorType;
                    var count = binding.Count;
                    
                     Bindings[name] = (setIdx, bindingIdx, (DescriptorType)type, count);
                }
                _ctx.Reflect.DestroyShaderModule(ref reflectModule);
            }
        }
    }

    
    
    public ShaderModule ShaderModule => _shaderModule;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            unsafe
            {
                _ctx.Api.DestroyShaderModule(_device.Device,
                    _shaderModule, null);
            }

            _disposedValue = true;
        }
    }

    ~VkShaderModule()
    {
        Dispose(false);
    }
}