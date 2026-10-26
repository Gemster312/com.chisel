using Chisel.Core;
using UnityEngine;

namespace Chisel.Components
{
    /// <summary>
    /// Computes deterministic per-stage input hashes directly from model components. Unlike
    /// <see cref="CompactHierarchyManager.GetTreeInputHash"/>, these hashes do not require a constructed tree.
    /// </summary>
    public static class ChiselStagedInputHashes
    {
        // Bump when a stage hashes something else, or the same values differently
        const int kGeneratorInputHashVersion = 1;
        const int kNodeGraphHashVersion      = 1;
        const int kCSGInputHashVersion       = 1;
        const int kFinalizeInputHashVersion  = 2;

        // Separates otherwise identical values belonging to different node kinds.
        const int kGeneratorKind = 1;
        const int kCompositeKind = 2;
        const int kOtherNodeKind = 3;
        const int kDecalKind     = 4;
        const int kPlainKind     = 5;
        const int kNothing       = -1;

        /// <summary>
        /// Computes the stage-two hash from the generator definition, contents, pivot, surfaces, and material metadata.
        /// <paramref name="surfaceParameterIdentity"/> must provide an identity that remains stable across sessions.
        /// </summary>
        public static Hash128 GetGeneratorInputHash(ChiselGeneratorComponent generator, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var hash = new Hash128();
            if (!generator || surfaceParameterIdentity == null)
                return hash;

            var version    = kGeneratorInputHashVersion;
            var operation  = (int)generator.Operation;
            var contents   = generator.Contents;
            var pivot      = generator.PivotOffset;
            var definition = generator.GetDefinitionInputHash();
            hash.Append(ref version);
            hash.Append(ref operation);
            hash.Append(ref contents);
            hash.Append(ref pivot);
            hash.Append(ref definition);
            AppendSurfaces(ref hash, generator.SurfaceDefinition, surfaceParameterIdentity);
            return hash;
        }

        /// <summary>
        /// Computes the stage-one node graph hash from child order, node kinds, and local transforms.
        /// Generator definitions and the model transform are excluded.
        /// </summary>
        public static Hash128 GetNodeGraphHash(ChiselModelComponent model)
        {
            var hash = new Hash128();
            if (!model)
                return hash;
            var version = kNodeGraphHashVersion;
            hash.Append(ref version);
            AppendChildren(ref hash, model.transform, null, skipGenerated: false);
            return hash;
        }

        /// <summary>
        /// Computes the stage-three CSG hash from model settings, the node graph, generators, and decals.
        /// </summary>
        public static Hash128 GetCSGInputHash(ChiselModelComponent model, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var hash = new Hash128();
            if (!model || surfaceParameterIdentity == null)
                return hash;

            var version = kCSGInputHashVersion;
            hash.Append(ref version);
            AppendModelSettings(ref hash, model);
            AppendChildren(ref hash, model.transform, surfaceParameterIdentity, skipGenerated: false);
            return hash;
        }

#if UNITY_EDITOR
        /// <summary>
        /// The hash of everything a model's sculpture is made from (see <see cref="ChiselFinalize"/>): its generators
        /// like <see cref="GetCSGInputHash"/>, plus the model's own scale (its lightmap UVs follow it), layer, static
        /// flags and render/collider settings. Not its position or rotation: the meshes are in the model's own space, and
        /// the model follows its sculpture around (see ChiselModelFinalizer.FollowSculpture). Unlike the CSG hash it
        /// reads the serialized settings rather than the settings store, so it is the same for a model that is inactive
        /// or was never enabled this session, and it leaves out the generated objects and the code version, so neither a
        /// rebuild nor a recompile makes a model look changed.
        /// </summary>
        public static Hash128 GetFinalizeInputHash(ChiselModelComponent model, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var hash = new Hash128();
            if (!model || surfaceParameterIdentity == null)
                return hash;

            var version = kFinalizeInputHashVersion;
            hash.Append(ref version);

            var transform     = model.transform;
            var localScale    = transform.localScale;
            var layer         = model.gameObject.layer;
            var staticFlags   = (int)UnityEditor.GameObjectUtility.GetStaticEditorFlags(model.gameObject);
            hash.Append(ref localScale);
            hash.Append(ref layer);
            hash.Append(ref staticFlags);

            var createRenderComponents   = model.CreateRenderComponents ? 1 : 0;
            var createColliderComponents = model.CreateColliderComponents ? 1 : 0;
            var vertexChannelMask        = (int)model.VertexChannelMask;
            hash.Append(ref createRenderComponents);
            hash.Append(ref createColliderComponents);
            hash.Append(ref vertexChannelMask);
            AppendRenderSettings(ref hash, model.RenderSettings, surfaceParameterIdentity);
            AppendColliderSettings(ref hash, model.ColliderSettings);

            AppendChildren(ref hash, transform, surfaceParameterIdentity, skipGenerated: true);
            return hash;
        }

        // Everything but lightmapTexelsPerUnit / lightmapPaddingTexels, which follow the open scene's lighting settings,
        // and the probe anchor / proxy volume, which are scene objects without an identity that outlasts the session
        static void AppendRenderSettings(ref Hash128 hash, ChiselGeneratedRenderSettings settings, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            if (settings == null)
            {
                var missing = kNothing;
                hash.Append(ref missing);
                return;
            }
            var motionVectorGenerationMode     = (int)settings.motionVectorGenerationMode;
            var reflectionProbeUsage           = (int)settings.reflectionProbeUsage;
            var lightProbeUsage                = (int)settings.lightProbeUsage;
            var allowOcclusionWhenDynamic      = settings.allowOcclusionWhenDynamic ? 1 : 0;
            var renderingLayerMask             = settings.renderingLayerMask;
            var receiveGI                      = (int)settings.receiveGI;
            var castShadows                    = settings.castShadows ? 1 : 0;
            var receiveShadows                 = settings.receiveShadows ? 1 : 0;
            var subtractiveWorkflow            = settings.subtractiveWorkflow ? 1 : 0;
            var normalSmoothing                = settings.normalSmoothing ? 1 : 0;
            var normalSmoothingAngle           = settings.normalSmoothingAngle;
            var importantGI                    = settings.ImportantGI ? 1 : 0;
            var optimizeUVs                    = settings.OptimizeUVs ? 1 : 0;
            var ignoreNormalsForChartDetection = settings.IgnoreNormalsForChartDetection ? 1 : 0;
            var autoUVMaxDistance              = settings.AutoUVMaxDistance;
            var autoUVMaxAngle                 = settings.AutoUVMaxAngle;
            var minimumChartSize               = settings.MinimumChartSize;
            var stitchLightmapSeams            = settings.stitchLightmapSeams ? 1 : 0;
            var scaleInLightmap                = settings.scaleInLightmap;
            hash.Append(ref motionVectorGenerationMode);
            hash.Append(ref reflectionProbeUsage);
            hash.Append(ref lightProbeUsage);
            hash.Append(ref allowOcclusionWhenDynamic);
            hash.Append(ref renderingLayerMask);
            hash.Append(ref receiveGI);
            hash.Append(ref castShadows);
            hash.Append(ref receiveShadows);
            hash.Append(ref subtractiveWorkflow);
            hash.Append(ref normalSmoothing);
            hash.Append(ref normalSmoothingAngle);
            hash.Append(ref importantGI);
            hash.Append(ref optimizeUVs);
            hash.Append(ref ignoreNormalsForChartDetection);
            hash.Append(ref autoUVMaxDistance);
            hash.Append(ref autoUVMaxAngle);
            hash.Append(ref minimumChartSize);
            hash.Append(ref stitchLightmapSeams);
            hash.Append(ref scaleInLightmap);
            AppendAssetIdentity(ref hash, settings.LightmapParameters, surfaceParameterIdentity);

            var uvSettings    = settings.uvGenerationSettings;
            var hasUVSettings = (uvSettings != null) ? 1 : 0;
            hash.Append(ref hasUVSettings);
            if (uvSettings != null)
            {
                var angleError       = uvSettings.angleError;
                var areaError        = uvSettings.areaError;
                var hardAngle        = uvSettings.hardAngle;
                var packMarginPixels = uvSettings.packMarginPixels;
                hash.Append(ref angleError);
                hash.Append(ref areaError);
                hash.Append(ref hardAngle);
                hash.Append(ref packMarginPixels);
            }
        }

        static void AppendColliderSettings(ref Hash128 hash, ChiselGeneratedColliderSettings settings)
        {
            if (settings == null)
            {
                var missing = kNothing;
                hash.Append(ref missing);
                return;
            }
            var isTrigger      = settings.isTrigger ? 1 : 0;
            var convex         = settings.convex ? 1 : 0;
            var cookingOptions = (int)settings.cookingOptions;
            hash.Append(ref isTrigger);
            hash.Append(ref convex);
            hash.Append(ref cookingOptions);
        }
#endif

        static void AppendModelSettings(ref Hash128 hash, ChiselModelComponent model)
        {
            if (!ModelSettingsStore.TryGet(UnityEngine.EntityId.ToULong(model.GetEntityId()), out var settings))
            {
                var missing = kNothing;
                hash.Append(ref missing);
                return;
            }
            var subtractiveWorkflow   = settings.SubtractiveWorkflow ? 1 : 0;
            var normalSmoothing       = settings.NormalSmoothing ? 1 : 0;
            var normalSmoothingAngle  = settings.NormalSmoothingAngle;
            var lightmapTexelsPerUnit = settings.LightmapTexelsPerUnit;
            var lightmapPaddingTexels = settings.LightmapPaddingTexels;
            hash.Append(ref subtractiveWorkflow);
            hash.Append(ref normalSmoothing);
            hash.Append(ref normalSmoothingAngle);
            hash.Append(ref lightmapTexelsPerUnit);
            hash.Append(ref lightmapPaddingTexels);
        }

        static void AppendChildren(ref Hash128 hash, Transform parent, SurfaceParameterIdentity surfaceParameterIdentity, bool skipGenerated)
        {
            var childCount = parent.childCount;
            hash.Append(ref childCount);
            for (int c = 0; c < childCount; c++)
            {
                var child = parent.GetChild(c);
                if (skipGenerated && child.name == ChiselGeneratedObjects.kGeneratedContainerName)
                    continue;
                if (child.TryGetComponent<ChiselModelComponent>(out _))
                {
                    var nested = kNothing;
                    hash.Append(ref nested);
                    continue;
                }

                var active        = child.gameObject.activeSelf ? 1 : 0;
                var localPosition = child.localPosition;
                var localRotation = child.localRotation;
                var localScale    = child.localScale;
                hash.Append(ref active);
                hash.Append(ref localPosition);
                hash.Append(ref localRotation);
                hash.Append(ref localScale);

                if (child.TryGetComponent<ChiselNodeComponent>(out var node))
                {
                    var kind = (node is ChiselGeneratorComponent) ? kGeneratorKind :
                               (node is ChiselCompositeComponent) ? kCompositeKind : kOtherNodeKind;
                    hash.Append(ref kind);
                    if (node is IChiselHasOperation hasOperation)
                    {
                        var operation = (int)hasOperation.Operation;
                        hash.Append(ref operation);
                    }
                    if (surfaceParameterIdentity != null && node is ChiselGeneratorComponent generator)
                    {
                        var generatorHash = GetGeneratorInputHash(generator, surfaceParameterIdentity);
                        hash.Append(ref generatorHash);
                    }
                } else
                if (child.TryGetComponent<ChiselDecalComponent>(out var decal))
                {
                    var kind = kDecalKind;
                    hash.Append(ref kind);
                    if (surfaceParameterIdentity != null)
                        AppendDecal(ref hash, decal, surfaceParameterIdentity);
                } else
                {
                    var kind = kPlainKind;
                    hash.Append(ref kind);
                }

                AppendChildren(ref hash, child, surfaceParameterIdentity, skipGenerated);
            }
        }

        // Target surfaces are derived from geometry hashed by earlier stages.
        static void AppendDecal(ref Hash128 hash, ChiselDecalComponent decal, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var settings = decal.Settings;
            hash.Append(ref settings);
            AppendAssetIdentity(ref hash, decal.Material, surfaceParameterIdentity);
        }

        static void AppendSurfaces(ref Hash128 hash, ChiselSurfaceArray surfaceArray, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var surfaces = (surfaceArray != null) ? surfaceArray.surfaces : null;
            var count    = (surfaces != null) ? surfaces.Length : 0;
            hash.Append(ref count);
            for (int s = 0; s < count; s++)
            {
                var surface = surfaces[s];
                if (surface == null)
                {
                    var missing = kNothing;
                    hash.Append(ref missing);
                    continue;
                }
                var details = surface.surfaceDetails;
                hash.Append(ref details);
                // Use current material metadata; the serialized surface flags may be stale.
                var destinationFlags = (int)surface.DestinationFlags;
                var outputFlags      = (int)surface.OutputFlags;
                hash.Append(ref destinationFlags);
                hash.Append(ref outputFlags);
                AppendAssetIdentity(ref hash, surface.RenderMaterial, surfaceParameterIdentity);
                AppendAssetIdentity(ref hash, surface.PhysicsMaterial, surfaceParameterIdentity);
            }
        }

        static void AppendAssetIdentity(ref Hash128 hash, Object asset, SurfaceParameterIdentity surfaceParameterIdentity)
        {
            var identity = asset ? surfaceParameterIdentity(UnityEngine.EntityId.ToULong(asset.GetEntityId())) : default;
            hash.Append(ref identity);
        }
    }
}
