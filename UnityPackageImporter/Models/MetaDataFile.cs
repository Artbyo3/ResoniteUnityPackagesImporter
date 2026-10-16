using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FrooxEngine;
using Renderite.Shared;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityPackageImporter.Models;
using YamlDotNet.Serialization;

namespace UnityPackageImporter;

public class MetaDataFile
{
    public float GlobalScale = 1;
    public float UnitScaleFactor = 100f;
    public bool UseFileUnits = true;
    public bool UseFileScale = true;
    public float CalculatedScaleFactor = 1f;
    public Dictionary<string, SourceObj> externalObjects = new Dictionary<string, SourceObj>();
    public Dictionary<BodyNode, Slot> storagebones = new Dictionary<BodyNode, Slot>();
    public Dictionary<long, string> fileIDToRecycleName = new Dictionary<long, string>();
    public MetaDataFile() { }
            
    private static readonly IDeserializer SharedDeserializer =
        new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    // The second I tried to make this a yaml parser the parser broke. if anyone else can get it to work that's fine by me. - @989onan
    // This creates our biped rig under the slot we specify. in this case, it is the root.
    // This MetaDataFile object class gives us access to the biped rig component directly if desired
    public async Task GenerateComponents(BipedRig rig) 
    {
        UnityPackageImporter.Msg("Humanoid bone description being instanciated is: " + (rig != null));
        await default(ToBackground);

        // So that we add the bone without parsing it's children
        // If we use """AssignBones(Rig.BoneNode root, bool ignoreDuplicates)""" that will cause errors.
        foreach (var BoneNode in storagebones)
        {
            if (BoneNode.Key != BodyNode.NONE && BoneNode.Value != null)
            {
                UnityPackageImporter.Msg("Assigning bone: " + BoneNode.Value.Name);
                await default(ToWorld);
                rig.Bones[BoneNode.Key] = BoneNode.Value;
                await default(ToBackground);
            }
            else
            {
                UnityPackageImporter.Msg("Assigning bone was null! Idk what it was...");
            }
        }
    }

    // It scans the file's metadata to make it work.
    // It also finds alternative GUID's that unity makes for fbx objects
    // But we're using it to also get our global scale.
    public async Task ScanFile(FileImportTaskScene task, Slot ModelRootSlot)
    {
        // Section identification
        int sectiontype = -1;

        // Clear previous before we start
        storagebones.Clear();
        fileIDToRecycleName.Clear();
        externalObjects.Clear();
        GlobalScale = 1f;
        UnitScaleFactor = 100f;
        UseFileUnits = true;
        UseFileScale = true;
        CalculatedScaleFactor = 1f;

        // Bonereading section
        string boneName = string.Empty;
        string boneNameHuman = string.Empty;
        string externalObjects_name = string.Empty;

        foreach (string line in File.ReadLines(task.file + UnityPackageImporter.UNITY_META_EXTENSION))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("globalScale:") || trimmed.StartsWith("m_ScaleFactor:"))
            {
                string valStr = trimmed.Split(':')[1].Trim();
                if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gs))
                {
                    GlobalScale = gs;
                }
                continue;
            }
            if (trimmed.StartsWith("useFileUnits:") || trimmed.StartsWith("m_UseFileUnits:"))
            {
                string valStr = trimmed.Split(':')[1].Trim();
                UseFileUnits = valStr == "1" || valStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (trimmed.StartsWith("useFileScale:") || trimmed.StartsWith("m_UseFileScale:"))
            {
                string valStr = trimmed.Split(':')[1].Trim();
                UseFileScale = valStr == "1" || valStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("  fileIDToRecycleName:"))
            {
                sectiontype = 1;
                continue;
            }
            if (line.StartsWith("  internalIDToNameTable:"))
            {
                sectiontype = -1;
                continue;
            }
            if (line.StartsWith("  externalObjects:"))
            {
                sectiontype = 2;
                continue;
            }
            if (line.StartsWith("  humanDescription:"))
            {
                sectiontype = 0;
                continue;
            }
            // Any other top-level ModelImporter key (2-space indent, not a list item) ends the current section.
            if (line.StartsWith("  ") && !line.StartsWith("    ") && !line.StartsWith("  -"))
            {
                sectiontype = -1;
                continue;
            }

            // Per-bone scales are separate from the model-wide import scale.
            if (line.StartsWith("      scale:")) continue;
            switch (sectiontype)
            {
                case 0:
                    if (line.StartsWith("    - boneName:"))
                    {
                        boneName = line.Split(':', 2)[1].Trim();
                    }
                    else if (line.StartsWith("      humanName:"))
                    {
                        boneNameHuman = line.Split(':', 2)[1].Trim();
                    }

                    if (boneName.Length != 0 && boneNameHuman.Length != 0)
                    {
                        await default(ToWorld);
                        UnityPackageImporter.Msg("finding bone: " + boneName);
                        Slot boneSlot = ModelRootSlot.FindChild(boneName, false, false, -1);
                        BodyNode bodyNodeType = HumanoidNameToEnum(boneNameHuman);

                        // Only record valid humanoid bones that resolved to a non-NONE BodyNode and an existing Slot.
                        if (bodyNodeType != BodyNode.NONE && boneSlot != null && !storagebones.ContainsKey(bodyNodeType))
                        {
                            storagebones.Add(bodyNodeType, boneSlot);
                        }
                        await default(ToBackground);
                        boneName = string.Empty;
                        boneNameHuman = string.Empty;
                    }
                    break;
                case 1:
                    if (line.StartsWith("    ") && line.Contains(':'))
                    {
                        string[] parts = line.Split(':', 2);
                        if (long.TryParse(parts[0].Trim(), out long num) && num != 0)
                        {
                            fileIDToRecycleName[num] = parts[1].Trim();
                        }
                    }
                    break;
                case 2:
                    if (line.StartsWith("  - first:"))
                    {
                        externalObjects_name = string.Empty;
                    }
                    if (line.StartsWith("      name:"))
                    {
                        externalObjects_name = line.Split(':', 2)[1].Trim();
                    }
                    if (line.StartsWith("    second:") && line.Contains('{'))
                    {
                        string unparsed = '{' + line.Split('{', 2)[1].Trim();
                        SourceObj second = SharedDeserializer.Deserialize<SourceObj>(unparsed);

                        if (externalObjects_name != string.Empty)
                        {
                            externalObjects[externalObjects_name] = second;
                        }
                    }
                    break;
            }
        }
        if (!string.IsNullOrEmpty(task.file) && File.Exists(task.file))
        {
            UnitScaleFactor = ModelScaleHelper.GetFbxUnitScaleFactor(task.file);
        }
        CalculatedScaleFactor = ModelScaleHelper.CalculateEffectiveScale(GlobalScale, UseFileUnits, UseFileScale, UnitScaleFactor);
        GlobalScale = CalculatedScaleFactor;
        UnityPackageImporter.Msg($"Parsed model scale for '{Path.GetFileName(task.file)}': globalScale={GlobalScale}, useFileUnits={UseFileUnits}, useFileScale={UseFileScale}, unitScaleFactor={UnitScaleFactor} -> CalculatedScaleFactor={CalculatedScaleFactor}");
    }

    // Maps Unity HumanDescription humanName values to Resonite BodyNode enum values.
    public static BodyNode HumanoidNameToEnum(string boneNameHuman)
    {
        string normalized = SkinBoneMapping.NormalizeUnityHumanoidBoneName(boneNameHuman);
        if (string.IsNullOrEmpty(normalized))
            return BodyNode.NONE;

        if (Enum.TryParse(normalized, ignoreCase: true, out BodyNode bodyNode))
        {
            return bodyNode;
        }

        return BodyNode.NONE;
    }
}
