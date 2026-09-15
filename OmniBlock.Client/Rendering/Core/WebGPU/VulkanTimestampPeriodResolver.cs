using Silk.NET.Vulkan;
using Silk.NET.WebGPU;
using VulkanInstance = Silk.NET.Vulkan.Instance;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     Compatibility bridge for the wgpu-native 0.19 ABI. That backend returns raw timestamp
///     query ticks but has no C entry point for <c>Queue::get_timestamp_period</c>. Vulkan exposes
///     the same physical-device property directly, so it is safe to use only after a unique
///     vendor/device match with the WebGPU adapter.
/// </summary>
internal static unsafe class VulkanTimestampPeriodResolver
{
    private const uint MaximumPhysicalDevices = 64;

    internal readonly record struct Candidate(
        uint VendorId,
        uint DeviceId,
        float TimestampPeriodNanoseconds);

    public static double? TryResolve(WebGpuDevice device, out string detail)
    {
        AdapterProperties webGpuProperties = default;
        device.Api.AdapterGetProperties(device.Adapter, &webGpuProperties);
        if (webGpuProperties.BackendType != BackendType.Vulkan)
        {
            detail = $"backend is {webGpuProperties.BackendType}, not Vulkan";
            return null;
        }

        return TryReadVulkanPeriod(
            webGpuProperties.VendorID,
            webGpuProperties.DeviceID,
            out detail);
    }

    internal static double? SelectUnique(
        uint vendorId,
        uint deviceId,
        ReadOnlySpan<Candidate> candidates,
        out int matchingAdapters)
    {
        matchingAdapters = 0;
        double? period = null;
        foreach (var candidate in candidates)
        {
            if (candidate.VendorId != vendorId || candidate.DeviceId != deviceId) continue;
            if (!float.IsFinite(candidate.TimestampPeriodNanoseconds) ||
                candidate.TimestampPeriodNanoseconds <= 0) continue;

            matchingAdapters++;
            period = candidate.TimestampPeriodNanoseconds;
        }

        return matchingAdapters == 1 ? period : null;
    }

    private static double? TryReadVulkanPeriod(uint vendorId, uint deviceId, out string detail)
    {
        Vk? vk = null;
        VulkanInstance instance = default;
        try
        {
            vk = Vk.GetApi();
            InstanceCreateInfo createInfo = new() { SType = StructureType.InstanceCreateInfo };
            var createResult = vk.CreateInstance(&createInfo, null, &instance);
            if (createResult != Result.Success)
            {
                detail = $"Vulkan instance creation failed ({createResult})";
                return null;
            }

            uint count = 0;
            var enumerateResult = vk.EnumeratePhysicalDevices(instance, &count, null);
            if (enumerateResult != Result.Success || count == 0 || count > MaximumPhysicalDevices)
            {
                detail = enumerateResult != Result.Success
                    ? $"Vulkan adapter enumeration failed ({enumerateResult})"
                    : $"Vulkan reported an invalid adapter count ({count})";
                return null;
            }

            var devices = new PhysicalDevice[count];
            fixed (PhysicalDevice* devicePointer = devices)
            {
                enumerateResult = vk.EnumeratePhysicalDevices(instance, &count, devicePointer);
            }
            if (enumerateResult is not (Result.Success or Result.Incomplete))
            {
                detail = $"Vulkan adapter enumeration failed ({enumerateResult})";
                return null;
            }

            Span<Candidate> candidates = stackalloc Candidate[checked((int)count)];
            for (var i = 0; i < candidates.Length; i++)
            {
                var properties = vk.GetPhysicalDeviceProperties(devices[i]);
                candidates[i] = new Candidate(
                    properties.VendorID,
                    properties.DeviceID,
                    properties.Limits.TimestampPeriod);
            }

            var selected = SelectUnique(vendorId, deviceId, candidates, out var matches);
            detail = selected is not null
                ? $"unique Vulkan adapter {vendorId:X4}:{deviceId:X4}"
                : matches == 0
                    ? $"no Vulkan adapter matched {vendorId:X4}:{deviceId:X4}"
                    : $"{matches} Vulkan adapters matched {vendorId:X4}:{deviceId:X4}";
            return selected;
        }
        catch (DllNotFoundException)
        {
            detail = "Vulkan loader was not found";
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            detail = "Vulkan loader is missing a required entry point";
            return null;
        }
        catch (TypeInitializationException)
        {
            detail = "Vulkan binding initialization failed";
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            detail = "Vulkan adapter inspection is unavailable on this platform";
            return null;
        }
        finally
        {
            if (vk is not null)
            {
                if (instance.Handle != 0) vk.DestroyInstance(instance, null);
                vk.Dispose();
            }
        }
    }
}
