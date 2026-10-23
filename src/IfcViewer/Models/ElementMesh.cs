using HelixToolkit.Wpf.SharpDX;
using SharpDX;

namespace IfcViewer.Models
{
    /// <summary>
    /// Triangles of one IFC element, ready to display. All shapes of the element are merged into one mesh.
    /// Coordinates are in meters, moved so the whole model is centered on the origin.
    /// </summary>
    public class ElementMesh
    {
        // IFC entity label of the element — used to look up its properties.
        public int ProductLabel { get; set; }

        // IFC class name, e.g. "IfcWall".
        public string IfcClass { get; set; }

        public Color4 Color { get; set; }
        public bool IsTransparent { get; set; }

        public MeshGeometry3D Geometry { get; set; }

        // Bounding box of this element (used for the rotation pivot in Step 7).
        public BoundingBox Bounds { get; set; }
    }
}
