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

    // Parses the model's .meta YAML via ModelMetaParser and resolves humanoid bone slots in a single world-thread pass.
    public async Task ScanFile(FileImportTaskScene task, Slot ModelRootSlot)
    {
        await default(ToBackground);

        storagebones.Clear();
        fileIDToRecycleName.Clear();
        externalObjects.Clear();
        GlobalScale = 1f;
        UnitScaleFactor = 100f;
        UseFileUnits = true;
        UseFileScale = true;
        CalculatedScaleFactor = 1f;

        string metaPath = task.file + UnityPackageImporter.UNITY_META_EXTENSION;
        ParsedModelMeta parsed = ModelMetaParser.ParseFile(metaPath);

        GlobalScale = parsed.GlobalScale;
        UseFileUnits = parsed.UseFileUnits;
        UseFileScale = parsed.UseFileScale;

        foreach (var kvp in parsed.FileIdToRecycleName)
        {
            fileIDToRecycleName[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in parsed.ExternalObjects)
        {
            externalObjects[kvp.Key] = new SourceObj(kvp.Value.FileId, kvp.Value.Guid, kvp.Value.Type);
        }

        if (parsed.HumanBones.Count > 0 && ModelRootSlot != null)
        {
            await default(ToWorld);
            foreach (var (boneName, humanName) in parsed.HumanBones)
            {
                UnityPackageImporter.Msg("finding bone: " + boneName);
                Slot boneSlot = ModelRootSlot.FindChild(boneName, false, false, -1);
                BodyNode bodyNodeType = HumanoidNameToEnum(humanName);

                if (bodyNodeType != BodyNode.NONE && boneSlot != null && !storagebones.ContainsKey(bodyNodeType))
                {
                    storagebones.Add(bodyNodeType, boneSlot);
                }
            }
            await default(ToBackground);
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
