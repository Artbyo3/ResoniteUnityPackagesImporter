using UnityPackageImporter.Materials;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Assimp;
using Assimp.Configs;
using Elements.Core;
using FrooxEngine;
using HashDepot;
using SkyFrost.Base;
using UnityPackageImporter.FrooxEngineRepresentation;
using UnityPackageImporter.FrooxEngineRepresentation.GameObjectTypes;

namespace UnityPackageImporter.Models;

public class FileImportTaskScene
{
    public object data;
    public Dictionary<SourceObj, IUnityObject> FILEID_To_Slot_Pairs = new Dictionary<SourceObj, IUnityObject>(new SourceObjCompare());
    public string file;
    private UnityProjectImporter importer;
    public string assetID;
    public Slot importTaskAssetSlot;
    public MetaDataFile metafile;
    private ProgressBarInterface importDialogue;

    private Slot targetSlot;
    public Slot FinishedFileSlot = null;
    internal Slot ModelSceneRoot { get; private set; }
    private readonly object importLock = new();
    private Task importTask;
    private ReportModelPhase reportPhase = ReportModelPhase.Initialize;
    private PostProcessSteps reportProcessing;
    private readonly ModelImportFailureState nativeImportFailures = new();
    private float3 globalPosition;
    private Dictionary<string, string[]> sourceBlendShapeNames = new(StringComparer.Ordinal);
    private readonly UnityMeshData serializedMesh;

    private void RecordBlendShapeNames(Assimp.Scene scene, Assimp.Node node)
    {
        if (node.MeshCount > 0)
        {
            var names = scene.Meshes[node.MeshIndices[0]].MeshAnimationAttachments.Select(shape => shape.Name).ToArray();
            bool consistent = node.MeshIndices.All(index => scene.Meshes[index].MeshAnimationAttachments.Select(shape => shape.Name).SequenceEqual(names));
            if (!consistent || sourceBlendShapeNames.ContainsKey(node.Name))
                sourceBlendShapeNames[node.Name] = null;
            else sourceBlendShapeNames[node.Name] = names;
        }
        foreach (var child in node.Children) RecordBlendShapeNames(scene, child);
    }

    public FileImportTaskScene(Slot targetSlot, string assetID, UnityProjectImporter importer, string file, float3 globalPosition)
    {
        this.targetSlot = targetSlot.AddSlot(Path.GetFileNameWithoutExtension(file)+ " - Temp.fbx");

        this.file = file;
        this.importer = importer;
        this.importTaskAssetSlot = importer.importTaskAssetRoot.AddSlot("Assets - "+ Path.GetFileNameWithoutExtension(file) + ".fbx");

        this.assetID = assetID;
        this.globalPosition = globalPosition;
    }

    private FileImportTaskScene(Slot targetSlot, UnityProjectImporter importer)
    {
        this.targetSlot = targetSlot;
        this.importer = importer;
    }

    internal FileImportTaskScene(Slot targetSlot, string assetID, UnityProjectImporter importer, string file, UnityMeshData mesh)
        : this(targetSlot, assetID, importer, file, float3.Zero)
    {
        serializedMesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
    }



    public Task RunnerWrapper()
    {
        lock (importLock)
        {
            if (importTask == null)
            {
                importTask = ImportObservedAsync();
            }
            return importTask;
        }
    }

    private async Task ImportObservedAsync()
    {
        try { await ImportFileMeshes(); }
        catch (Exception error) when (error is not OperationCanceledException || importer.Session?.Token.IsCancellationRequested != true)
        {
            ImportReportService.ModelFailed(importer.Session?.Reports, assetID, file, error, reportProcessing.ToString(),
                nativeImportFailures.MaterialFailure == null ? reportPhase : ReportModelPhase.Materials);
            throw;
        }
    }

    private async Task ImportFileMeshes()
    {
        UnityPackageImporter.Msg("Start code block for file import for file " + file);

        await default(ToWorld);
        importer.Session?.Check();
        if (importer.Station == null)
        {
            Slot indicator = importer.world.AddSlot("FBX Import Indicator", false);
            indicator.GlobalPosition = globalPosition;
            importDialogue = await indicator.SpawnEntity<ProgressBarInterface, LegacySegmentCircleProgress>(FavoriteEntity.ProgressBar);
        }
        await default(ToBackground);

        await default(ToWorld);
        AssimpContext assimpContext = new AssimpContext();
        assimpContext.Scale = 1f;
        assimpContext.SetConfig(new NormalSmoothingAngleConfig(66f));
        assimpContext.SetConfig(new TangentSmoothingAngleConfig(10f));
        PostProcessSteps postProcessSteps = PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.ImproveCacheLocality | PostProcessSteps.PopulateArmatureData | PostProcessSteps.GenerateUVCoords | PostProcessSteps.FindInstances | PostProcessSteps.FlipWindingOrder | PostProcessSteps.LimitBoneWeights;
        reportProcessing = serializedMesh == null ? postProcessSteps : PostProcessSteps.None;
        Assimp.Scene scene = null;
        await default(ToBackground);

        metafile = new MetaDataFile();

        importDialogue?.UpdateProgress(0f,"","Start assimp file import for file \"" + Path.GetFileName(file)+ "\" If your import stops here, then Assimp crashed like a drunk man and took the game with it.\"");

        await default(ToBackground);
        try
        {
            reportPhase = ReportModelPhase.Decode;
            scene = serializedMesh == null ? assimpContext.ImportFile(this.file, postProcessSteps) : UnityMeshSourceImporter.CreateScene(serializedMesh);
        }
        catch (Exception arg)
        {
            UnityPackageImporter.Error(string.Format("Exception when importing {0}:\n\n{1}", this.file, arg), false);
            await default(ToWorld);
            importDialogue?.ProgressFail("Failed to read model: " + Path.GetFileName(file));
            throw;
        }
        finally
        {
            assimpContext.Dispose();
        }

        importer.Session?.Check();
        this.importDialogue?.UpdateProgress(0f, "", "Preprocessing scene for file " + Path.GetFileName(file));
        if (scene?.RootNode == null)
            throw new InvalidDataException("Model has no scene root: " + file);
        reportPhase = ReportModelPhase.Metadata;
        var modelMetadata = ModelMetaParser.ParseFile(file + UnityPackageImporter.UNITY_META_EXTENSION);
        float unitConversion = serializedMesh != null ? 1f : ModelScaleHelper.CalculateEffectiveScale(modelMetadata.GlobalScale,
            modelMetadata.UseFileUnits, modelMetadata.UseFileScale, ModelScaleHelper.GetFbxUnitScaleFactor(file));
        ModelUnitConversion.Apply(scene, unitConversion);
        UnityPackageImporter.Msg($"Converted model coordinate units ({unitConversion}) for '{Path.GetFileName(file)}', including vertices, bind poses and translations.");
        reportPhase = ReportModelPhase.Preprocess;
        FrooxInternalBridge.PreprocessScene(scene);
        RecordBlendShapeNames(scene, scene.RootNode);
        UnityPackageImporter.Msg("making model import data for file: " + file);

        var settings = ModelImportSettings.PBS(true, true, false, false, false, false);
        var nativeMaterialMapper = settings.MaterialMapper;
        settings.MaterialMapper = (slot, material, textures) => nativeImportFailures.MapMaterial(() => nativeMaterialMapper(slot, material, textures));
        this.data = FrooxInternalBridge.CreateModelImportData(file, scene, this.targetSlot, this.importTaskAssetSlot, settings, null);
        this.importDialogue?.UpdateProgress(0f, "", "importing nodes into froox engine, file: " + Path.GetFileName(file));

        await default(ToWorld);
        importer.Session?.Check();
        reportPhase = ReportModelPhase.Nodes;
        await ImportNodeAsync(scene.RootNode, targetSlot, data);
        importer.Session?.Check();

        await default(ToWorld);
        importer.Session?.Check();

        UnityPackageImporter.Msg("retrieving scene root for file: " + Path.GetFileName(file));

        Slot sceneRootSlot = FrooxInternalBridge.TryGetSlot(data, scene.RootNode);
        ModelSceneRoot = sceneRootSlot;
        await this.metafile.ScanFile(this, sceneRootSlot);



        reportPhase = ReportModelPhase.Bindings;
        await InitializeSkinsAsync();
        await default(ToWorld);
        importer.Session?.Check();
        sceneRootSlot.Tag = "PREFABROOTTAG1234";
        await default(ToBackground);

        this.FinishedFileSlot = this.targetSlot;
        reportPhase = ReportModelPhase.Identifiers;

        this.importDialogue?.UpdateProgress(0f, "", "Hashing slot paths with XXHash64 for file: " + Path.GetFileName(file));

        foreach (Slot childofroot in sceneRootSlot.Children)
        {
            this.FILEID_To_Slot_Pairs.AddRange(RecusiveFileIDSlotFinder(childofroot, sceneRootSlot, this.assetID));
        }



        this.importDialogue?.UpdateProgress(0f, "", "Hashing skinned mesh renderers with XXHash64 for file: " + Path.GetFileName(file));


        foreach (FrooxEngine.SkinnedMeshRenderer mesh in this.FinishedFileSlot.GetAllChildren().FindAll(slot => slot.GetComponent<FrooxEngine.SkinnedMeshRenderer>() != null).Select(slot => slot.GetComponent<FrooxEngine.SkinnedMeshRenderer>()))
        {
            UnityPackageImporter.Msg("scanning files for ids for mesh renderer "+mesh.Slot.Name);
            FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer skinnedrenderer = new FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer();
            SourceObj identifier = this.FindSource(mesh.Slot.Name, UnityModelClassId.SkinnedMeshRenderer, "//RootNode/root/" + mesh.Slot.Name);
            identifier.guid = this.assetID;
            skinnedrenderer.m_CorrespondingSourceObject = identifier;
            skinnedrenderer.createdMeshRenderer = mesh;
            sourceBlendShapeNames.TryGetValue(mesh.Slot.Name, out skinnedrenderer.SourceBlendShapeNames);
            skinnedrenderer.m_Mesh = this.FindSource(mesh.Slot.Name, UnityModelClassId.Mesh, mesh.Slot.Name);
            skinnedrenderer.m_Mesh.guid = this.assetID;
            UnityPackageImporter.Msg("id for mesh \""+mesh.Slot.Name+"\" is \""+ skinnedrenderer.m_Mesh.ToString() + "\"files for ids for mesh renderer " + mesh.Slot.Name);
            UnityPackageImporter.Msg("id for skinned mesh renderer \"" + mesh.Slot.Name + "\" is \"" + identifier.ToString()+"\"");
            this.FILEID_To_Slot_Pairs.Add(identifier, skinnedrenderer);
        }

        this.importDialogue?.ProgressDone("Finished task for file " + Path.GetFileName(file));
    }



    public async Task<FileImportTaskScene> MakeCopyAndPopulatePrefabData()
    {
        FileImportTaskScene copy = new FileImportTaskScene(targetSlot, importer);
        copy.file = this.file;
        await default(ToWorld);
        UnityPackageImporter.Msg("target slot instanciated?: "+(copy.targetSlot != null));
        importer.Session?.Check();
        copy.targetSlot = copy.targetSlot.Duplicate(importer.root, false, null);
        copy.FinishedFileSlot = copy.targetSlot;
        copy.metafile = new MetaDataFile();
        copy.FILEID_To_Slot_Pairs.Clear();
        copy.assetID = this.assetID;
        copy.sourceBlendShapeNames = this.sourceBlendShapeNames;


        Slot Sceneroot = copy.targetSlot.GetChildrenWithTag("PREFABROOTTAG1234").First();
        copy.ModelSceneRoot = Sceneroot;
        Sceneroot.Tag = null;
        await default(ToBackground);

        UnityPackageImporter.Msg("scanning meta file for data");
        UnityPackageImporter.Msg("file instanciated?: " + (copy.file != null));
        UnityPackageImporter.Msg("targetSlot instanciated?: " + (copy.targetSlot != null));
        UnityPackageImporter.Msg("FinishedFileSlot instanciated?: " + (copy.FinishedFileSlot != null));
        await copy.metafile.ScanFile(copy, copy.targetSlot);
        UnityPackageImporter.Msg("recurisively searching for slots in file: " + file);

        foreach (Slot childofroot in Sceneroot.Children)
        {
            copy.FILEID_To_Slot_Pairs.AddRange(RecusiveFileIDSlotFinder(childofroot, Sceneroot, copy.assetID));
        }


        foreach (FrooxEngine.SkinnedMeshRenderer mesh in copy.FinishedFileSlot.GetAllChildren().FindAll(slot => slot.GetComponent<FrooxEngine.SkinnedMeshRenderer>() != null).Select(slot => slot.GetComponent<FrooxEngine.SkinnedMeshRenderer>()))
        {
            UnityPackageImporter.Msg("giving a skinned mesh renderer materials for file: " + file);
            FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer skinnedrenderer = new FrooxEngineRepresentation.GameObjectTypes.SkinnedMeshRenderer();
            SourceObj identifier = copy.FindSource(mesh.Slot.Name, UnityModelClassId.SkinnedMeshRenderer, "//RootNode/root/" + mesh.Slot.Name);
            skinnedrenderer.m_CorrespondingSourceObject = identifier;
            identifier.guid = copy.assetID;
            UnityPackageImporter.Msg("path for mesh \""+ mesh.Slot.Name + "\"" + identifier.ToString());

            skinnedrenderer.m_Mesh = copy.FindSource(mesh.Slot.Name, UnityModelClassId.Mesh, mesh.Slot.Name);

            skinnedrenderer.createdMeshRenderer = mesh;
            copy.sourceBlendShapeNames.TryGetValue(mesh.Slot.Name, out skinnedrenderer.SourceBlendShapeNames);

            await default(ToWorld);
            await ImportMeshReadiness.WaitAsync(new[] { mesh }, importer.Session?.Token ?? default);
            await default(ToWorld);




            ValidateSkin(mesh, copy.FinishedFileSlot);
              FrooxInternalBridge.SetupBlendShapes(skinnedrenderer.createdMeshRenderer);


              skinnedrenderer.m_BlendShapeWeights = new List<float>();
              if (skinnedrenderer.SourceBlendShapeNames != null)
                  foreach (var name in skinnedrenderer.SourceBlendShapeNames)
                  {
                      int index = mesh.BlendShapeIndex(name);
                      skinnedrenderer.m_BlendShapeWeights.Add(index >= 0 ? mesh.BlendShapeWeights[index] * 100f : 0f);
                  }


            List<string> materialnames = new List<string>();

            for (int i = 0; i < skinnedrenderer.createdMeshRenderer.Materials.Count; i++)
            {
                materialnames.Add(skinnedrenderer.createdMeshRenderer.Materials[i].FindNearestParent<Slot>().Name.Replace("Material: ", "").Trim());
            }

            for (int i = 0; i < skinnedrenderer.createdMeshRenderer.Mesh.Asset.Data.SubmeshCount; i++)
            {
                FileImportHelperTaskMaterial materialtask;
                SourceObj recordedSource = new SourceObj(0, string.Empty, 0);
                string matName = i < materialnames.Count ? materialnames[i] : $"Submesh_{i}";
                try
                {
                    if (copy.metafile.externalObjects != null &&
                        i < materialnames.Count &&
                        copy.metafile.externalObjects.TryGetValue(matName, out SourceObj material) &&
                        !string.IsNullOrEmpty(material.guid))
                    {
                        recordedSource = material;
                        if (copy.importer.AssetIDDict.TryGetValue(material.guid, out string matPath))
                        {
                            await default(ToWorld);
                            materialtask = new FileImportHelperTaskMaterial(material.guid, matPath, copy.importer);
                            await default(ToBackground);
                        }
                        else
                        {
                            if (!MaterialDependencyDiagnostics.IsBuiltinOrEmptyGuid(material.guid))
                            {
                                copy.importer.RegisterMissingMaterial(mesh, i, material.guid);
                            }
                            await default(ToWorld);
                            materialtask = new FileImportHelperTaskMaterial(copy.importer);
                            await default(ToBackground);
                        }
                    }
                    else
                    {
                        await default(ToWorld);
                        materialtask = new FileImportHelperTaskMaterial(copy.importer);
                        await default(ToBackground);
                    }
                    await default(ToWorld);
                    skinnedrenderer.materials.Add(materialtask);
                    skinnedrenderer.m_Materials.Add(recordedSource);
                    await default(ToBackground);
                }
                catch (Exception)
                {
                    UnityPackageImporter.Msg("The material " + matName + " importing encountered an error!");
                    skinnedrenderer.m_Materials.Add(recordedSource);
                    throw;
                }
                await default(ToWorld);
            }

            if (!copy.FILEID_To_Slot_Pairs.ContainsKey(identifier))
            {
                copy.FILEID_To_Slot_Pairs.Add(identifier, skinnedrenderer);
            }

            UnityPackageImporter.Msg("clearing bad material objects for initial import for: \"" + skinnedrenderer.createdMeshRenderer.Slot.Name + "\"");
            await default(ToWorld);
            skinnedrenderer.createdMeshRenderer.Materials.Clear();
            await default(ToBackground);
            int counter = 0;
            UnityPackageImporter.Msg("getting good material objects for initial import for: \"" + skinnedrenderer.createdMeshRenderer.Slot.Name + "\" it has \"" + skinnedrenderer.materials.Count.ToString() + " \" materials in the generated list to instanciate");
            foreach (FileImportHelperTaskMaterial materialtask in skinnedrenderer.materials)
            {
                await MaterialSlotAssignment.AssignAsync<IAssetProvider<FrooxEngine.Material>>(counter,
                    () => materialtask.runImportFileMaterialsAsync(),
                    async error =>
                    {
                        UnityPackageImporter.Warn($"Initial material conversion failed at slot {counter} on '{skinnedrenderer.createdMeshRenderer.Slot.Name}': {error.Message}");
                        await default(ToWorld);
                        copy.importer.Session?.Check();
                        return new FileImportHelperTaskMaterial(copy.importer).finalMaterial;
                    },
                    async (index, material) =>
                    {
                        await default(ToWorld);
                        copy.importer.Session?.Check();
                        while (skinnedrenderer.createdMeshRenderer.Materials.Count <= index) skinnedrenderer.createdMeshRenderer.Materials.Add();
                        skinnedrenderer.createdMeshRenderer.Materials[index] = material;
                        await default(ToBackground);
                    }, copy.importer.Session?.Token ?? default);
                counter++;
            }
        }

        return copy;
    }

    private static bool IsInside(Slot slot, Slot root)
    {
        for (var current = slot; current != null; current = current.Parent)
            if (current == root) return true;
        return false;
    }

    private async Task InitializeSkinsAsync()
    {
        await default(ToWorld);
        importer.Session?.Check();
        var skins = targetSlot.GetComponentsInChildren<FrooxEngine.SkinnedMeshRenderer>().ToArray();
        await ImportMeshReadiness.WaitAsync(skins, importer.Session?.Token ?? default);
        await default(ToWorld);
        importer.Session?.Check();
        foreach (var skin in skins)
        {
            if (skin.Mesh.Target is not StaticMesh staticMesh)
                throw new InvalidDataException("Native model import did not produce a static mesh provider.");
            var mapping = SkinBoneMapping.ResolveNative(skin.Mesh.Asset.Data.Bones.Select(bone => bone.Name),
                FrooxInternalBridge.TryGetBoneMapping(data, staticMesh), () => BoneCandidates(targetSlot));
            skin.SetupBones(mapping);
            ValidateSkin(skin, targetSlot);
            FrooxInternalBridge.SetupBlendShapes(skin);




            SkinnedBoundsPolicy.ApplyAfterBones(skin, "native FBX template finalization");
        }
        UnityPackageImporter.Msg($"Initialized native bone bindings for {skins.Length} imported skin(s).");
    }

    private static IEnumerable<(string Name, Slot Bone)> BoneCandidates(Slot root)
    {


        if (root.GetComponent<FrooxEngine.MeshRenderer>() != null) yield break;
        yield return (root.Name, root);
        foreach (var child in root.Children)
            foreach (var candidate in BoneCandidates(child)) yield return candidate;
    }

    private static void ValidateSkin(FrooxEngine.SkinnedMeshRenderer skin, Slot root)
    {
        try
        {
            SkinBoneMapping.Validate(skin.Mesh.Asset.Data.BoneCount, skin.Bones,
                bone => !bone.IsDestroyed && IsInside(bone, root));
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException("Invalid bone bindings for '" + skin.Slot.Name + "': " + error.Message, error);
        }
    }


    private Task ImportNodeAsync(Node node, Slot targetSlot, object data)
    {


        var world = importer.world;
        var cancellationToken = importer.Session?.Token ?? CancellationToken.None;
        cancellationToken.ThrowIfCancellationRequested();
        if (targetSlot.IsDestroyed || world.IsDisposed || world.RootSlot.IsDestroyed)
            throw new OperationCanceledException("Model import target is no longer available.");

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Coroutine coroutine = default;
        int targetRemoved = 0;

        void Unsubscribe()
        {
            ((IDestroyable)targetSlot).Destroyed -= OnTargetDestroyed;
            world.WorldDestroyed -= OnWorldDestroyed;
        }

        void Finished(Coroutine _)
        {
            Unsubscribe();
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref targetRemoved) != 0)
                completion.TrySetCanceled(cancellationToken);
            else
                completion.TrySetException(nativeImportFailures.Interrupted());
        }

        void Removed()
        {
            Interlocked.Exchange(ref targetRemoved, 1);
            if (!coroutine.IsNull) coroutine.Stop();

            Finished(coroutine);
        }

        void OnTargetDestroyed(IDestroyable _) => Removed();
        void OnWorldDestroyed(World _) => Removed();

        ((IDestroyable)targetSlot).Destroyed += OnTargetDestroyed;
        world.WorldDestroyed += OnWorldDestroyed;
        try
        {
            coroutine = world.Coroutines.StartCoroutine(
                ImportNodeWrapper(node, targetSlot, data, completion, cancellationToken,
                    () => Volatile.Read(ref targetRemoved) != 0, nativeImportFailures), Finished);
        }
        catch (Exception error)
        {
            Unsubscribe();
            completion.TrySetException(error);
        }
        return completion.Task;
    }

    private static IEnumerator<Context> ImportNodeWrapper(Node node, Slot targetSlot, object data,
        TaskCompletionSource<bool> completion, CancellationToken cancellationToken, Func<bool> targetRemoved,
        ModelImportFailureState nativeImportFailures)
    {
        try
        {
            IEnumerator<Context> steps = null;
            try { steps = FrooxInternalBridge.ImportNode(node, targetSlot, data); }
            catch (Exception error) { completion.TrySetException(error); }
            if (steps == null)
            {
                completion.TrySetException(new InvalidOperationException("Engine model import coroutine is unavailable."));
                yield break;
            }
            using (steps)
            {
                while (true)
                {
                    bool hasNext = false;
                    Context current = default;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();


                        if (targetRemoved())
                            throw new OperationCanceledException("Model import target was removed.");
                        hasNext = steps.MoveNext();
                        if (hasNext) current = steps.Current;
                    }
                    catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
                    catch (Exception error) { completion.TrySetException(error); }
                    if (completion.Task.IsCompleted) yield break;
                    if (!hasNext) break;
                    yield return current;
                }
            }
            completion.TrySetResult(true);
        }
        finally
        {
            completion.TrySetException(nativeImportFailures.Interrupted());
        }
    }

    internal void DestroyTemplate() => targetSlot?.Destroy();

    public Dictionary<SourceObj, IUnityObject> RecusiveFileIDSlotFinder(Slot curnode, Slot Scene, string assetID)
    {
        Dictionary<SourceObj, IUnityObject> FILEID_into_Slot_Pairs = new Dictionary<SourceObj, IUnityObject>(new SourceObjCompare());

        FrooxEngineRepresentation.GameObjectTypes.GameObject calclatedobj = new FrooxEngineRepresentation.GameObjectTypes.GameObject();
        calclatedobj.frooxEngineSlot = curnode;
        calclatedobj.m_CorrespondingSourceObject = FindSource(curnode.Name, UnityModelClassId.GameObject, "//RootNode/root" + FindSlotPath(curnode, Scene));
        calclatedobj.m_Name = curnode.Name;
        calclatedobj.m_IsActive = curnode.ActiveSelf ? 1 : 0;
        if (!FILEID_into_Slot_Pairs.ContainsKey(calclatedobj.m_CorrespondingSourceObject))
        {
            FILEID_into_Slot_Pairs.Add(calclatedobj.m_CorrespondingSourceObject, calclatedobj);
        }

        FrooxEngineRepresentation.GameObjectTypes.Transform calclatedTransformobj = new FrooxEngineRepresentation.GameObjectTypes.Transform();
        calclatedTransformobj.parentHashedGameObj = calclatedobj;
        calclatedTransformobj.m_CorrespondingSourceObject = FindSource(curnode.Name, UnityModelClassId.Transform, "//RootNode/root" + FindSlotPath(curnode, Scene));


        calclatedTransformobj.m_LocalPosition = new TransformFloat3(calclatedobj.frooxEngineSlot.LocalPosition.x, calclatedobj.frooxEngineSlot.LocalPosition.y, calclatedobj.frooxEngineSlot.LocalPosition.z);
        calclatedTransformobj.m_LocalScale = new TransformFloat3(calclatedobj.frooxEngineSlot.LocalScale.x, calclatedobj.frooxEngineSlot.LocalScale.y, calclatedobj.frooxEngineSlot.LocalScale.z);
        calclatedTransformobj.m_LocalRotation = new TransformFloat4(calclatedobj.frooxEngineSlot.LocalRotation.x, calclatedobj.frooxEngineSlot.LocalRotation.y, calclatedobj.frooxEngineSlot.LocalRotation.z, calclatedobj.frooxEngineSlot.LocalRotation.w);


        if (!FILEID_into_Slot_Pairs.ContainsKey(calclatedTransformobj.m_CorrespondingSourceObject))
        {
            FILEID_into_Slot_Pairs.Add(calclatedTransformobj.m_CorrespondingSourceObject, calclatedTransformobj);
        }
        if (curnode.ChildrenCount > 0)
        {
            foreach(Slot child in curnode.Children)
            {
                FILEID_into_Slot_Pairs.AddRange(RecusiveFileIDSlotFinder(child, Scene, assetID));
            }
        }

        return FILEID_into_Slot_Pairs;
    }

    internal IEnumerable<(long FileId, int ClassId)> StaticRendererIdentities() =>
        FinishedFileSlot.GetComponentsInChildren<FrooxEngine.MeshRenderer>()
            .Where(renderer => MeshReferenceLookup.IsExactStaticRenderer(renderer.GetType(), typeof(FrooxEngine.MeshRenderer))).Select(renderer =>
            (FindSource(renderer.Slot.Name, UnityModelClassId.MeshRenderer, "//RootNode/root/" + renderer.Slot.Name).fileID, 23));

    internal FrooxEngine.MeshRenderer ResolveStaticRendererTemplate(long rendererFileId) =>
        MeshReferenceLookup.ResolveUnique(rendererFileId,
            FinishedFileSlot.GetComponentsInChildren<FrooxEngine.MeshRenderer>()
                .Where(renderer => MeshReferenceLookup.IsExactStaticRenderer(renderer.GetType(), typeof(FrooxEngine.MeshRenderer))).Select(renderer =>
                (FindSource(renderer.Slot.Name, UnityModelClassId.MeshRenderer, "//RootNode/root/" + renderer.Slot.Name).fileID, renderer)));

    internal FrooxEngine.MeshRenderer ResolveStaticMeshTemplate(long meshFileId)
    {
        return MeshReferenceLookup.ResolveUnique(meshFileId,
            FinishedFileSlot.GetComponentsInChildren<FrooxEngine.MeshRenderer>()
                .Where(renderer => MeshReferenceLookup.IsExactStaticRenderer(renderer.GetType(), typeof(FrooxEngine.MeshRenderer)))
                .Select(renderer => (FindSource(renderer.Slot.Name, UnityModelClassId.Mesh, renderer.Slot.Name).fileID, renderer)));
    }

    private SourceObj FindSource(string thisname, UnityModelClassId classId, string rawpath)
    {
        string type = classId.ToString();
        SourceObj sourceObj = new SourceObj();
        string endingtype = "";
        if (!type.Equals("GameObject", StringComparison.Ordinal) && !type.Equals("Mesh", StringComparison.Ordinal))
        {
            endingtype = "/"+type;
        }
        string path = "Type:" + type + "->" + rawpath + endingtype + "0";
        long calculated_gameobj_id = (long)XXHash.Hash64(Encoding.UTF8.GetBytes(path));
        sourceObj.fileID = calculated_gameobj_id;
        sourceObj.guid = this.assetID;
        sourceObj.type = 0;

        string numident = ((int)classId).ToString();
        foreach (KeyValuePair<long, string> item in this.metafile.fileIDToRecycleName)
        {
            string nummatch = item.Key.ToString();


            if (thisname == item.Value && nummatch.Length > 5 && nummatch.Remove(nummatch.Length - 5).Equals(numident, StringComparison.Ordinal))
            {
                sourceObj.fileID = item.Key;
            }
        }
        return sourceObj;
    }
    public static string FindSlotPath(Slot child, Slot StopAt)
    {
        if (child == null || StopAt == null)
        {
            return string.Empty;
        }

        if (child.IsChildOf(StopAt))
        {
            return FindSlotPathRecursive(child, StopAt, "/" + child.Name);
        }
        else
        {
            return "/" + child.Name;
        }
    }

    private static string FindSlotPathRecursive(Slot child, Slot StopAt, string curpath)
    {
        if (child.Parent == StopAt)
        {
            return curpath;
        }

        var path = "/" + child.Parent.Name + curpath;
        return FindSlotPathRecursive(child.Parent, StopAt, path);
    }
}
