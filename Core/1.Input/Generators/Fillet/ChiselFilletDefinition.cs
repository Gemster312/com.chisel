using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;

namespace Chisel.Core
{
    // Fork: a fillet, the inverse of a rounded edge: a square block (seen end on, x across, y up, running along z) with a
    // quarter-round bite out of it, leaving the solid a rounded edge would lose. The origin is at its sharp corner, the
    // solid in +x, +y. Subtracted at a sharp edge it rounds the edge; added in an inside corner it makes a cove. Each side
    // of the curve is its own brush
    [Serializable]
    public struct ChiselFillet : IBranchGenerator
    {
        readonly static ChiselFillet kDefaultSettings = new()
        {
            radius  = 0.5f,
            length  = 1,
            sides   = 8
        };
        public static ref readonly ChiselFillet DefaultSettings => ref kDefaultSettings;

        [Tooltip("The radius of the curve, which is also the block's size across.")]
        [DistanceValue] public float radius;
        [Tooltip("How long the fillet is, along its edge.")]
        [DistanceValue] public float length;
        [Tooltip("How many flat faces make up the curve. About 8-12 keep a rolling ball smooth. Minimum: 1.")]
        public int sides;

        #region Generate
        public int PrepareAndCountRequiredBrushMeshes() { return sides; }

        public bool GenerateNodes(BlobAssetReference<InternalChiselSurfaceArray> surfaceDefinitionBlob,
                                  NativeList<GeneratedNode> nodes, Allocator allocator = Allocator.Persistent)// Indirect
        {
            ref var surfaceArray = ref surfaceDefinitionBlob.Value;
            using var brushes = new NativeList<BlobAssetReference<BrushMeshBlob>>(nodes.Length, Allocator.Temp);
            var piece = new BrushMeshFactory.ProfilePiece(Allocator.Temp);
            try
            {
                // The curve's centre is the block's far corner; from the -x side round to the -y side
                var centre = new float2(radius, radius);
                var succeeded = true;
                for (int s = 0; s < sides && succeeded; s++)
                {
                    var angle0 = math.PI + (s / (float)sides) * math.PI * 0.5f;
                    var angle1 = math.PI + ((s + 1) / (float)sides) * math.PI * 0.5f;
                    BrushMeshFactory.SetBendPiece(piece, centre, radius, angle0, angle1, float2.zero, (int)SurfaceSides.Curve, (int)SurfaceSides.Flat);
                    succeeded = BrushMeshFactory.AddProfileBrush(brushes, in piece, length, (int)SurfaceSides.Back, (int)SurfaceSides.Front, ref surfaceArray, allocator);
                }
                return BrushMeshFactory.FinishProfileBrushes(brushes, nodes, succeeded);
            }
            finally
            {
                piece.Dispose();
            }
        }

        public void Dispose() { }
        #endregion

        #region Surfaces
        public enum SurfaceSides : byte
        {
            Back    = 0,
            Front   = 1,
            Curve   = 2,
            Flat    = 3,

            TotalSides
        }

        [BurstDiscard]
        public int RequiredSurfaceCount { get { return (int)SurfaceSides.TotalSides; } }

        [BurstDiscard]
        public void UpdateSurfaces(ref ChiselSurfaceArray surfaceDefinition)
        {
            var surfaces = surfaceDefinition.surfaces;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (!surfaces[i].HasMaterial)
                    surfaces[i].SetMaterial(ChiselProjectSettings.DefaultWallMaterial);
            }
        }
        #endregion

        #region Validation
        public const float kMinSize = 0.01f;
        public const int   kMaxSides = 64;

        public bool Validate()
        {
            radius  = math.max(kMinSize, math.abs(radius));
            length  = math.max(kMinSize, math.abs(length));
            sides   = math.clamp(sides, 1, kMaxSides);
            return true;
        }

        [BurstDiscard]
        public void GetWarningMessages(IChiselMessageHandler messages) { }
        #endregion

        #region Reset
        public void Reset() { this = DefaultSettings; }
        #endregion
    }

    [Serializable]
    public class ChiselFilletDefinition : SerializedBranchGenerator<ChiselFillet>
    {
        public const string kNodeTypeName = "Fillet";

        #region OnEdit
        public override void OnEdit(IChiselHandles handles)
        {
            var halfLength  = settings.length * 0.5f;
            var middle      = settings.radius * 0.25f;
            var frontPoint  = new Vector3(middle, middle,  halfLength);
            var backPoint   = new Vector3(middle, middle, -halfLength);

            ChiselProfileHandles.DoFaceHandle(handles, ref frontPoint, Vector3.forward);
            ChiselProfileHandles.DoFaceHandle(handles, ref backPoint, Vector3.back);

            if (handles.modified)
                settings.length = math.max(0, frontPoint.z - backPoint.z);
        }
        #endregion
    }
}
