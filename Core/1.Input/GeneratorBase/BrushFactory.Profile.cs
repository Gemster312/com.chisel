using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Chisel.Core
{
    // Fork: shapes drawn as a flat outline (a profile) in x, y and pushed along z, cut into convex pieces with one brush
    // each (a brush has to be convex). Used by the Half Pipe, Rounded Box and Fillet generators
    public sealed partial class BrushMeshFactory
    {
        const float kProfileEpsilon = 0.0001f;

        // A convex piece of a profile: its corners in order (either way round) and, for each, the surface of the side from
        // it to the next corner
        public struct ProfilePiece : IDisposable
        {
            public NativeList<float2> corners;
            public NativeList<int>    sideSurfaces;

            public ProfilePiece(Allocator allocator)
            {
                corners      = new NativeList<float2>(16, allocator);
                sideSurfaces = new NativeList<int>(16, allocator);
            }

            public void Clear() { corners.Clear(); sideSurfaces.Clear(); }
            public void Add(float2 corner, int sideSurface) { corners.Add(corner); sideSurfaces.Add(sideSurface); }

            public void Dispose()
            {
                if (corners.IsCreated) corners.Dispose();
                if (sideSurfaces.IsCreated) sideSurfaces.Dispose();
            }
        }

        // The piece pushed length along z, centred on z = 0, as one brush; its back cap (at -z) and front cap get the given
        // surfaces. Repeated corners (a piece narrowing to a point) are dropped. Null if fewer than three corners are left
        public static BlobAssetReference<BrushMeshBlob> CreateProfileBrush(in ProfilePiece piece, float length, int backSurface, int frontSurface,
                                                                           ref InternalChiselSurfaceArray surfaceArray, Allocator allocator)
        {
            var inCount = piece.corners.Length;
            using var corners = new NativeList<float2>(inCount, Allocator.Temp);
            using var sides   = new NativeList<int>(inCount, Allocator.Temp);
            for (int i = 0; i < inCount; i++)
            {
                // A side of no length: the next corner (the same point) takes over
                if (math.lengthsq(piece.corners[(i + 1) % inCount] - piece.corners[i]) < kProfileEpsilon * kProfileEpsilon)
                    continue;
                corners.Add(piece.corners[i]);
                sides.Add(piece.sideSurfaces[i]);
            }

            var count = corners.Length;
            if (count < 3)
                return BlobAssetReference<BrushMeshBlob>.Null;

            // Chisel wants the back cap's normal facing away from the push (along +z): its corners clockwise in x, y
            var area = 0f;
            for (int i = 0, j = count - 1; i < count; j = i, i++)
                area += corners[j].x * corners[i].y - corners[i].x * corners[j].y;
            var reverse = area > 0;

            var vertices = new NativeArray<float3>(count, Allocator.Temp);
            var surfaces = new NativeArray<int>(count + 2, Allocator.Temp);
            try
            {
                surfaces[0] = backSurface;
                surfaces[1] = frontSurface;
                for (int j = 0; j < count; j++)
                {
                    var i = reverse ? count - 1 - j : j;
                    vertices[j] = new float3(corners[i], -length * 0.5f);

                    // The side from corner j to j + 1 (reversed, that's the original side from count - 2 - j); an extruded
                    // brush's side polygon k runs from corner k - 1 to corner k
                    var side = reverse ? (count * 2 - 2 - j) % count : j;
                    surfaces[2 + (j + 1) % count] = sides[side];
                }
                return CreateExtrudedSubMeshBlob(vertices, new float3(0, 0, length), surfaces, ref surfaceArray, allocator);
            }
            finally
            {
                vertices.Dispose();
                surfaces.Dispose();
            }
        }

        // The piece made into a brush, added to brushes; false if it couldn't be made
        public static bool AddProfileBrush(NativeList<BlobAssetReference<BrushMeshBlob>> brushes, in ProfilePiece piece, float length,
                                           int backSurface, int frontSurface, ref InternalChiselSurfaceArray surfaceArray, Allocator allocator)
        {
            var brush = CreateProfileBrush(in piece, length, backSurface, frontSurface, ref surfaceArray, allocator);
            if (!brush.IsCreated)
                return false;
            brushes.Add(brush);
            return true;
        }

        // Hands the brushes to the generator's nodes when all of them were made (as many as it counted), else frees them
        public static bool FinishProfileBrushes(NativeList<BlobAssetReference<BrushMeshBlob>> brushes, NativeList<GeneratedNode> nodes, bool succeeded)
        {
            if (!succeeded || brushes.Length != nodes.Length)
            {
                for (int i = 0; i < brushes.Length; i++)
                {
                    if (brushes[i].IsCreated)
                        brushes[i].Dispose();
                }
                return false;
            }
            for (int i = 0; i < brushes.Length; i++)
                nodes[i] = GeneratedNode.GenerateBrush(brushes[i]);
            return true;
        }

        // One side's slice of a rounded bend: between the rays from centre at angles angle0 and angle1 (radians), from the
        // arc (cut straight across: the curved surface) out to the lines x = outside.x and y = outside.y, which meet in the
        // bend's outside corner. A slice that crosses the corner's direction takes the corner too. (A piece is passed by value:
        // its lists are shared, not copied)
        public static void SetBendPiece(ProfilePiece piece, float2 centre, float radius, float angle0, float angle1, float2 outside,
                                        int curveSurface, int otherSurface)
        {
            piece.Clear();
            var direction0 = new float2(math.cos(angle0), math.sin(angle0));
            var direction1 = new float2(math.cos(angle1), math.sin(angle1));
            var outside0   = RayToOutside(centre, direction0, outside, out var onX0);
            var outside1   = RayToOutside(centre, direction1, outside, out var onX1);
            piece.Add(centre + direction0 * radius, curveSurface);
            piece.Add(centre + direction1 * radius, otherSurface);
            piece.Add(outside1, otherSurface);
            if (onX0 != onX1)
                piece.Add(outside, otherSurface);
            piece.Add(outside0, otherSurface);
        }

        // Where a ray from centre first meets the line x = outside.x or y = outside.y; onX: it meets the x line first
        static float2 RayToOutside(float2 centre, float2 direction, float2 outside, out bool onX)
        {
            var toOutside = outside - centre;
            var alongX = math.abs(direction.x) > kProfileEpsilon ? toOutside.x / direction.x : float.PositiveInfinity;
            var alongY = math.abs(direction.y) > kProfileEpsilon ? toOutside.y / direction.y : float.PositiveInfinity;
            if (alongX < 0) alongX = float.PositiveInfinity;
            if (alongY < 0) alongY = float.PositiveInfinity;
            onX = alongX <= alongY;
            return centre + direction * math.min(alongX, alongY);
        }
    }

    // Fork: the face handles the profile generators share: an arrow on a face that drags it along its normal
    public static class ChiselProfileHandles
    {
        public static void DoFaceHandle(IChiselHandles handles, ref UnityEngine.Vector3 point, UnityEngine.Vector3 normal)
        {
            handles.backfaced = handles.IsSufaceBackFaced(point, normal);
            handles.DoDirectionHandle(ref point, normal);
            handles.backfaced = false;
        }
    }
}
