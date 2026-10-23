using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HelixToolkit.Wpf.SharpDX;
using IfcViewer.Infrastructure;
using IfcViewer.Models;
using SharpDX;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;

namespace IfcViewer.Services
{
    /// <summary>
    /// Turns the IFC shapes of a model into one triangle mesh per element.
    /// </summary>
    public class GeometryService
    {
        private const double MinAlpha = 0.15;

        // Generates geometry on a background thread.
        // progress: Receives 0–100 while xBIM computes geometry (runs through several phases).
        public Task<List<ElementMesh>> BuildAsync(IfcStore model, IProgress<int> progress)
        {
            return Task.Run(() => Build(model, progress));
        }

        private static List<ElementMesh> Build(IfcStore model, IProgress<int> progress)
        {
            var context = new Xbim3DModelContext(model);
            context.CreateContext((percent, state) =>
            {
                if (percent >= 0 && percent <= 100) progress?.Report(percent);
            }, adjustWcs: true);

            // IFC files use different length units (mm, m, ...): convert everything to meters
            var toMeters = 1.0 / model.ModelFactors.OneMeter;

            var builders = new Dictionary<int, ElementBuilder>();
            foreach (var instance in context.ShapeInstances())
            {
                // Each element has several representations; this one is the final solid with openings cut out
                if (instance.RepresentationType != XbimGeometryRepresentationType.OpeningsAndAdditionsIncluded) continue;

                var product = model.Instances[instance.IfcProductLabel] as IIfcProduct;
                if (product == null || IsHidden(product)) continue;

                var geometry = context.ShapeGeometry(instance.ShapeGeometryLabel);
                if (geometry.Format != XbimGeometryType.PolyhedronBinary) continue;

                XbimShapeTriangulation triangulation;
                using (var stream = new MemoryStream(((IXbimShapeGeometryData)geometry).ShapeData))
                using (var reader = new BinaryReader(stream))
                {
                    triangulation = reader.ReadShapeTriangulation();
                }
                // Shape geometry is stored once and reused; the instance places it in the model
                triangulation = triangulation.Transform(instance.Transformation);

                if (!builders.TryGetValue(product.EntityLabel, out var builder))
                {
                    // An element made of differently colored parts takes the color of its first part
                    var (color, isTransparent) = GetStyleColor(model, instance) ?? GetDefaultColor(product);
                    builder = new ElementBuilder(product.EntityLabel, product.ExpressType.Name, color, isTransparent);
                    builders.Add(product.EntityLabel, builder);
                }
                builder.Add(triangulation, toMeters);
            }

            // Move the model to the origin. Done in double precision before converting to float,
            // so geo-referenced models (coordinates in the millions) don't lose precision and jitter.
            var center = GetCenter(builders.Values);
            var meshes = builders.Values.Select(b => b.ToElementMesh(center)).ToList();

            // Building leaves temporary data behind (double-precision copies, list growth, xBIM's shape data): for a
            // large model several GB, e.g. 2.6 GB for a 385 MB file. Free it before the 3D scene allocates its buffers.
            builders.Clear();
            MemoryCleanup.Release();
            return meshes;
        }

        // Spaces, zones and openings are invisible volumes that would only block clicks.
        private static bool IsHidden(IIfcProduct product)
        {
            return product is IIfcSpace || product is IIfcSpatialZone || product is IIfcOpeningElement;
        }

        // Color from the IFC surface style, or null if the shape has none.
        private static (Color4 Color, bool IsTransparent)? GetStyleColor(IfcStore model, XbimShapeInstance instance)
        {
            if (!instance.HasStyle) return null;

            var style = model.Instances[instance.StyleLabel] as IIfcSurfaceStyle;
            var shading = style?.Styles.OfType<IIfcSurfaceStyleShading>().FirstOrDefault();
            if (shading?.SurfaceColour == null) return null;

            var colour = shading.SurfaceColour;
            // IFC transparency: 0 = opaque, 1 = fully transparent.
            // Keep at least 15% opacity: some exports mark real elements fully transparent,
            // which would make them invisible but still clickable.
            var transparency = shading.Transparency.HasValue ? (double)shading.Transparency.Value : 0.0;
            var alpha = Math.Max(1 - transparency, MinAlpha);
            var color = new Color4((float)colour.Red, (float)colour.Green, (float)colour.Blue, (float)alpha);
            return (color, alpha < 0.99);
        }

        // Fallback color by element type, for shapes without a surface style.
        // IFC defines no standard colors; this follows common coordination practice
        // (one color per Revit category; ducts blue, pipes green, electrical yellow).
        // Specific types must come before their parent type (e.g. duct before flow segment).
        private static (Color4 Color, bool IsTransparent) GetDefaultColor(IIfcProduct product)
        {
            switch (product)
            {
                // Architecture
                case IIfcWall _:                return Opaque(0.90f, 0.87f, 0.80f);  // Walls: warm off-white
                case IIfcCurtainWall _:
                case IIfcWindow _:
                case IIfcPlate _:               return (new Color4(0.55f, 0.75f, 0.90f, 0.4f), true);  // Curtain panels, windows: glass
                case IIfcDoor _:                return Opaque(0.60f, 0.42f, 0.25f);  // Doors: brown
                case IIfcSlab _:                return Opaque(0.62f, 0.62f, 0.60f);  // Floors: concrete grey
                case IIfcRoof _:                return Opaque(0.65f, 0.30f, 0.22f);  // Roofs: terracotta
                case IIfcCovering _:            return Opaque(0.96f, 0.96f, 0.93f);  // Ceilings, finishes: near white
                case IIfcStair _:
                case IIfcStairFlight _:
                case IIfcRamp _:
                case IIfcRampFlight _:          return Opaque(0.80f, 0.72f, 0.55f);  // Stairs, ramps: sand
                case IIfcRailing _:             return Opaque(0.35f, 0.35f, 0.38f);  // Railings: dark metal
                case IIfcFurnishingElement _:   return Opaque(0.80f, 0.65f, 0.45f);  // Furniture, casework: light wood

                // Structure
                case IIfcColumn _:              return Opaque(0.90f, 0.55f, 0.25f);  // Columns: orange
                case IIfcBeam _:                return Opaque(0.55f, 0.40f, 0.75f);  // Structural framing: purple
                case IIfcMember _:              return Opaque(0.55f, 0.58f, 0.62f);  // Braces, mullions: steel grey
                case IIfcFooting _:
                case IIfcPile _:                return Opaque(0.55f, 0.50f, 0.45f);  // Foundations: dark earth grey
                case IIfcReinforcingElement _:  return Opaque(0.70f, 0.25f, 0.20f);  // Rebar: rust red

                // MEP (IFC4 files name the system; IFC2x3 only says "flow segment/fitting")
                case IIfcDuctSegment _:
                case IIfcDuctFitting _:         return Opaque(0.25f, 0.50f, 0.85f);  // Ducts: blue
                case IIfcPipeSegment _:
                case IIfcPipeFitting _:         return Opaque(0.25f, 0.65f, 0.35f);  // Pipes: green
                case IIfcCableCarrierSegment _:
                case IIfcCableCarrierFitting _: return Opaque(0.95f, 0.80f, 0.20f);  // Cable trays: yellow
                case IIfcFlowSegment _:
                case IIfcFlowFitting _:         return Opaque(0.40f, 0.65f, 0.65f);  // Unknown system: teal
                case IIfcSanitaryTerminal _:    return Opaque(0.97f, 0.97f, 1.00f);  // Plumbing fixtures: white
                case IIfcLightFixture _:        return Opaque(1.00f, 0.92f, 0.55f);  // Lighting fixtures: light yellow
                case IIfcDistributionElement _: return Opaque(0.45f, 0.50f, 0.55f);  // Equipment, air terminals, other MEP

                // Unclassified (incl. Revit "Generic Models" → IfcBuildingElementProxy)
                default:                        return Opaque(0.80f, 0.80f, 0.80f);
            }
        }

        private static (Color4 Color, bool IsTransparent) Opaque(float r, float g, float b)
        {
            return (new Color4(r, g, b, 1f), false);
        }

        private static Vector3D GetCenter(IEnumerable<ElementBuilder> builders)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var p in builders.SelectMany(b => b.Positions))
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); minZ = Math.Min(minZ, p.Z);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); maxZ = Math.Max(maxZ, p.Z);
            }
            if (minX > maxX) return new Vector3D(0, 0, 0); // empty model
            return new Vector3D((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        }

        /// <summary>
        /// Double-precision point, used before the model is moved to the origin.
        /// </summary>
        private struct Vector3D
        {
            public readonly double X, Y, Z;
            public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
        }

        /// <summary>
        /// Collects the triangles of one element from all its shapes.
        /// </summary>
        private class ElementBuilder
        {
            private readonly int _label;
            private readonly string _ifcClass;
            private readonly Color4 _color;
            private readonly bool _isTransparent;
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<int> _indices = new List<int>();

            public List<Vector3D> Positions { get; } = new List<Vector3D>();

            public ElementBuilder(int label, string ifcClass, Color4 color, bool isTransparent)
            {
                _label = label;
                _ifcClass = ifcClass;
                _color = color;
                _isTransparent = isTransparent;
            }

            public void Add(XbimShapeTriangulation triangulation, double toMeters)
            {
                var vertices = triangulation.Vertices;
                foreach (var face in triangulation.Faces)
                {
                    var indices = face.Indices;
                    for (var i = 0; i < indices.Count; i++)
                    {
                        var p = vertices[indices[i]];
                        // Planar faces store one normal for the whole face, curved faces one per index
                        var n = (face.IsPlanar ? face.Normals[0] : face.Normals[i]).Normal;

                        Positions.Add(new Vector3D(p.X * toMeters, p.Y * toMeters, p.Z * toMeters));
                        _normals.Add(new Vector3((float)n.X, (float)n.Y, (float)n.Z));
                        _indices.Add(Positions.Count - 1);
                    }
                }
            }

            public ElementMesh ToElementMesh(Vector3D center)
            {
                var positions = new Vector3Collection(Positions.Count);
                foreach (var p in Positions)
                {
                    positions.Add(new Vector3((float)(p.X - center.X), (float)(p.Y - center.Y), (float)(p.Z - center.Z)));
                }

                return new ElementMesh
                {
                    ProductLabel = _label,
                    IfcClass = _ifcClass,
                    Color = _color,
                    IsTransparent = _isTransparent,
                    Geometry = new MeshGeometry3D
                    {
                        Positions = positions,
                        Normals = new Vector3Collection(_normals),
                        Indices = new IntCollection(_indices),
                    },
                    Bounds = BoundingBox.FromPoints(positions.ToArray()),
                };
            }
        }
    }
}
