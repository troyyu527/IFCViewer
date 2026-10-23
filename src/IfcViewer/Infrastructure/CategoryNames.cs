using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcViewer.Infrastructure
{
    /// <summary>
    /// Revit-style category names for IFC objects ("Walls", "Doors", "Structural Columns"), as shown in the
    /// Properties panel header for a multiple selection ("Walls (3)"). Same categories as the default color
    /// table in GeometryService. Classes not listed fall back to their IFC class name.
    /// </summary>
    public static class CategoryNames
    {
        // Specific classes before their parents (e.g. a duct segment is also a flow segment in IFC4).
        public static string For(IPersistEntity entity)
        {
            switch (entity)
            {
                // Architecture
                case IIfcCurtainWall _: return "Curtain Walls";
                case IIfcWall _: return "Walls";
                case IIfcWindow _: return "Windows";
                case IIfcPlate _: return "Curtain Panels";
                case IIfcDoor _: return "Doors";
                case IIfcSlab _: return "Floors";
                case IIfcRoof _: return "Roofs";
                case IIfcCovering _: return "Ceilings";
                case IIfcStair _:
                case IIfcStairFlight _: return "Stairs";
                case IIfcRamp _:
                case IIfcRampFlight _: return "Ramps";
                case IIfcRailing _: return "Railings";
                case IIfcFurnishingElement _: return "Furniture";

                // Structure
                case IIfcColumn _: return "Structural Columns";
                case IIfcBeam _: return "Structural Framing";
                case IIfcMember _: return "Mullions / Braces";
                case IIfcFooting _:
                case IIfcPile _: return "Structural Foundations";
                case IIfcReinforcingElement _: return "Structural Rebar";

                // MEP
                case IIfcDuctSegment _: return "Ducts";
                case IIfcDuctFitting _: return "Duct Fittings";
                case IIfcPipeSegment _: return "Pipes";
                case IIfcPipeFitting _: return "Pipe Fittings";
                case IIfcCableCarrierSegment _: return "Cable Trays";
                case IIfcCableCarrierFitting _: return "Cable Tray Fittings";
                case IIfcSanitaryTerminal _: return "Plumbing Fixtures";
                case IIfcLightFixture _: return "Lighting Fixtures";
                case IIfcFlowSegment _: return "Flow Segments";      // IFC2x3: duct or pipe, system unknown
                case IIfcFlowFitting _: return "Flow Fittings";
                case IIfcFlowTerminal _: return "Flow Terminals";    // IFC2x3: lights, air terminals, ...
                case IIfcDistributionElement _: return "MEP Equipment";

                // Revit "Generic Models" are exported as proxies
                case IIfcBuildingElementProxy _: return "Generic Models";

                // Spatial structure (selectable in the object tree)
                case IIfcBuildingStorey _: return "Levels";
                case IIfcBuilding _: return "Buildings";
                case IIfcSite _: return "Sites";
                case IIfcProject _: return "Project";

                default: return entity?.ExpressType.Name ?? "";
            }
        }
    }
}
