using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GlideBar.HardwareMonitor.Interop;

/// <summary>
/// Thin P/Invoke surface for the Tier-A (no-admin, cheap) metrics:
/// CPU load via <c>GetSystemTimes</c>, physical RAM via <c>GlobalMemoryStatusEx</c>,
/// and total dedicated VRAM via DXGI adapter enumeration.
/// </summary>
internal static class NativeMethods
{
    // ---- CPU: GetSystemTimes ------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;

        public readonly ulong ToUInt64() => ((ulong)DwHighDateTime << 32) | DwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    // ---- RAM: GlobalMemoryStatusEx -----------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong UllTotalPhys;
        public ulong UllAvailPhys;
        public ulong UllTotalPageFile;
        public ulong UllAvailPageFile;
        public ulong UllTotalVirtual;
        public ulong UllAvailVirtual;
        public ulong UllAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---- Elevation ----------------------------------------------------------

    /// <summary>True when the current process runs with administrator rights.</summary>
    internal static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ---- DXGI: total dedicated VRAM ----------------------------------------
    // Enumerate adapters once (cheap, cached) to learn each GPU's total dedicated
    // video memory. The GPU Adapter Memory perf counter only exposes *usage*.

    private const int DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // --- IDXGIObject ---
        void SetPrivateData();                 // placeholders to preserve vtable order
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // --- IDXGIFactory ---
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        // --- IDXGIFactory1 ---
        [PreserveSig] int EnumAdapters1(uint adapter, out IntPtr ppAdapter);
        [PreserveSig] int IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // --- IDXGIObject ---
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // --- IDXGIAdapter ---
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        // --- IDXGIAdapter1 ---
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    /// <summary>
    /// Sums the dedicated video memory of all non-software DXGI adapters.
    /// Returns 0 on any failure (e.g. DXGI unavailable) — callers treat 0 as "unknown".
    /// </summary>
    internal static long GetTotalDedicatedVideoMemoryBytes()
    {
        IntPtr factoryPtr = IntPtr.Zero;
        try
        {
            var iid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
                return 0;

            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
            long total = 0;
            for (uint i = 0; ; i++)
            {
                if (factory.EnumAdapters1(i, out IntPtr adapterPtr) != 0 || adapterPtr == IntPtr.Zero)
                    break;
                try
                {
                    var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(adapterPtr);
                    if (adapter.GetDesc1(out DXGI_ADAPTER_DESC1 desc) == 0 &&
                        (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
                    {
                        total += (long)desc.DedicatedVideoMemory.ToUInt64();
                    }
                    Marshal.ReleaseComObject(adapter);
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }
            Marshal.ReleaseComObject(factory);
            return total;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (factoryPtr != IntPtr.Zero)
                Marshal.Release(factoryPtr);
        }
    }
}
