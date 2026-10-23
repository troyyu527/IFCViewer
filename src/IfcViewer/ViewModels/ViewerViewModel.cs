using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelixToolkit.Wpf.SharpDX;
using IfcViewer.Models;
using SharpDX;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;

namespace IfcViewer.ViewModels
{
    /// <summary>
    /// 3D scene, camera, selection and visibility state.
    /// </summary>
    public partial class ViewerViewModel : ObservableObject, IDisposable
    {
        public IEffectsManager EffectsManager { get; } = new DefaultEffectsManager();

        // IFC models are Z-up
        public Camera Camera { get; } = new PerspectiveCamera
        {
            Position = new Point3D(6, -8, 5),
            LookDirection = new Vector3D(-6, 8, -5),
            UpDirection = new Vector3D(0, 0, 1),
            NearPlaneDistance = 0.1,
            FarPlaneDistance = 10000,
        };

        public ObservableElement3DCollection SceneModels { get; } = new ObservableElement3DCollection();

        // Selection highlights, one per selected, visible element that has a mesh. Kept apart from
        // SceneModels so loading a new scene doesn't touch them.
        public ObservableElement3DCollection OverlayModels { get; } = new ObservableElement3DCollection();

        // Shared by all highlights (own material, because element materials are shared per color).
        // Color: the usual BIM selection blue (Revit/Navisworks #003BBD), lightened to a vivid #3D8CFF.
        // High opacity plus an emissive part make it stronger and brighter than any lit surface, so it
        // stands out even on pale-blue glass or a blue element.
        private readonly PhongMaterial _highlightMaterial = new PhongMaterial
        {
            DiffuseColor = new Color4(0.24f, 0.55f, 1f, 0.75f),   // #3D8CFF, 75% opacity
            AmbientColor = new Color4(0.24f, 0.55f, 1f, 1f),
            EmissiveColor = new Color4(0.12f, 0.27f, 0.5f, 1f),
        };

        // Meshes of the loaded model by IFC entity label.
        private readonly Dictionary<int, ElementMesh> _meshes = new Dictionary<int, ElementMesh>();

        // The 3D model drawing each element, by IFC entity label (shown / hidden here).
        private readonly Dictionary<int, MeshGeometryModel3D> _models = new Dictionary<int, MeshGeometryModel3D>();

        // Highlight overlays of the selected elements by IFC entity label.
        private readonly Dictionary<int, MeshGeometryModel3D> _highlights = new Dictionary<int, MeshGeometryModel3D>();

        private readonly List<int> _selectedLabels = new List<int>();
        private readonly HashSet<int> _hiddenLabels = new HashSet<int>();

        // IFC entity labels of the selected elements, in selection order. A label may have no mesh
        // (e.g. a storey selected in the tree): it is selected but not highlighted.
        public IReadOnlyList<int> SelectedLabels => _selectedLabels;

        public bool HasSelection => _selectedLabels.Count > 0;

        // Raised when SelectedLabels changes.
        public event EventHandler SelectionChanged;

        // IFC entity labels of the hidden elements (Isolate / Hide).
        public IReadOnlyCollection<int> HiddenLabels => _hiddenLabels;

        // Raised when HiddenLabels changes.
        public event EventHandler VisibilityChanged;

        // Bounding box of the whole loaded model (meters, centered on the origin).
        public BoundingBox ModelBounds { get; private set; }

        // Bounding box of the visible elements; null if nothing is visible.
        public BoundingBox? VisibleBounds => MergeBounds(_meshes.Values.Where(m => !_hiddenLabels.Contains(m.ProductLabel)));

        // Point the camera orbits around: the center of the combined box of all selected elements, or the
        // center of the visible elements when nothing is selected. Changing it doesn't move the camera; it
        // only affects the next rotation.
        [ObservableProperty] private Point3D _rotationPivot = new Point3D(0, 0, 0);

        private Point3D ModelCenter => ToPoint3D(ModelBounds.Center);

        // Raised when the view should frame the visible elements. Handled by the View.
        public event EventHandler FitToViewRequested;

        public void FitToView() => FitToViewRequested?.Invoke(this, EventArgs.Empty);

        // ----- Selection -----

        // Selects only this element (replaces the selection).
        public void Select(int label) => SetSelection(new[] { label });

        // Ctrl + click: adds the element, or removes it if it is already selected.
        public void ToggleSelection(int label)
        {
            var labels = new List<int>(_selectedLabels);
            if (!labels.Remove(label)) labels.Add(label);
            SetSelection(labels);
        }

        // Selects exactly these elements (replaces the selection).
        public void SelectMany(IEnumerable<int> labels) => SetSelection(labels);

        public void ClearSelection() => SetSelection(Array.Empty<int>());

        private void SetSelection(IEnumerable<int> labels)
        {
            var newLabels = labels.Distinct().ToList();
            if (new HashSet<int>(newLabels).SetEquals(_selectedLabels)) return;

            _selectedLabels.Clear();
            _selectedLabels.AddRange(newLabels);
            SyncHighlights();
            UpdatePivot();
            IsolateSelectedCommand.NotifyCanExecuteChanged();
            HideSelectedCommand.NotifyCanExecuteChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ----- Visibility (context menu) -----

        // Shows only the selected elements; everything else is hidden. The selection is kept.
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void IsolateSelected()
        {
            var keep = new HashSet<int>(_selectedLabels);
            SetHidden(_models.Keys.Where(label => !keep.Contains(label)));
        }

        // Hides the selected elements (adding to what is already hidden) and deselects them.
        [RelayCommand(CanExecute = nameof(HasSelection))]
        private void HideSelected()
        {
            SetHidden(_hiddenLabels.Concat(_selectedLabels));
            ClearSelection();
        }

        // Shows every element again.
        [RelayCommand]
        private void ResetVisibility() => SetHidden(Array.Empty<int>());

        private void SetHidden(IEnumerable<int> labels)
        {
            var newHidden = new HashSet<int>(labels);
            if (newHidden.SetEquals(_hiddenLabels)) return;

            _hiddenLabels.Clear();
            _hiddenLabels.UnionWith(newHidden);
            foreach (var pair in _models)
            {
                var visible = !_hiddenLabels.Contains(pair.Key);
                pair.Value.IsRendering = visible;
                // Clicks go through hidden elements to whatever is behind them
                pair.Value.IsHitTestVisible = visible;
            }
            SyncHighlights();
            UpdatePivot();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        // ----- Highlights and pivot -----

        // Makes the overlays match the state: one per selected element that has a mesh and is visible.
        // Overlays that are still needed are kept, so adding one element doesn't rebuild the others.
        private void SyncHighlights()
        {
            var wanted = new HashSet<int>(_selectedLabels.Where(l => _meshes.ContainsKey(l) && !_hiddenLabels.Contains(l)));

            foreach (var label in _highlights.Keys.Where(l => !wanted.Contains(l)).ToList())
            {
                OverlayModels.Remove(_highlights[label]);
                _highlights.Remove(label);
            }
            foreach (var label in _selectedLabels)
            {
                if (!wanted.Contains(label) || _highlights.ContainsKey(label)) continue;
                var highlight = CreateHighlight(_meshes[label]);
                _highlights.Add(label, highlight);
                OverlayModels.Add(highlight);
            }
        }

        // Semi-transparent copy of an element, drawn on top of it.
        private MeshGeometryModel3D CreateHighlight(ElementMesh mesh)
        {
            return new MeshGeometryModel3D
            {
                Geometry = mesh.Geometry, // same triangles → the overlay matches the shape exactly
                Material = _highlightMaterial,
                IsTransparent = true,
                // Pulls the overlay slightly towards the camera, so it wins against the identical surface
                // of the element below instead of flickering (z-fighting)
                DepthBias = -10,
                // Clicks go through to the element below
                IsHitTestVisible = false,
            };
        }

        // Selected visible elements' combined center; else the visible elements' center.
        private void UpdatePivot()
        {
            var selected = MergeBounds(_selectedLabels
                .Where(l => !_hiddenLabels.Contains(l) && _meshes.ContainsKey(l))
                .Select(l => _meshes[l]));
            var box = selected ?? VisibleBounds;
            RotationPivot = box == null ? ModelCenter : ToPoint3D(box.Value.Center);
        }

        // One box around all given meshes; null if there are none.
        private static BoundingBox? MergeBounds(IEnumerable<ElementMesh> meshes)
        {
            BoundingBox? box = null;
            foreach (var mesh in meshes)
            {
                box = box == null ? mesh.Bounds : BoundingBox.Merge(box.Value, mesh.Bounds);
            }
            return box;
        }

        // ----- Scene -----

        // Replaces the scene with the given elements (one 3D model per element) and frames it.
        // Must be called on the UI thread.
        public void LoadScene(IReadOnlyList<ElementMesh> meshes)
        {
            // The selected and hidden elements belong to the old scene
            ClearSelection();
            SceneModels.Clear();
            _meshes.Clear();
            _models.Clear();
            if (_hiddenLabels.Count > 0)
            {
                _hiddenLabels.Clear();
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }

            // Elements with the same color share one material
            var materials = new Dictionary<Color4, PhongMaterial>();
            var bounds = new BoundingBox();
            var first = true;

            foreach (var mesh in meshes)
            {
                if (!materials.TryGetValue(mesh.Color, out var material))
                {
                    material = new PhongMaterial
                    {
                        DiffuseColor = mesh.Color,
                        // Ambient light is multiplied by this; Helix's default is black, which makes
                        // faces not facing the headlight almost black
                        AmbientColor = new Color4(mesh.Color.Red, mesh.Color.Green, mesh.Color.Blue, 1f),
                    };
                    materials.Add(mesh.Color, material);
                }

                var model = new MeshGeometryModel3D
                {
                    Geometry = mesh.Geometry,
                    Material = material,
                    IsTransparent = mesh.IsTransparent,
                    Tag = mesh, // lets selection (Step 6) find the element behind a clicked model
                };
                SceneModels.Add(model);
                _meshes[mesh.ProductLabel] = mesh;
                _models[mesh.ProductLabel] = model;

                bounds = first ? mesh.Bounds : BoundingBox.Merge(bounds, mesh.Bounds);
                first = false;
            }

            ModelBounds = bounds;
            RotationPivot = ModelCenter;
            FitToView();
        }

        private static Point3D ToPoint3D(Vector3 v) => new Point3D(v.X, v.Y, v.Z);

        public void Dispose()
        {
            (EffectsManager as IDisposable)?.Dispose();
        }
    }
}
