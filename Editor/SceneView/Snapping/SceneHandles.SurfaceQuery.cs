using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Chisel.Components;

namespace Chisel.Editors
{
    // Fork: the scene's surfaces, leaving out the selection, for the move handle's Ctrl+Shift drag and for 1D handles
    // snapping to nearby surface levels. Chisel models are queried through Chisel itself (a selected generator's faces are
    // in its model's mesh, so the model's colliders can't leave them out); anything else with a collider (sculptures,
    // props) through physics
    internal static class SurfaceQuery
    {
        const float kLevelRange = 25.0f;                    // How far along the slide line levels are looked for, either way
        const float kLevelMargin = 2.0f;                    // How far past the selection's footprint levels are looked for
        const float kLevelSpacing = 0.5f;                   // Closest spacing of the lines cast across the footprint
        const int   kLevelMaxLines = 12;                    // Most lines cast across each side of the footprint
        const float kLevelFacing = 0.95f;                   // How square to the slide line a surface must face to be a level

        struct Hit
        {
            public Vector3 point, normal;
        }

        readonly static List<ChiselIntersection> s_Intersections = new();
        readonly static List<Hit> s_Hits = new();

        // The nearest front-facing surface along the ray
        public static bool Raycast(Ray ray, float distance, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            s_Hits.Clear();
            FindHits(ray.origin, ray.origin + ray.direction * distance, false, s_Hits);
            var nearest = float.PositiveInfinity;
            foreach (var hit in s_Hits)
            {
                var along = Vector3.Dot(hit.point - ray.origin, ray.direction);
                if (along < 0 || along >= nearest)
                    continue;
                nearest = along;
                point = hit.point;
                normal = Vector3.Dot(hit.normal, ray.direction) > 0 ? -hit.normal : hit.normal;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        // Points on the line through linePoint where it meets the planes of surfaces facing along it (floors and ceilings
        // for an upright line), so a handle slid along it can stop level with them. Lines parallel to it are cast across
        // the selection's footprint and kLevelMargin past it: a handle in the middle of a wide top face still finds the
        // floor beside the selection
        public static void FindLevels(Vector3 linePoint, Vector3 direction, List<Vector3> levels)
        {
            levels.Clear();
            if (direction.sqrMagnitude == 0)
                return;
            direction.Normalize();
            var side = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var other = Vector3.Cross(direction, side);

            // The footprint across the line, as ranges along side and other measured from linePoint
            var area = new Bounds(linePoint, Vector3.zero);
            if (TryGetSelectionBounds(out var selectionBounds))
                area.Encapsulate(selectionBounds);
            area.Expand(kLevelMargin * 2);
            var centre = area.center - linePoint;
            var sideCentre  = Vector3.Dot(centre, side);
            var otherCentre = Vector3.Dot(centre, other);
            var sideHalf  = Vector3.Dot(area.extents, new Vector3(Mathf.Abs(side.x),  Mathf.Abs(side.y),  Mathf.Abs(side.z)));
            var otherHalf = Vector3.Dot(area.extents, new Vector3(Mathf.Abs(other.x), Mathf.Abs(other.y), Mathf.Abs(other.z)));
            var sideCount  = Mathf.Clamp(Mathf.CeilToInt(sideHalf  * 2 / kLevelSpacing) + 1, 2, kLevelMaxLines);
            var otherCount = Mathf.Clamp(Mathf.CeilToInt(otherHalf * 2 / kLevelSpacing) + 1, 2, kLevelMaxLines);

            s_Hits.Clear();
            FindHits(linePoint - direction * kLevelRange, linePoint + direction * kLevelRange, true, s_Hits);
            for (int i = 0; i < sideCount; i++)
            {
                var sideOffset = sideCentre - sideHalf + sideHalf * 2 * i / (sideCount - 1);
                for (int j = 0; j < otherCount; j++)
                {
                    var otherOffset = otherCentre - otherHalf + otherHalf * 2 * j / (otherCount - 1);
                    var point = linePoint + side * sideOffset + other * otherOffset;
                    FindHits(point - direction * kLevelRange, point + direction * kLevelRange, true, s_Hits);
                }
            }

            foreach (var hit in s_Hits)
            {
                if (Mathf.Abs(Vector3.Dot(hit.normal, direction)) < kLevelFacing)
                    continue;
                var level = linePoint + direction * Vector3.Dot(hit.point - linePoint, direction);
                var known = false;
                foreach (var found in levels)
                {
                    if ((found - level).sqrMagnitude < 0.0001f) { known = true; break; }
                }
                if (!known)
                    levels.Add(level);
            }
        }

        // World bounds of the selected Chisel nodes and renderers
        static bool TryGetSelectionBounds(out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var gameObject in Selection.gameObjects)
            {
                if (!gameObject)
                    continue;
                foreach (var node in gameObject.GetComponentsInChildren<ChiselNodeComponent>())
                    Add(node.hierarchyItem?.Bounds ?? default, ref bounds, ref found);
                foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>())
                    Add(renderer.bounds, ref bounds, ref found);
            }
            return found;

            static void Add(Bounds add, ref Bounds bounds, ref bool found)
            {
                if (add.size.sqrMagnitude == 0 || float.IsInfinity(add.size.sqrMagnitude))
                    return;
                if (found) bounds.Encapsulate(add); else bounds = add;
                found = true;
            }
        }

        static void FindHits(Vector3 start, Vector3 end, bool backFaces, List<Hit> hits)
        {
            var selected = Selection.gameObjects;
            s_Intersections.Clear();
            if (ChiselSceneQuery.FindFirstWorldIntersection(s_Intersections, start, end, ignoreBackfaced: !backFaces, ignore: selected))
            {
                foreach (var intersection in s_Intersections)
                    hits.Add(new Hit { point = intersection.worldPlaneIntersection, normal = intersection.worldPlane.normal });
            }

            var delta = end - start;
            var length = delta.magnitude;
            if (length == 0)
                return;
            AddPhysicsHits(new Ray(start, delta / length), length, selected, hits);
            if (backFaces) // Physics only hits faces from the front, so cast back the other way too
                AddPhysicsHits(new Ray(end, -delta / length), length, selected, hits);
        }

        static void AddPhysicsHits(Ray ray, float length, GameObject[] selected, List<Hit> hits)
        {
            foreach (var hit in Physics.RaycastAll(ray, length, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<ChiselModelComponent>(true) || IsSelected(hit.transform, selected))
                    continue;
                hits.Add(new Hit { point = hit.point, normal = hit.normal });
            }
        }

        static bool IsSelected(Transform transform, GameObject[] selected)
        {
            foreach (var gameObject in selected)
            {
                if (gameObject && transform.IsChildOf(gameObject.transform))
                    return true;
            }
            return false;
        }
    }
}
