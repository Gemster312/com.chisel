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
        readonly static float[] kLevelRadii = { 0.0f, 0.75f, 2.0f };   // Out from the line: a floor beside the stairs' top still counts
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
        // for an upright line), found on and around the line, so a handle slid along it can stop level with them
        public static void FindLevels(Vector3 linePoint, Vector3 direction, List<Vector3> levels)
        {
            levels.Clear();
            if (direction.sqrMagnitude == 0)
                return;
            direction.Normalize();
            var side = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var other = Vector3.Cross(direction, side);

            s_Hits.Clear();
            foreach (var radius in kLevelRadii)
            {
                for (int i = 0; i < (radius == 0 ? 1 : 4); i++)
                {
                    var offset = (i == 0 ? side : i == 1 ? -side : i == 2 ? other : -other) * radius;
                    FindHits(linePoint + offset - direction * kLevelRange, linePoint + offset + direction * kLevelRange, true, s_Hits);
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
