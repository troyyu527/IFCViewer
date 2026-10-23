using System;
using System.IO;
using System.Threading.Tasks;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;

namespace IfcViewer.Services
{
    /// <summary>
    /// Opens and holds the current IFC model. Only one model is open at a time.
    /// </summary>
    public class IfcModelService : IDisposable
    {
        // Files larger than this (MB) are kept in a temporary database on disk instead of in memory.
        // Same value as xBIM's default (HeuristicModelProvider.DefaultIfcDatabaseSizeThreshHoldMb).
        public const double DiskModelThresholdMb = 100;

        public IfcStore CurrentModel { get; private set; }
        public string FilePath { get; private set; }

        // Schema of the open file, e.g. "IFC2X3" or "IFC4".
        public string SchemaName => CurrentModel?.SchemaVersion.ToString().ToUpperInvariant();

        // Number of physical elements (walls, slabs, doors, ...) in the open file.
        public long ElementCount { get; private set; }

        // Opens an IFC file on a background thread. The previous model is closed only after
        // the new one opened successfully, so a failed open leaves the old model in place.
        // progress: Receives 0–100 while the file is parsed.
        public Task OpenAsync(string path, IProgress<int> progress)
        {
            return Task.Run(() =>
            {
                var model = IfcStore.Open(path, null, DiskModelThresholdMb, (percent, state) =>
                {
                    // xBIM also reports -1 / 101 as phase start/end markers; only pass real percentages
                    if (percent >= 0 && percent <= 100) progress?.Report(percent);
                });
                var elementCount = model.Instances.CountOf<IIfcElement>();

                Close();
                CurrentModel = model;
                FilePath = path;
                ElementCount = elementCount;
            });
        }

        // True if a file of this size will be kept on disk (slower to load).
        public static bool UsesDiskModel(string path) => new FileInfo(path).Length / 1048576.0 > DiskModelThresholdMb;

        public void Close()
        {
            CurrentModel?.Dispose();
            CurrentModel = null;
            FilePath = null;
            ElementCount = 0;
        }

        public void Dispose() => Close();
    }
}
