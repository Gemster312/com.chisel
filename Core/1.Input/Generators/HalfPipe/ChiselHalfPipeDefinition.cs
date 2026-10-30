using System;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using Unity.Entities;

namespace Chisel.Core
{
    // Fork: a half-pipe, seen end on (x across, y up) and running along z. A flat floor between two quarter-round bends,
    // each rising into a straight wall; the solid is thickness deep under the floor, behind the bends and in the walls.
    // The origin is at the bottom of the solid, under the middle of the floor. With one bend it's a quarter-pipe: the bend
    // on +x, the floor's other end open. Each bend side is its own brush, so the curve's faces stay flat for the ball
    [Serializable]
    public struct ChiselHalfPipe : IBranchGenerator
    {
        readonly static ChiselHalfPipe kDefaultSettings = new()
        {
            length      = 4,
            radius      = 2,
            sides       = 12,
            floorWidth  = 2,
            thickness   = 0.5f,
            wallHeight  = 1,
            twoBends    = true
        };
        public static ref readonly ChiselHalfPipe DefaultSettings => ref kDefaultSettings;

        [Tooltip("How long the pipe is, along its length.")]
        [DistanceValue] public float length;
        [Tooltip("The radius of each bend's curve.")]
        [DistanceValue] public float radius;
        [Tooltip("How many flat faces make up each bend. About 8-12 keep a rolling ball smooth. Minimum: 1.")]
        public int sides;
        [Tooltip("How wide the flat floor between the bends is. 0 joins the bends into one curve.")]
        [DistanceValue] public float floorWidth;
        [Tooltip("How thick the solid is under the floor, behind the bends and in the walls.")]
        [DistanceValue] public float thickness;
        [Tooltip("How tall the straight wall above each bend is. 0 ends the sides at the top of the bends.")]
        [DistanceValue] public float wallHeight;
        [Tooltip("Two bends make a full half-pipe; one makes a quarter-pipe, its bend on the shape's right (+x).")]
        public bool twoBends;

        #region Properties
        const float kNoPartEpsilon = 0.0001f;

        public int   Bends           { get { return twoBends ? 2 : 1; } }
        public bool  HaveFloor       { get { return floorWidth > kNoPartEpsilon; } }
        public bool  HaveWalls       { get { return wallHeight > kNoPartEpsilon; } }
        public float BendHeight      { get { return thickness + radius; } }
        public float Top             { get { return BendHeight + wallHeight; } }
        public float OuterHalfWidth  { get { return floorWidth * 0.5f + radius + thickness; } }
        #endregion

        #region Generate
        public int PrepareAndCountRequiredBrushMeshes()
        {
            return (HaveFloor ? 1 : 0) + Bends * (sides + (HaveWalls ? 1 : 0));
        }

        public bool GenerateNodes(BlobAssetReference<InternalChiselSurfaceArray> surfaceDefinitionBlob,
                                  NativeList<GeneratedNode> nodes, Allocator allocator = Allocator.Persistent)// Indirect
        {
            ref var surfaceArray = ref surfaceDefinitionBlob.Value;
            const int back = (int)SurfaceSides.Back, front = (int)SurfaceSides.Front;
            const int inside = (int)SurfaceSides.Inside, outside = (int)SurfaceSides.Outside;

            using var brushes = new NativeList<BlobAssetReference<BrushMeshBlob>>(nodes.Length, Allocator.Temp);
            var piece = new BrushMeshFactory.ProfilePiece(Allocator.Temp);
            try
            {
                var halfFloor = floorWidth * 0.5f;
                var succeeded = true;
                if (HaveFloor)
                {
                    piece.Clear();
                    piece.Add(new float2(-halfFloor, 0), outside);
                    piece.Add(new float2( halfFloor, 0), outside);
                    piece.Add(new float2( halfFloor, thickness), inside);
                    piece.Add(new float2(-halfFloor, thickness), outside);
                    succeeded = BrushMeshFactory.AddProfileBrush(brushes, in piece, length, back, front, ref surfaceArray, allocator);
                }

                for (int bend = 0; bend < Bends && succeeded; bend++)
                {
                    // The right bend, then the left one mirrored
                    var side    = bend == 0 ? 1f : -1f;
                    var centre  = new float2(side * halfFloor, BendHeight);
                    var corner  = new float2(side * OuterHalfWidth, 0);
                    for (int s = 0; s < sides && succeeded; s++)
                    {
                        // From straight down to straight out
                        var angle0 = BendAngle(side, s / (float)sides);
                        var angle1 = BendAngle(side, (s + 1) / (float)sides);
                        BrushMeshFactory.SetBendPiece(piece, centre, radius, angle0, angle1, corner, inside, outside);
                        succeeded = BrushMeshFactory.AddProfileBrush(brushes, in piece, length, back, front, ref surfaceArray, allocator);
                    }

                    if (HaveWalls && succeeded)
                    {
                        var inner = side * (halfFloor + radius);
                        piece.Clear();
                        piece.Add(new float2(inner, BendHeight), outside);
                        piece.Add(new float2(corner.x, BendHeight), outside);
                        piece.Add(new float2(corner.x, Top), outside);
                        piece.Add(new float2(inner, Top), inside);
                        succeeded = BrushMeshFactory.AddProfileBrush(brushes, in piece, length, back, front, ref surfaceArray, allocator);
                    }
                }
                return BrushMeshFactory.FinishProfileBrushes(brushes, nodes, succeeded);
            }
            finally
            {
                piece.Dispose();
            }
        }

        // The angle (radians) at fraction t round a bend, 0 = straight down, 1 = straight out to its side
        static float BendAngle(float side, float t)
        {
            var right = -math.PI * 0.5f + t * math.PI * 0.5f;
            return side > 0 ? right : math.PI - right;
        }

        public void Dispose() { }
        #endregion

        #region Surfaces
        public enum SurfaceSides : byte
        {
            Back    = 0,
            Front   = 1,
            Inside  = 2,
            Outside = 3,

            TotalSides
        }

        [BurstDiscard]
        public int RequiredSurfaceCount { get { return (int)SurfaceSides.TotalSides; } }

        [BurstDiscard]
        public void UpdateSurfaces(ref ChiselSurfaceArray surfaceDefinition)
        {
            var surfaces = surfaceDefinition.surfaces;
            if (!surfaces[(int)SurfaceSides.Inside].HasMaterial) surfaces[(int)SurfaceSides.Inside].SetMaterial(ChiselProjectSettings.DefaultFloorMaterial);
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
            length      = math.max(kMinSize, math.abs(length));
            radius      = math.max(kMinSize, math.abs(radius));
            thickness   = math.max(kMinSize, math.abs(thickness));
            floorWidth  = math.max(0, floorWidth);
            wallHeight  = math.max(0, wallHeight);
            sides       = math.clamp(sides, 1, kMaxSides);
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
    public class ChiselHalfPipeDefinition : SerializedBranchGenerator<ChiselHalfPipe>
    {
        public const string kNodeTypeName = "Half Pipe";

        #region OnEdit
        public override void OnEdit(IChiselHandles handles)
        {
            var halfLength  = settings.length * 0.5f;
            var middle      = settings.Top * 0.5f;
            var frontPoint  = new Vector3(0, middle,  halfLength);
            var backPoint   = new Vector3(0, middle, -halfLength);
            var topPoint    = new Vector3(settings.OuterHalfWidth - settings.thickness * 0.5f, settings.Top, 0);

            ChiselProfileHandles.DoFaceHandle(handles, ref frontPoint, Vector3.forward);
            ChiselProfileHandles.DoFaceHandle(handles, ref backPoint, Vector3.back);
            ChiselProfileHandles.DoFaceHandle(handles, ref topPoint, Vector3.up);

            if (handles.modified)
            {
                settings.length     = math.max(0, frontPoint.z - backPoint.z);
                settings.wallHeight = math.max(0, topPoint.y - settings.BendHeight);
            }
        }
        #endregion
    }
}
