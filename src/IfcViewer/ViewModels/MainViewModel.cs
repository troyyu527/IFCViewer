using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IfcViewer.Infrastructure;
using IfcViewer.Models;
using IfcViewer.Services;

namespace IfcViewer.ViewModels
{
    /// <summary>
    /// Main window state: opens IFC files (with progress) and connects the viewer, properties panel and
    /// object tree.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // "IFC Viewer 1.0.0": the version comes from the project file (Version)
        private static readonly string AppTitle = "IFC Viewer " + typeof(MainViewModel).Assembly.GetName().Version.ToString(3);

        private readonly IfcModelService _ifcModel = new IfcModelService();
        private readonly GeometryService _geometry = new GeometryService();
        private readonly DialogService _dialogs = new DialogService();
        private readonly PropertyService _propertyService = new PropertyService();
        private readonly SpatialTreeService _spatialTree = new SpatialTreeService();
        private readonly AppSettings _settings;

        public ViewerViewModel Viewer { get; } = new ViewerViewModel();
        public PropertiesViewModel Properties { get; } = new PropertiesViewModel();
        public ObjectTreeViewModel Tree { get; }

        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private double _progress;
        [ObservableProperty] private bool _isBusy;

        // "IFC Viewer 1.0.0 — mc8.ifc" while a model is open.
        [ObservableProperty] private string _windowTitle = AppTitle;

        // False until a model is loaded: the viewport then shows a hint how to open one.
        [ObservableProperty] private bool _hasModel;

        // settings: remembered between runs; the last opened folder is updated here.
        public MainViewModel(AppSettings settings)
        {
            _settings = settings;
            Tree = new ObjectTreeViewModel(Viewer);
            Viewer.SelectionChanged += (s, e) => UpdateProperties();
        }

        // Shows the selected element(s) in the Properties panel; several are merged (VARIES).
        private void UpdateProperties()
        {
            var labels = Viewer.SelectedLabels;
            var model = _ifcModel.CurrentModel;
            if (labels.Count == 0 || model == null)
            {
                Properties.Clear();
                return;
            }
            var infos = labels.Select(label => _propertyService.GetInfo(model, label)).ToList();
            Properties.Show(PropertyService.Merge(infos), labels.Count);
        }

        // Generated as OpenCommand. Because the method is async, the command is disabled
        // while it runs, so the Open button can't be clicked twice during a load.
        [RelayCommand]
        private async Task OpenAsync()
        {
            var path = _dialogs.OpenIfcFile(_settings.LastFolder);
            if (path == null) return;

            var fileName = Path.GetFileName(path);
            if (new FileInfo(path).Length == 0)
            {
                Log.Info($"Not opened (empty file): {path}");
                _dialogs.ShowError("Could not open IFC file", $"{fileName}\n\nThe file is empty (0 bytes).");
                return;
            }

            // Large files are kept on disk: say so while loading, because they take noticeably longer
            var note = IfcModelService.UsesDiskModel(path) ? " (large file, kept on disk)" : "";
            IsBusy = true;
            Progress = 0;
            StatusText = $"Loading {fileName}{note}…";
            // Nothing stays selected from the old model while the new one loads
            Viewer.ClearSelection();

            // Progress<T> runs the callback on the UI thread, so it can update bound properties.
            // 'phase' is captured by the callback, so changing it switches the text shown.
            var phase = "Loading";
            var progress = new Progress<int>(percent =>
            {
                if (!IsBusy) return; // ignore late reports after loading finished
                Progress = percent;
                StatusText = $"{phase} {fileName}{note}… {percent}%";
            });

            // Once the new model is open, the old one is closed: a later failure must not leave its scene on screen
            var modelReplaced = false;
            var clock = Stopwatch.StartNew();
            try
            {
                Log.Info($"Opening {path} ({new FileInfo(path).Length / 1048576.0:N1} MB)");
                await _ifcModel.OpenAsync(path, progress);
                modelReplaced = true;
                var readTime = clock.Elapsed;

                phase = "Building 3D model of";
                var meshes = await _geometry.BuildAsync(_ifcModel.CurrentModel, progress);
                Viewer.LoadScene(meshes);
                var geometryTime = clock.Elapsed - readTime;

                var model = _ifcModel.CurrentModel;
                Tree.Load(await Task.Run(() => _spatialTree.Build(model)));
                await Task.Run(() => _propertyService.Prepare(model));

                // The previous model's 3D scene and tree are only unreferenced now: free them (GBs after a large model)
                await Task.Run(MemoryCleanup.Release);

                IsBusy = false;
                HasModel = true;
                WindowTitle = $"{AppTitle} — {fileName}";
                _settings.LastFolder = Path.GetDirectoryName(path);
                StatusText = $"Loaded {fileName} — {_ifcModel.SchemaName} — {_ifcModel.ElementCount} elements" +
                             (meshes.Count == 0 ? " — no 3D geometry" : "");
                Log.Info($"Loaded {fileName}: {_ifcModel.SchemaName}, {_ifcModel.ElementCount} elements, {meshes.Count} meshes; " +
                         $"read {readTime.TotalSeconds:N1} s, geometry {geometryTime.TotalSeconds:N1} s, total {clock.Elapsed.TotalSeconds:N1} s, " +
                         $"memory {Process.GetCurrentProcess().PrivateMemorySize64 / 1048576:N0} MB");

                if (meshes.Count == 0)
                {
                    _dialogs.ShowInfo("No 3D geometry",
                        $"{fileName}\n\nThis file contains no 3D geometry, so the 3D view stays empty. " +
                        "Its elements are listed in the object tree, and their properties can be viewed there.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Could not open {path}", ex);
                if (modelReplaced) ShowNoModel();
                IsBusy = false;
                StatusText = $"Could not open {fileName}";
                _dialogs.ShowError("Could not open IFC file", $"{fileName}\n\n{Describe(ex)}");
            }
        }

        // Empty scene, tree and properties: used after a load failed half-way, when the old model is already closed.
        private void ShowNoModel()
        {
            _ifcModel.Close();
            Viewer.LoadScene(Array.Empty<ElementMesh>());
            Tree.Load(null);
            Properties.Clear();
            HasModel = false;
            WindowTitle = AppTitle;
            MemoryCleanup.Release();
        }

        // A readable reason for a failed load; the technical details go to the log.
        private static string Describe(Exception ex)
        {
            var all = Flatten(ex).ToList();
            if (all.Any(e => e is OutOfMemoryException))
                return "This model is too large for the memory currently available.\n\n" +
                       "Close other programs and try again. Very large models need several GB of memory for the 3D view.";
            if (all.Any(e => e is FileNotFoundException || e is DirectoryNotFoundException))
                return "The file no longer exists.";
            if (all.Any(e => e is UnauthorizedAccessException))
                return "You don't have permission to read this file.";
            if (all.OfType<IOException>().Any(IsLocked))
                return "The file is open in another program that doesn't allow reading it.\n\nClose it there, or open a copy of the file.";
            if (all.Any(e => e.Message.IndexOf("not a valid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             e.GetType().Name.IndexOf("Parse", StringComparison.OrdinalIgnoreCase) >= 0))
                return $"This doesn't look like a valid IFC file, or the file is damaged.\n\nDetails: {ex.Message}";
            return $"Details: {ex.Message}\n\nMore information is in the log file:\n{Log.Folder}";
        }

        // The exception and all exceptions inside it (inner and aggregated ones).
        private static IEnumerable<Exception> Flatten(Exception ex)
        {
            if (ex == null) yield break;
            yield return ex;
            var children = ex is AggregateException aggregate ? aggregate.InnerExceptions : (IEnumerable<Exception>)new[] { ex.InnerException };
            foreach (var e in children.SelectMany(Flatten)) yield return e;
        }

        // Sharing (32) or lock (33) violation: another program holds the file.
        private static bool IsLocked(IOException ex) => (ex.HResult & 0xFFFF) == 32 || (ex.HResult & 0xFFFF) == 33;

        [RelayCommand]
        private void FitToView() => Viewer.FitToView();

        public void Dispose()
        {
            _ifcModel.Dispose();
            Viewer.Dispose();
        }
    }
}
