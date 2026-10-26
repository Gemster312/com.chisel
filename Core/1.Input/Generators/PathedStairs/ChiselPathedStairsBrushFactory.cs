using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Unity.Entities;

namespace Chisel.Core
{
    // TODO: rename
    public sealed partial class BrushMeshFactory
    {
        public static int CountPathedStairBrushes(UnsafeList<SegmentVertex> shapeVertices,
                                                  bool              closedLoop,

												  MinMaxAABB        bounds,

                                                  float	            stepHeight,
                                                  float	            stepDepth,

                                                  float	            treadHeight,

                                                  float	            nosingDepth,

                                                  float             plateauHeight,

                                                  StairsRiserType   riserType,
                                                  float	            riserDepth,

                                                  StairsSideType    leftSide,
                                                  StairsSideType    rightSide,
        
                                                  float	            sideWidth,
                                                  float	            sideHeight,
                                                  float	            sideDepth)
        {
            var totalSubMeshCount = 0;
            for (int i = 0; i < shapeVertices.Length; i++)
            {
                if (i == 0 && !closedLoop)
                    continue;

                var segmentLeftSide  = (!closedLoop && i ==                        1) ? leftSide : StairsSideType.None;
                var segmentRightSide = (!closedLoop && i == shapeVertices.Length - 1) ? rightSide : StairsSideType.None;
                
                var description = new LineairStairsData(bounds, stepHeight, stepDepth, treadHeight,
                                                        nosingDepth, nosingWidth: 0, plateauHeight,
                                                        riserType, riserDepth,
                                                        segmentLeftSide, segmentRightSide,
                                                        sideWidth, sideHeight, sideDepth);
                totalSubMeshCount += description.subMeshCount;
            }
            return totalSubMeshCount;
        }
        
        // TODO: kind of broken, needs fixing
        public static bool GeneratePathedStairs(NativeList<BlobAssetReference<BrushMeshBlob>> brushMeshes,
                                                UnsafeList<SegmentVertex> shapeVertices,
                                                bool                closedLoop,
												MinMaxAABB          bounds,

                                                float	            stepHeight,
                                                float	            stepDepth,

                                                float	            treadHeight,

                                                float	            nosingDepth,
                                                
                                                float               plateauHeight,

                                                StairsRiserType     riserType,
                                                float	            riserDepth,

                                                StairsSideType      leftSide,
                                                StairsSideType      rightSide,
        
                                                float	            sideWidth,
                                                float	            sideHeight,
                                                float	            sideDepth,
                                                in BlobAssetReference<InternalChiselSurfaceArray> surfaceDefinitionBlob,
                                                Allocator           allocator = Allocator.Persistent)// Indirect
        {
            var absDepth    = math.abs(bounds.Max.z - bounds.Min.z);
            var absHeight   = math.abs(bounds.Max.y - bounds.Min.y);
            var width       = (bounds.Max.x - bounds.Min.x);
            var centerX     = (bounds.Max.x + bounds.Min.x) * 0.5f;

            var halfDepth	= absDepth  * 0.5f;
            var halfHeight	= absHeight * 0.5f;

            int subMeshIndex = 0;
            var count = shapeVertices.Length;
            if (count == 0)
                return false;
            for (int vi0 = (count + count - 3) % count, vi1 = (count + count - 2) % count, vi2 = (count + count - 1) % count, vi3 = 0; vi3 < count; vi0 = vi1, vi1 = vi2, vi2 = vi3, vi3++)
            {
                if (vi2 == 0 && !closedLoop)
                    continue;

                // TODO: optimize this, we're probably redoing a lot of stuff for every iteration
                var v0 = shapeVertices[vi0].position;
                var v1 = shapeVertices[vi1].position;
                var v2 = shapeVertices[vi2].position;
                var v3 = shapeVertices[vi3].position;

                var m0 = (v0 + v1) * 0.5f;
                var m1 = (v1 + v2) * 0.5f;
                var m2 = (v2 + v3) * 0.5f;

                var d0 = (v1 - v0);
                var d1 = (v2 - v1);
                var d2 = (v3 - v2);

                var maxWidth0 = math.length(d0);
                var maxWidth1 = math.length(d1);
                var maxWidth2 = math.length(d2);
                var halfWidth1 = d1 * 0.5f;

                d0 /= maxWidth0;
                d1 /= maxWidth1;
                d2 /= maxWidth2;

                var depthVector = new float3(d1.y, 0, -d1.x);
                var lineCenter  = new float3(m1.x, halfHeight, m1.y) - (depthVector * halfDepth);

                var depthVector0 = new float2(d0.y, -d0.x) * absDepth;
                var depthVector1 = new float2(d1.y, -d1.x) * absDepth;
                var depthVector2 = new float2(d2.y, -d2.x) * absDepth;

                m0 -= depthVector0;
                m1 -= depthVector1;
                m2 -= depthVector2;

				var leftShear = Intersect(m1, d1, m0, d0, out float2 output) ? math.dot(d1, (output - (m1 - halfWidth1))) : 0;
				var rightShear	= Intersect(m1, d1, m2, d2, out output) ? -math.dot(d1, (output - (m1 + halfWidth1))) : 0;

                var transform = float4x4.TRS(lineCenter, // move to center of line
                                              quaternion.LookRotationSafe(depthVector, Vector3.up),	// rotate to align with line
                                              new float3(1));

                // set the width to the width of the line
                var newWidth = math.abs(maxWidth1);
                var halfWidth = newWidth * 0.5f;
                var min = bounds.Min;
                var max = bounds.Max;
                min.x = centerX - halfWidth;
                max.x = centerX + halfWidth;
                bounds = BoundsExtensions.CreateAABB(min: min, max: max);

                var segmentLeftSide  = (!closedLoop && vi2 ==                        1) ? leftSide  : StairsSideType.None;
                var segmentRightSide = (!closedLoop && vi2 == shapeVertices.Length - 1) ? rightSide : StairsSideType.None;

                var description = new LineairStairsData(bounds,
                                                        stepHeight, stepDepth,
                                                        treadHeight,
                                                        nosingDepth, nosingWidth: 0,
                                                        plateauHeight,
                                                        riserType, riserDepth,
                                                        segmentLeftSide, segmentRightSide,
                                                        sideWidth, sideHeight, sideDepth);
                var subMeshCount = description.subMeshCount;
                if (subMeshCount == 0)
                    continue;

                if (!GenerateLinearStairsSubMeshes(brushMeshes, subMeshIndex, in description, in surfaceDefinitionBlob, allocator)) // Indirect
                    return false;

                for (int m = 0; m < subMeshCount; m++)
                {
                    if (!brushMeshes[subMeshIndex + m].IsCreated)
                        return false;
                    ref var brushMesh = ref brushMeshes[subMeshIndex + m].Value;
                    ref var localVertices = ref brushMesh.localVertices;
                    for (int v = 0; v < localVertices.Length; v++)
                    {
                        // TODO: is it possible to put all of this in a single matrix?
                        // lerp the stairs to go from less wide to wider depending on the depth of the vertex
                        var depthFactor = 1.0f - ((localVertices[v].z / absDepth) + 0.5f);
                        var scale		= ((localVertices[v].x - centerX) / halfWidth);
                        var wideFactor  = (scale * 0.5f) + 0.5f; // 0 on the right side (-x), 1 on the left (+x)

                        // lerp the stairs width depending on if it's on the left or right side of the stairs
                        localVertices[v].x = math.lerp( scale * (halfWidth - (rightShear * depthFactor)),
                                                        scale * (halfWidth - (leftShear  * depthFactor)),
                                                        wideFactor);
                        localVertices[v] = math.mul(transform, new float4(localVertices[v], 1)).xyz;
                    }

                    // The brush was made, planes and bounds too, before its vertices moved onto the path, and the CSG works
                    // from the planes
                    if (!RecalculatePlanesAndBounds(ref brushMesh))
                        return false;
                }

                subMeshIndex += subMeshCount;
            }
            return true;
        }

        // As CalculatePlanes and CalculateBounds do while a brush mesh is being built, for one that's already built. False when
        // a polygon has collapsed (no area, so no plane), as happens when flights are too deep for their path and meet
        static bool RecalculatePlanesAndBounds(ref BrushMeshBlob brushMesh)
        {
            ref var vertices  = ref brushMesh.localVertices;
            ref var halfEdges = ref brushMesh.halfEdges;
            ref var polygons  = ref brushMesh.polygons;
            for (int p = 0; p < polygons.Length && p < brushMesh.localPlanes.Length; p++)
            {
                // Newell's algorithm, as in CalculatePlane
                var firstEdge  = polygons[p].firstEdge;
                var lastEdge   = firstEdge + polygons[p].edgeCount;
                var normal     = double3.zero;
                var prevVertex = (double3)vertices[halfEdges[lastEdge - 1].vertexIndex];
                for (int n = firstEdge; n < lastEdge; n++)
                {
                    var currVertex = (double3)vertices[halfEdges[n].vertexIndex];
                    normal.x += ((prevVertex.y - currVertex.y) * (prevVertex.z + currVertex.z));
                    normal.y += ((prevVertex.z - currVertex.z) * (prevVertex.x + currVertex.x));
                    normal.z += ((prevVertex.x - currVertex.x) * (prevVertex.y + currVertex.y));
                    prevVertex = currVertex;
                }
                if (math.lengthsq(normal) < 1e-12)
                    return false;
                normal = math.normalize(normal);

                var d = 0.0;
                for (int n = firstEdge; n < lastEdge; n++)
                    d -= math.dot(normal, vertices[halfEdges[n].vertexIndex]);
                d /= polygons[p].edgeCount;
                brushMesh.localPlanes[p] = new float4((float3)normal, (float)d);
            }

            var min = new float3(float.PositiveInfinity);
            var max = new float3(float.NegativeInfinity);
            for (int i = 0; i < vertices.Length; i++)
            {
                min = math.min(min, vertices[i]);
                max = math.max(max, vertices[i]);
            }
            brushMesh.localBounds = new MinMaxAABB { Min = min, Max = max };
            return true;
        }


        // TODO: move somewhere else
        static bool Intersect(float2 p1, float2 d1,
                              float2 p2, float2 d2, out float2 intersection)
        {
            const float kEpsilon = 0.0001f;

            var f = d1.y * d2.x - d1.x * d2.y;
            // check if the rays are parallel
            if (f >= -kEpsilon && f <= kEpsilon)
            {
                intersection = float2.zero;
                return false;
            }

            var c0 = p1 - p2;
            var t = (d2.y * c0.x - d2.x * c0.y) / f;
            intersection = p1 + (t * d1);
            return true;
        }

    }
}