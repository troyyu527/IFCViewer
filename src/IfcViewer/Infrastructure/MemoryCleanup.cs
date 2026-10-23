using System;
using System.Runtime;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// Frees memory that is no longer used after loading a model, instead of waiting until .NET decides to.
    /// A large model leaves gigabytes of temporary data (and the previous model's 3D scene) behind; compacting the
    /// large-object heap returns that memory to Windows. Takes milliseconds for small models.
    /// </summary>
    public static class MemoryCleanup
    {
        public static void Release()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
        }
    }
}
