using System;
using System.Collections.Generic;
using System.Linq;
using IfcViewer.Infrastructure;
using IfcViewer.Models;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace IfcViewer.Services
{
    /// <summary>
    /// Reads what the Properties panel shows: attributes, type, property sets, quantities and materials of an
    /// element, with values formatted in feet-inches / ft² / ft³. Works for IFC2x3 and IFC4 through the shared
    /// IIfc* interfaces.
    /// </summary>
    public class PropertyService
    {
        public const string MultipleCategories = "Multiple Categories";

        private IModel _unitsModel;
        private UnitFactors _units;
        private IModel _applicationModel;
        private string _fileApplication;

        // Reads what's the same for every element of a file (units, source application), so the first click doesn't
        // pay for it. Call once after loading, on a background thread: on a large model kept on disk it takes seconds.
        public void Prepare(IModel model)
        {
            UnitsOf(model);
            FileApplicationOf(model);
        }

        public ElementInfo GetInfo(IModel model, int label)
        {
            var entity = model.Instances[label];
            if (entity == null) return new ElementInfo { Name = "", IfcClass = "", Category = "" };
            var units = UnitsOf(model);

            var info = new ElementInfo
            {
                Name = Text((entity as IIfcRoot)?.Name),
                IfcClass = entity.ExpressType.Name,
                Category = CategoryNames.For(entity),
            };

            // Always the same rows, so they can be compared in a multiple selection
            var attributes = new PropertyGroup("Attributes");
            attributes.Add("Name", info.Name);
            attributes.Add("IFC class", info.IfcClass);
            attributes.Add("GlobalId", Text((entity as IIfcRoot)?.GlobalId));
            attributes.Add("Description", Text((entity as IIfcRoot)?.Description));
            attributes.Add("Object type", Text((entity as IIfcObject)?.ObjectType));
            // IFC "Tag": the authoring program's own ID (Revit Element ID, SketchUp ID path, ...)
            attributes.Add("ID", Text((entity as IIfcElement)?.Tag));
            attributes.Add("Source application", SourceApplication(model, entity as IIfcRoot));
            attributes.Add("Level", StoreyName(entity as IIfcObjectDefinition));
            info.Groups.Add(attributes);

            // Shown even without a type (empty rows), so every element has the same rows
            var obj = entity as IIfcObject;
            var type = obj?.IsTypedBy.FirstOrDefault()?.RelatingType;
            var typeGroup = new PropertyGroup("Type");
            typeGroup.Add("Name", Text(type?.Name));
            typeGroup.Add("IFC class", type?.ExpressType.Name ?? "");
            info.Groups.Add(typeGroup);

            // Property and quantity sets of the element, then those of its type
            var definitions = obj?.IsDefinedBy
                .SelectMany(rel => rel.RelatingPropertyDefinition.PropertySetDefinitions)
                .ToList() ?? new List<IIfcPropertySetDefinition>();
            foreach (var pset in definitions.OfType<IIfcPropertySet>().OrderBy(p => Text(p.Name)))
            {
                info.Groups.Add(PropertySetGroup(pset, Text(pset.Name), units));
            }
            if (type?.HasPropertySets != null)
            {
                foreach (var pset in type.HasPropertySets.OfType<IIfcPropertySet>().OrderBy(p => Text(p.Name)))
                {
                    info.Groups.Add(PropertySetGroup(pset, Text(pset.Name) + " (from type)", units));
                }
            }
            foreach (var quantities in definitions.OfType<IIfcElementQuantity>().OrderBy(q => Text(q.Name)))
            {
                info.Groups.Add(QuantityGroup(quantities, units));
            }

            var materials = MaterialGroup(entity as IIfcObjectDefinition, units)
                            ?? MaterialGroup(type, units, " (from type)");
            if (materials == null)
            {
                // No material at all: an empty row instead of no section
                materials = new PropertyGroup("Materials");
                materials.Add("Material", "");
            }
            info.Groups.Add(materials);

            return info;
        }

        // Combines several elements into one view: only groups and rows present in all of them; a row whose
        // value differs shows PropertyGroup.Varies.
        public static ElementInfo Merge(IReadOnlyList<ElementInfo> infos)
        {
            if (infos.Count == 1) return infos[0];

            var first = infos[0];
            var sameCategory = AllSame(infos.Select(i => i.Category));
            var merged = new ElementInfo
            {
                Name = AllSame(infos.Select(i => i.Name)) ? first.Name : PropertyGroup.Varies,
                Category = sameCategory ? first.Category : null,
                IfcClass = !sameCategory ? MultipleCategories
                         : AllSame(infos.Select(i => BaseClass(i.IfcClass))) ? BaseClass(first.IfcClass)
                         : PropertyGroup.Varies,
            };

            foreach (var group in first.Groups)
            {
                var sameGroups = infos.Skip(1).Select(i => i.Groups.FirstOrDefault(g => g.Title == group.Title)).ToList();
                if (sameGroups.Any(g => g == null)) continue;

                var mergedGroup = new PropertyGroup(group.Title);
                foreach (var item in group.Items)
                {
                    var values = new List<string> { item.Value };
                    var inAll = true;
                    foreach (var other in sameGroups)
                    {
                        var match = other.Items.FirstOrDefault(i => i.Name == item.Name);
                        if (match == null)
                        {
                            inAll = false;
                            break;
                        }
                        values.Add(match.Value);
                    }
                    if (inAll) mergedGroup.Add(item.Name, AllSame(values) ? item.Value : PropertyGroup.Varies);
                }
                if (mergedGroup.Items.Count > 0) merged.Groups.Add(mergedGroup);
            }
            return merged;
        }

        private static bool AllSame(IEnumerable<string> values) => values.Distinct().Count() <= 1;

        // The class without IFC's "StandardCase" / "ElementedCase" variants, so three walls of mixed kinds
        // (IfcWall, IfcWallStandardCase) show "IfcWall" under "Walls (3)".
        private static string BaseClass(string ifcClass)
        {
            foreach (var suffix in new[] { "StandardCase", "ElementedCase" })
            {
                if (ifcClass.EndsWith(suffix, StringComparison.Ordinal)) return ifcClass.Substring(0, ifcClass.Length - suffix.Length);
            }
            return ifcClass;
        }

        // ----- Groups -----

        private static PropertyGroup PropertySetGroup(IIfcPropertySet pset, string title, UnitFactors units)
        {
            var group = new PropertyGroup(title);
            foreach (var property in pset.HasProperties.OrderBy(p => Text(p.Name)))
            {
                group.Add(Text(property.Name), PropertyValue(property, units));
            }
            return group;
        }

        private static string PropertyValue(IIfcProperty property, UnitFactors units)
        {
            switch (property)
            {
                case IIfcPropertySingleValue single:
                    return units.Format(single.NominalValue);
                case IIfcPropertyEnumeratedValue enumerated:
                    return string.Join(", ", enumerated.EnumerationValues.Select(units.Format));
                case IIfcPropertyListValue list:
                    return string.Join(", ", list.ListValues.Select(units.Format));
                case IIfcPropertyBoundedValue bounded:
                    return $"{units.Format(bounded.LowerBoundValue)} – {units.Format(bounded.UpperBoundValue)}";
                default:
                    // Tables, references and complex properties: name the kind only
                    return $"({property.ExpressType.Name})";
            }
        }

        private static PropertyGroup QuantityGroup(IIfcElementQuantity quantities, UnitFactors units)
        {
            var group = new PropertyGroup(Text(quantities.Name));
            foreach (var quantity in quantities.Quantities.OrderBy(q => Text(q.Name)))
            {
                string value;
                switch (quantity)
                {
                    case IIfcQuantityLength length: value = units.Format(length.LengthValue); break;
                    case IIfcQuantityArea area: value = units.Format(area.AreaValue); break;
                    case IIfcQuantityVolume volume: value = units.Format(volume.VolumeValue); break;
                    case IIfcQuantityCount count: value = units.Format(count.CountValue); break;
                    case IIfcQuantityWeight weight: value = units.Format(weight.WeightValue) + " kg"; break;
                    default: value = $"({quantity.ExpressType.Name})"; break;
                }
                group.Add(Text(quantity.Name), value);
            }
            return group;
        }

        // Material rows; null if the object has no material.
        private static PropertyGroup MaterialGroup(IIfcObjectDefinition definition, UnitFactors units, string suffix = "")
        {
            var select = definition?.HasAssociations.OfType<IIfcRelAssociatesMaterial>().FirstOrDefault()?.RelatingMaterial;
            if (select == null) return null;

            var group = new PropertyGroup("Materials" + suffix);
            switch (select)
            {
                case IIfcMaterial material:
                    group.Add("Material", Text(material.Name));
                    break;
                case IIfcMaterialList list:
                    var i = 1;
                    foreach (var material in list.Materials) group.Add($"Material {i++}", Text(material.Name));
                    break;
                case IIfcMaterialLayerSetUsage usage:
                    AddLayers(group, usage.ForLayerSet, units);
                    break;
                case IIfcMaterialLayerSet layerSet:
                    AddLayers(group, layerSet, units);
                    break;
                case IIfcMaterialProfileSetUsage profileUsage:
                    AddProfiles(group, profileUsage.ForProfileSet);
                    break;
                case IIfcMaterialProfileSet profileSet:
                    AddProfiles(group, profileSet);
                    break;
                case IIfcMaterialConstituentSet constituents:
                    foreach (var constituent in constituents.MaterialConstituents)
                    {
                        group.Add(Text(constituent.Name) is var name && name.Length > 0 ? name : "Constituent",
                                  Text(constituent.Material?.Name));
                    }
                    break;
                default:
                    group.Add("Material", $"({select.GetType().Name})");
                    break;
            }
            return group;
        }

        private static void AddLayers(PropertyGroup group, IIfcMaterialLayerSet layerSet, UnitFactors units)
        {
            if (layerSet == null) return;
            group.Add("Layer set", Text(layerSet.LayerSetName));
            var i = 1;
            foreach (var layer in layerSet.MaterialLayers)
            {
                group.Add($"Layer {i++}: {Text(layer.Material?.Name)}", units.Format(layer.LayerThickness));
            }
        }

        private static void AddProfiles(PropertyGroup group, IIfcMaterialProfileSet profileSet)
        {
            if (profileSet == null) return;
            var i = 1;
            foreach (var profile in profileSet.MaterialProfiles)
            {
                group.Add($"Profile {i++}: {Text(profile.Material?.Name)}", Text(profile.Profile?.ProfileName));
            }
        }

        // The storey (or other spatial element) containing the object, also for parts of an element.
        private static string StoreyName(IIfcObjectDefinition definition)
        {
            while (definition != null)
            {
                if (definition is IIfcElement element)
                {
                    var structure = element.ContainedInStructure.FirstOrDefault()?.RelatingStructure;
                    if (structure != null) return Text(structure.Name);
                }
                // Parts (e.g. a curtain wall panel) are not placed themselves; ask the element they belong to
                definition = definition.Decomposes.FirstOrDefault()?.RelatingObject;
            }
            return "";
        }

        // The program that wrote the object: its own owner history (IFC2x3 requires one, IFC4 makes it optional),
        // else the file's first IfcApplication, else the file header's "originating system", else its
        // "preprocessor" (the export library, e.g. "BSProLib (Ver:2023.7.0.17)"); empty if none. Blank values
        // (some files write a single space) count as none.
        private string SourceApplication(IModel model, IIfcRoot root)
        {
            var own = Text(root?.OwnerHistory?.OwningApplication?.ApplicationFullName).Trim();
            return own.Length > 0 ? own : FileApplicationOf(model);
        }

        // The file-level fallbacks, looked up once per model: searching the file for an IfcApplication scans every
        // entity when there is none — fast in memory, ~4 s per click on a large model kept on disk.
        private string FileApplicationOf(IModel model)
        {
            if (!ReferenceEquals(model, _applicationModel))
            {
                var candidates = new[]
                {
                    Text(model.Instances.FirstOrDefault<IIfcApplication>()?.ApplicationFullName),
                    model.Header?.FileName?.OriginatingSystem,
                    model.Header?.FileName?.PreprocessorVersion,
                };
                _fileApplication = candidates.Select(c => c?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? "";
                _applicationModel = model;
            }
            return _fileApplication;
        }

        private static string Text(object value) => value?.ToString() ?? "";

        // ----- Units -----

        private UnitFactors UnitsOf(IModel model)
        {
            if (!ReferenceEquals(model, _unitsModel))
            {
                _units = new UnitFactors(model);
                _unitsModel = model;
            }
            return _units;
        }

        /// <summary>
        /// Factors from the file's units to SI (m, m², m³, radians). Read per kind, because files often mix them
        /// (Revit: lengths in mm, areas in m²).
        /// </summary>
        private class UnitFactors
        {
            private readonly double _length = 1, _area = 1, _volume = 1, _angle = 1;

            public UnitFactors(IModel model)
            {
                var units = model.Instances.FirstOrDefault<IIfcProject>()?.UnitsInContext?.Units.OfType<IIfcNamedUnit>()
                            ?? Enumerable.Empty<IIfcNamedUnit>();
                foreach (var unit in units)
                {
                    switch (unit.UnitType)
                    {
                        case IfcUnitEnum.LENGTHUNIT: _length = ToSi(unit, 1); break;
                        case IfcUnitEnum.AREAUNIT: _area = ToSi(unit, 2); break;
                        case IfcUnitEnum.VOLUMEUNIT: _volume = ToSi(unit, 3); break;
                        case IfcUnitEnum.PLANEANGLEUNIT: _angle = ToSi(unit, 1); break;
                    }
                }
            }

            // The value as display text, converted by its IFC measure type.
            public string Format(IIfcValue value)
            {
                if (value == null) return "";
                var raw = value.Value;
                switch (value.GetType().Name)
                {
                    case "IfcLengthMeasure":
                    case "IfcPositiveLengthMeasure":
                    case "IfcNonNegativeLengthMeasure":
                        return UnitFormatter.Length(Convert.ToDouble(raw) * _length);
                    case "IfcAreaMeasure":
                        return UnitFormatter.Area(Convert.ToDouble(raw) * _area);
                    case "IfcVolumeMeasure":
                        return UnitFormatter.Volume(Convert.ToDouble(raw) * _volume);
                    case "IfcPlaneAngleMeasure":
                    case "IfcPositivePlaneAngleMeasure":
                        return UnitFormatter.Number(Convert.ToDouble(raw) * _angle * 180 / Math.PI) + "°";
                    case "IfcBoolean":
                        return raw is bool b && b ? "Yes" : "No";
                    case "IfcLogical":
                        return raw is bool l ? (l ? "Yes" : "No") : "Unknown";
                }
                switch (raw)
                {
                    case double d: return UnitFormatter.Number(d);
                    case float f: return UnitFormatter.Number(f);
                    case bool flag: return flag ? "Yes" : "No";
                    default: return raw?.ToString() ?? "";
                }
            }

            private static double ToSi(IIfcNamedUnit unit, int dimension)
            {
                switch (unit)
                {
                    case IIfcSIUnit si:
                        return si.Prefix.HasValue ? Math.Pow(PrefixFactor(si.Prefix.Value), dimension) : 1;
                    case IIfcConversionBasedUnit converted:
                        // e.g. FOOT = 0.3048 × METRE, SQUARE FOOT = 0.0929 × SQUARE_METRE
                        var factor = Convert.ToDouble(converted.ConversionFactor.ValueComponent.Value);
                        return converted.ConversionFactor.UnitComponent is IIfcNamedUnit baseUnit
                            ? factor * ToSi(baseUnit, dimension)
                            : factor;
                    default:
                        return 1;
                }
            }

            private static double PrefixFactor(IfcSIPrefix prefix)
            {
                switch (prefix)
                {
                    case IfcSIPrefix.EXA: return 1e18;
                    case IfcSIPrefix.PETA: return 1e15;
                    case IfcSIPrefix.TERA: return 1e12;
                    case IfcSIPrefix.GIGA: return 1e9;
                    case IfcSIPrefix.MEGA: return 1e6;
                    case IfcSIPrefix.KILO: return 1e3;
                    case IfcSIPrefix.HECTO: return 1e2;
                    case IfcSIPrefix.DECA: return 1e1;
                    case IfcSIPrefix.DECI: return 1e-1;
                    case IfcSIPrefix.CENTI: return 1e-2;
                    case IfcSIPrefix.MILLI: return 1e-3;
                    case IfcSIPrefix.MICRO: return 1e-6;
                    case IfcSIPrefix.NANO: return 1e-9;
                    case IfcSIPrefix.PICO: return 1e-12;
                    case IfcSIPrefix.FEMTO: return 1e-15;
                    case IfcSIPrefix.ATTO: return 1e-18;
                    default: return 1;
                }
            }
        }
    }
}
