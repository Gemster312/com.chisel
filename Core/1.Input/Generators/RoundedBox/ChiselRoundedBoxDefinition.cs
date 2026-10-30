using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;

namespace Chisel.Core
{
    // Fork: a box with any of its four edges along its length (z) rounded, seen end on as a rectangle (x across, y up)
    // with chosen corners rounded. One convex brush. The origin is at the middle of its bottom
    [Serializable]
    public struct ChiselRoundedBox : IBranchGenerator
    {
        readonly static ChiselRoundedBox kDefaultSettings = new()
        {
            width               = 1,
            height              = 1,
            length              = 1,
            radius              = 0.25f,
            sides               = 8,
            roundTopLeft        = true,
            roundTopRight       = true,
            roundBottomLeft     = true,
            roundBottomRight    = true
        };
        public static ref readonly ChiselRoundedBox DefaultSettings => ref kDefaultSettings;

        [Tooltip("How wide the box is, across its rounded edges.")]
        [DistanceValue] public float width;
        [Tooltip("How tall the box is.")]
        [DistanceValue] public float height;
        [Tooltip("How long the box is, along its rounded edges.")]
        [DistanceValue] public float length;
        [Tooltip("The radius of the rounded edges. Maximum: half the width or height, whichever is smaller.")]
        [DistanceValue] public float radius;
        [Tooltip("How many flat faces make up each rounded edge. About 8-12 keep a rolling ball smooth. Minimum: 1.")]
        public int sides;
        [Tooltip("Rounds the top edge on the shape's left (-x).")]
        public bool roundTopLeft;
        [Tooltip("Rounds the top edge on the shape's right (+x).")]
        public bool roundTopRight;
        [Tooltip("Rounds the bottom edge on the shape's left (-x).")]
        public bool roundBottomLeft;
        [Tooltip("Rounds the bottom edge on the shape's right (+x).")]
        public bool roundBottomRight;

        #region Generate
        public int PrepareAndCountRequiredBrushMeshes() { return 1; }

        public bool GenerateNodes(BlobAssetReference<InternalChiselSurfaceArray> surfaceDefinitionBlob,
                                  NativeList<GeneratedNode> nodes, Allocator allocator = Allocator.Persistent)// Indirect
        {
            ref var surfaceArray = ref surfaceDefinitionBlob.Value;
            using var brushes = new NativeList<BlobAssetReference<BrushMeshBlob>>(nodes.Length, Allocator.Temp);
            var piece = new BrushMeshFactory.ProfilePiece(Allocator.Temp);
            try
            {
                var halfWidth = width * 0.5f;
                var r = radius;
                // Anticlockwise from the bottom left; each corner's rounding turns a quarter from its start angle, and the
                // straight side after it takes that side's surface
                AddCorner(piece, new float2(-halfWidth, 0),      new float2(-halfWidth + r, r),          math.PI,        roundBottomLeft,  SurfaceSides.Bottom);
                AddCorner(piece, new float2( halfWidth, 0),      new float2( halfWidth - r, r),          math.PI * 1.5f, roundBottomRight, SurfaceSides.Right);
                AddCorner(piece, new float2( halfWidth, height), new float2( halfWidth - r, height - r), 0,              roundTopRight,    SurfaceSides.Top);
                AddCorner(piece, new float2(-halfWidth, height), new float2(-halfWidth + r, height - r), math.PI * 0.5f, roundTopLeft,     SurfaceSides.Left);
                var succeeded = BrushMeshFactory.AddProfileBrush(brushes, in piece, length, (int)SurfaceSides.Back, (int)SurfaceSides.Front, ref surfaceArray, allocator);
                return BrushMeshFactory.FinishProfileBrushes(brushes, nodes, succeeded);
            }
            finally
            {
                piece.Dispose();
            }
        }

        readonly void AddCorner(BrushMeshFactory.ProfilePiece piece, float2 corner, float2 centre, float startAngle, bool rounded, SurfaceSides sideAfter)
        {
            if (!rounded || radius <= 0)
            {
                piece.Add(corner, (int)sideAfter);
                return;
            }
            for (int s = 0; s <= sides; s++)
            {
                var angle = startAngle + (s / (float)sides) * math.PI * 0.5f;
                piece.Add(centre + new float2(math.cos(angle), math.sin(angle)) * radius, s < sides ? (int)SurfaceSides.Rounded : (int)sideAfter);
            }
        }

        public void Dispose() { }
        #endregion

        #region Surfaces
        public enum SurfaceSides : byte
        {
            Back    = 0,
            Front   = 1,
            Bottom  = 2,
            Right   = 3,
            Top     = 4,
            Left    = 5,
            Rounded = 6,

            TotalSides
        }

        [BurstDiscard]
        public int RequiredSurfaceCount { get { return (int)SurfaceSides.TotalSides; } }

        [BurstDiscard]
        public void UpdateSurfaces(ref ChiselSurfaceArray surfaceDefinition)
        {
            var surfaces = surfaceDefinition.surfaces;
            if (!surfaces[(int)SurfaceSides.Top   ].HasMaterial) surfaces[(int)SurfaceSides.Top   ].SetMaterial(ChiselProjectSettings.DefaultFloorMaterial);
            if (!surfaces[(int)SurfaceSides.Bottom].HasMaterial) surfaces[(int)SurfaceSides.Bottom].SetMaterial(ChiselProjectSettings.DefaultFloorMaterial);
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
            width   = math.max(kMinSize, math.abs(width));
            height  = math.max(kMinSize, math.abs(height));
            length  = math.max(kMinSize, math.abs(length));
            radius  = math.clamp(radius, 0, math.min(width, height) * 0.5f);
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
    public class ChiselRoundedBoxDefinition : SerializedBranchGenerator<ChiselRoundedBox>
    {
        public const string kNodeTypeName = "Rounded Box";

        #region OnEdit
        public override void OnEdit(IChiselHandles handles)
        {
            var halfLength  = settings.length * 0.5f;
            var halfWidth   = settings.width * 0.5f;
            var middle      = settings.height * 0.5f;
            var frontPoint  = new Vector3(0, middle,  halfLength);
            var backPoint   = new Vector3(0, middle, -halfLength);
            var rightPoint  = new Vector3( halfWidth, middle, 0);
            var leftPoint   = new Vector3(-halfWidth, middle, 0);
            var topPoint    = new Vector3(0, settings.height, 0);

            if (ChiselProfileHandles.DoFaceHandle(handles, ref frontPoint, Vector3.forward))
                settings.length = math.max(0, frontPoint.z * 2);
            if (ChiselProfileHandles.DoFaceHandle(handles, ref backPoint, Vector3.back))
                settings.length = math.max(0, -backPoint.z * 2);
            if (ChiselProfileHandles.DoFaceHandle(handles, ref rightPoint, Vector3.right))
                settings.width = math.max(0, rightPoint.x * 2);
            if (ChiselProfileHandles.DoFaceHandle(handles, ref leftPoint, Vector3.left))
                settings.width = math.max(0, -leftPoint.x * 2);
            if (ChiselProfileHandles.DoFaceHandle(handles, ref topPoint, Vector3.up))
                settings.height = math.max(0, topPoint.y);

            // The radius: on the flat face beside the first rounded corner, where the rounding starts, dragged towards the
            // face's middle to round more
            if (!FirstRoundedCorner(out var side, out var top))
                return;
            var radiusPoint = new Vector3(side * (halfWidth - settings.radius), top ? settings.height : 0, 0);
            if (ChiselProfileHandles.DoFaceHandle(handles, ref radiusPoint, new Vector3(-side, 0, 0)))
                settings.radius = math.max(0, halfWidth - side * radiusPoint.x);
        }

        // side: +1 right, -1 left
        bool FirstRoundedCorner(out float side, out bool top)
        {
            side = 1; top = true;
            if (settings.roundTopRight)    return true;
            side = -1;
            if (settings.roundTopLeft)     return true;
            top = false;
            if (settings.roundBottomLeft)  return true;
            side = 1;
            return settings.roundBottomRight;
        }
        #endregion
    }
}
