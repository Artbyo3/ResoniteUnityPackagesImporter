using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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




    public async Task GenerateComponents(BipedRig rig)
    {
        UnityPackageImporter.Msg("Humanoid bone description being instanciated is: " + (rig != null));
        await default(ToBackground);



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
            var owned = task.FILEID_To_Slot_Pairs.Values
                .OfType<FrooxEngineRepresentation.GameObjectTypes.GameObject>()
                .Select(gameObject => gameObject.frooxEngineSlot).Where(slot => slot != null && !slot.IsDestroyed).Distinct().ToArray();
            var modelObjects = owned.Length > 0 ? owned : ModelRootSlot.GetAllChildren().ToArray();
            foreach (var (boneName, humanName) in parsed.HumanBones)
            {
                UnityPackageImporter.Msg("finding bone: " + boneName);
                Slot boneSlot = ModelBoneIndex<Slot>.FindOwned(modelObjects, boneName, slot => slot.Name);
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
