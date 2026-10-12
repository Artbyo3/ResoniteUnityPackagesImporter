using System;
using System.Collections.Generic;
using System.IO;
using UnityPackageImporter.FrooxEngineRepresentation;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnityPackageImporter.Models;

public class YamlToFrooxEngine
{
    public static Dictionary<ulong, IUnityObject> parseYaml(string FilePath)
    {
        // Begin the parsing of our prefabs.
        Dictionary<ulong, IUnityObject> existingIUnityObjects = new Dictionary<ulong, IUnityObject>();

        // Parse loop
        // Now using the power of yaml we can make this a bit more reliable and hopefully smaller.
        // Reading unity prefabs as yaml allows us to much more easily obtain the data we need.
        // Unity yamls are different, but with a little trickery we can still read them with a library.
        UnityNodeTypeResolver noderesolver = new UnityNodeTypeResolver();
        var deserializer = new DeserializerBuilder().WithNodeTypeResolver(noderesolver)
            .IgnoreUnmatchedProperties()
            //.WithNamingConvention(NullNamingConvention.Instance) // Outta here with that crappy conversion!!!! We got unity crap we deal with unity crap. - @989onan
            .Build();
        // Unity's optional "stripped" header suffix is not part of the YAML tag.
        // Normalize it in memory so the extracted package cache remains immutable.
        string yaml = AvatarPackageIndex.NormalizeDocumentHeaders(File.ReadAllText(FilePath));
        using var sr = new StringReader(yaml);
        var parser = new Parser(sr);
        parser.Consume<StreamStart>();
        DocumentStart variable;
        while (parser.Accept(out variable) == true)
        {
            ulong anchorId = noderesolver.anchor;
            try
            {
                UnityEngineObjectWrapper docWrapped = deserializer.Deserialize<UnityEngineObjectWrapper>(parser);
                IUnityObject doc = docWrapped.Result();
                doc.id = anchorId;
                existingIUnityObjects.TryAdd(doc.id, doc);
            }
            catch (Exception e)
            {
                UnityPackageImporter.Warn("Couldn't evaluate node type for Yaml \"" + FilePath + "\" at anchor " + anchorId + ": " + e.Message + ". Substituting NullType and continuing.");
                try
                {
                    IUnityObject doc = new FrooxEngineRepresentation.GameObjectTypes.NullType();
                    doc.id = anchorId;
                    existingIUnityObjects.TryAdd(doc.id, doc);
                }
                catch (ArgumentException e2)
                {
                    UnityPackageImporter.Msg("Duplicate key probably for Yaml \"" + FilePath + "\" just ignore this.");
                    UnityPackageImporter.Warn(e2.Message + e2.StackTrace);
                }

                // Advance parser past any unconsumed tokens in the failed document
                try
                {
                    while (!parser.Accept<DocumentEnd>(out _) && !parser.Accept<StreamEnd>(out _))
                    {
                        if (!parser.MoveNext()) break;
                    }
                    if (parser.Accept<DocumentEnd>(out _))
                    {
                        parser.Consume<DocumentEnd>();
                    }
                }
                catch (Exception drainEx)
                {
                    UnityPackageImporter.Warn("Could not advance parser after error in \"" + FilePath + "\": " + drainEx.Message);
                    break;
                }
            }
        }

        return existingIUnityObjects;
    }
}
