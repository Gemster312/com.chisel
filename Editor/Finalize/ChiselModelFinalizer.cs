using System.Collections.Generic;
using System.IO;
using Chisel.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chisel.Editors
{
    /// <summary>
    /// The editor half of Finalize (see <see cref="ChiselFinalize"/>): makes a model's sculpture, a GameObject named
    /// like the model holding its generated meshes as plain MeshFilter / MeshRenderer / MeshCollider components, with
    /// the meshes saved as one asset in a "Sculptures" folder beside the scene. The sculpture goes where
    /// <see cref="SculptureParent"/> says, else right beside the model as "&lt;Model&gt; (Sculpture)". The model is then
    /// switched off and tagged EditorOnly; <see cref="EditModel"/> switches back to it.
    /// </summary>
    public static class ChiselModelFinalizer
    {
        /// <summary>
        /// Where a model's sculpture goes when it is made (or moved to the model's scene), so a project can keep
        /// sculptures in a folder of their own. A parent in another scene than the model's, or null, puts the
        /// sculpture right beside its model instead.
        /// </summary>
        public static System.Func<ChiselModelComponent, Transform> SculptureParent;

        public const string kSculptureSuffix = " (Sculpture)";
        public const string kMeshFolderName  = "Sculptures";
        const string kUntaggedTag            = "Untagged";
        const string kFinalizeUndoName       = "Finalize";
        const string kEditUndoName           = "Edit Model";

        // Properties that tie a component to its own object or prefab, never copied from the generated components
        static readonly HashSet<string> kSkippedProperties = new()
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Enabled", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier"
        };

        /// <summary>Whether Finalize can run on this model, and if not, why</summary>
        public static bool CanFinalize(ChiselModelComponent model, out string reason)
        {
            reason = null;
            if (!model)
                reason = "the model is missing.";
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
                reason = "it can't be done in Play mode.";
            else if (model.IsDefaultModel)
                reason = "the default model can't be finalized. Move its brushes into a model first.";
            else if (!model.gameObject.activeSelf)
                reason = "it is switched off. Press Edit Model first.";
            else if (string.IsNullOrEmpty(model.gameObject.scene.path))
                reason = "its scene has never been saved, so there is no folder to save the meshes in.";
            else if (model.transform.parent && !model.transform.parent.gameObject.activeInHierarchy)
                reason = "its parent is switched off, so it can't build its meshes.";
            return reason == null;
        }

        /// <summary>
        /// Makes the model's sculpture and switches to it. When the sculpture is already up to date it only switches,
        /// which keeps the sculpture's baked lighting; <paramref name="rebuild"/> makes it again anyway. Returns whether the model
        /// ended up finalized.
        /// </summary>
        public static bool FinalizeModel(ChiselModelComponent model, bool rebuild = false)
        {
            if (!CanFinalize(model, out var reason))
            {
                Debug.LogError($"Can't finalize {(model ? model.name : "the model")}: {reason}", model);
                return false;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(kFinalizeUndoName);
            var undoGroup = Undo.GetCurrentGroup();
            try
            {
                var record = model.finalized;
                if (rebuild || record == null || !record.sculpture || record.inputHash != ChiselFinalize.GetInputHash(model))
                {
                    if (!MakeSculpture(model))
                        return false;
                }
                SwitchToSculpture(model);
                Selection.activeGameObject = model.finalized.sculpture;
                return true;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        public static void FinalizeModel(IEnumerable<ChiselModelComponent> models, bool rebuild = false)
        {
            foreach (var model in models)
                FinalizeModel(model, rebuild);
        }

        /// <summary>Switches the model back on, and its sculpture off, so it can be edited. Finalize again when done.</summary>
        public static void EditModel(ChiselModelComponent model)
        {
            if (!model)
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(kEditUndoName);
            var undoGroup = Undo.GetCurrentGroup();

            var gameObject = model.gameObject;
            var record     = model.finalized;
            // The sculpture is what was moved around while finalized; FollowSculpture normally keeps up already
            if (record != null && record.sculpture && record.sculpture.activeSelf)
                PlaceLike(model.transform, record.sculpture.transform, kEditUndoName);
            Undo.RecordObject(gameObject, kEditUndoName);
            if (gameObject.CompareTag(ChiselFinalize.kEditorOnlyTag))
                gameObject.tag = (record == null || string.IsNullOrEmpty(record.tagBeforeFinalize)) ? kUntaggedTag : record.tagBeforeFinalize;
            gameObject.SetActive(true);
            if (record != null && record.sculpture)
            {
                Undo.RecordObject(record.sculpture, kEditUndoName);
                record.sculpture.SetActive(false);
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

            if (ChiselModelManager.Instance.IsSelectable(model))
                ChiselModelManager.Instance.ActiveModel = model;
            Selection.activeGameObject = gameObject;

            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void EditModel(IEnumerable<ChiselModelComponent> models)
        {
            foreach (var model in models)
                EditModel(model);
        }

        static void SwitchToSculpture(ChiselModelComponent model)
        {
            var gameObject = model.gameObject;
            var record     = model.finalized;
            Undo.RecordObject(gameObject, kFinalizeUndoName);
            if (!gameObject.CompareTag(ChiselFinalize.kEditorOnlyTag))
            {
                Undo.RecordObject(model, kFinalizeUndoName);
                record.tagBeforeFinalize = gameObject.tag;
                gameObject.tag = ChiselFinalize.kEditorOnlyTag;
            }
            gameObject.SetActive(false);

            var sculpture = record.sculpture;
            if (sculpture.scene != gameObject.scene)
                MoveToScene(sculpture.transform, gameObject.scene, ParentForSculpture(model), kFinalizeUndoName);
            PlaceLike(sculpture.transform, model.transform, kFinalizeUndoName);
            Undo.RecordObject(sculpture, kFinalizeUndoName);
            sculpture.name = SculptureName(model, sculpture.transform);
            sculpture.SetActive(true);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        /// <summary>
        /// Called when a sculpture's transform or parent changed (see ChiselSculptureFollower): while it is the one
        /// switched on, its model follows it, so the model is where the sculpture is when it is edited or finalized
        /// again. Not recorded for undo, since undoing the sculpture's move moves the model back again.
        /// </summary>
        internal static void FollowSculpture(ChiselSculptureComponent sculpture)
        {
            var model = sculpture ? sculpture.source : null;
            if (!model || model.gameObject.activeSelf || model.finalized == null || model.finalized.sculpture != sculpture.gameObject)
                return;
            var moved = false;
            if (model.gameObject.scene != sculpture.gameObject.scene)
            {
                MoveToScene(model.transform, sculpture.gameObject.scene, parent: null, undoName: null);
                moved = true;
            }
            if (PlaceLike(model.transform, sculpture.transform, undoName: null) || moved)
            {
                EditorUtility.SetDirty(model.transform);
                EditorSceneManager.MarkSceneDirty(model.gameObject.scene);
            }
        }

        /// <summary>
        /// Gives <paramref name="target"/> the position, rotation and scale <paramref name="source"/> has in the
        /// world, under the parent the target already has (a model and its sculpture can be kept in different
        /// folders), recorded for undo unless <paramref name="undoName"/> is null. Returns whether anything changed.
        /// </summary>
        static bool PlaceLike(Transform target, Transform source, string undoName)
        {
            Vector3 position, scale;
            Quaternion rotation;
            var parent = target.parent;
            if (parent == source.parent)
            {
                source.GetLocalPositionAndRotation(out position, out rotation);
                scale = source.localScale;
            } else if (!parent)
            {
                source.GetPositionAndRotation(out position, out rotation);
                scale = source.lossyScale;
            } else
            {
                // Exact while neither parent is skewed (rotated with uneven scale), which a folder never is
                position = parent.InverseTransformPoint(source.position);
                rotation = Quaternion.Inverse(parent.rotation) * source.rotation;
                scale    = Divide(source.lossyScale, parent.lossyScale);
            }
            target.GetLocalPositionAndRotation(out var oldPosition, out var oldRotation);
            if (oldPosition == position && oldRotation == rotation && target.localScale == scale)
                return false;
            if (undoName != null)
                Undo.RecordObject(target, undoName);
            target.SetLocalPositionAndRotation(position, rotation);
            target.localScale = scale;
            return true;
        }

        static Vector3 Divide(Vector3 value, Vector3 by)
        {
            return new Vector3(by.x != 0f ? value.x / by.x : value.x,
                               by.y != 0f ? value.y / by.y : value.y,
                               by.z != 0f ? value.z / by.z : value.z);
        }

        /// <summary>Moves <paramref name="target"/> into <paramref name="scene"/>, under <paramref name="parent"/> if it is in that scene, else at its top level</summary>
        static void MoveToScene(Transform target, Scene scene, Transform parent, string undoName)
        {
            if (target.parent)
            {
                if (undoName != null) Undo.SetTransformParent(target, null, false, undoName);
                else target.SetParent(null, false);
            }
            if (undoName != null) Undo.MoveGameObjectToScene(target.gameObject, scene, undoName);
            else SceneManager.MoveGameObjectToScene(target.gameObject, scene);
            if (parent && parent.gameObject.scene == scene)
            {
                if (undoName != null) Undo.SetTransformParent(target, parent, false, undoName);
                else target.SetParent(parent, false);
            }
        }

        /// <summary>Where the model's sculpture goes: <see cref="SculptureParent"/>'s answer if it is in the model's scene, else the model's own parent</summary>
        static Transform ParentForSculpture(ChiselModelComponent model)
        {
            var parent = SculptureParent?.Invoke(model);
            return (parent && parent.gameObject.scene == model.gameObject.scene) ? parent : model.transform.parent;
        }

        /// <summary>Named like its model; right beside it, " (Sculpture)" tells the two apart</summary>
        static string SculptureName(ChiselModelComponent model, Transform sculpture)
        {
            return (sculpture.parent == model.transform.parent) ? model.name + kSculptureSuffix : model.name;
        }

        static bool MakeSculpture(ChiselModelComponent model)
        {
            var gameObject = model.gameObject;
            var scene      = gameObject.scene;

            // The lightmap UVs are laid out for the active scene's lighting settings, which have to be the model's own scene's
            if (SceneManager.GetActiveScene() != scene)
                SceneManager.SetActiveScene(scene);
            ChiselLightmapUVManager.UpdateLightmapResolution();
            ChiselNodeHierarchyManager.Update();
            ChiselModelManager.Instance.UpdateModels();

            var generated = model.generated;
            if (generated == null || !generated.IsValid())
            {
                Debug.LogError($"Can't finalize {model.name}: it has no generated meshes to copy.", model);
                return false;
            }

            // What to copy, and what to call it
            var renderables = new List<ChiselRenderObjects>();
            var colliders   = new List<ChiselColliderObjects>();
            var sources     = new List<Mesh>();
            var names       = new List<string>();
            foreach (var index in ChiselGeneratedObjects.kGeneratedMeshRendererIndices)
            {
                var renderable = generated.renderables[index];
                if (renderable == null || !renderable.Valid || !renderable.HasGeometry ||
                    !renderable.meshRenderer || !renderable.meshRenderer.enabled)
                    continue;
                renderables.Add(renderable);
                sources.Add(renderable.sharedMesh);
                names.Add(renderables.Count == 1 ? "Mesh" : $"Mesh {renderables.Count}");
            }
            foreach (var collider in generated.colliders)
            {
                if (collider == null || !collider.meshCollider || !collider.sharedMesh || collider.sharedMesh.vertexCount == 0)
                    continue;
                colliders.Add(collider);
                sources.Add(collider.sharedMesh);
                names.Add(colliders.Count == 1 ? "Collider" : $"Collider {colliders.Count}");
            }

            var record = model.finalized;
            var meshes = SaveMeshes(model, (record != null) ? record.sculpture : null, sources, names);

            var sculpture = MakeOrClearSculpture(model);
            var sculptureTransform = sculpture.transform;
            int meshIndex = 0;
            foreach (var renderable in renderables)
            {
                var part = MakePart(names[meshIndex], sculptureTransform, renderable.container);
                part.AddComponent<MeshFilter>().sharedMesh = meshes[meshIndex];
                var meshRenderer = part.AddComponent<MeshRenderer>();
                CopySettings(renderable.meshRenderer, meshRenderer);
                // The generated renderers are drawn by Chisel (forceRenderingOff); a new renderer has no lightmap yet
                meshRenderer.forceRenderingOff     = false;
                meshRenderer.lightmapIndex         = -1;
                meshRenderer.realtimeLightmapIndex = -1;
                EditorUtility.SetSelectedRenderState(meshRenderer, EditorSelectedRenderState.Wireframe | EditorSelectedRenderState.Highlight);
                meshIndex++;
            }
            if (colliders.Count > 0)
            {
                var part = MakePart("Collider", sculptureTransform, generated.colliderContainer);
                foreach (var collider in colliders)
                {
                    var meshCollider = part.AddComponent<MeshCollider>();
                    CopySettings(collider.meshCollider, meshCollider);
                    meshCollider.sharedMesh = meshes[meshIndex];
                    meshIndex++;
                }
            }

            Undo.RecordObject(model, kFinalizeUndoName);
            model.finalized ??= new ChiselFinalizedRecord();
            model.finalized.sculpture = sculpture;
            model.finalized.inputHash = ChiselFinalize.GetInputHash(model);
            EditorUtility.SetDirty(model);
            return true;
        }

        /// <summary>The sculpture, made where <see cref="ParentForSculpture"/> says when the model has none, otherwise emptied; flagged like the model</summary>
        static GameObject MakeOrClearSculpture(ChiselModelComponent model)
        {
            var modelObject    = model.gameObject;
            var modelTransform = model.transform;
            var record         = model.finalized;
            var sculpture      = (record != null) ? record.sculpture : null;
            if (!sculpture)
            {
                // Made in the active scene, which MakeSculpture made the model's
                sculpture = new GameObject(model.name);
                sculpture.SetActive(false);
                var parent = ParentForSculpture(model);
                sculpture.transform.SetParent(parent, false);
                if (parent == modelTransform.parent)
                    sculpture.transform.SetSiblingIndex(modelTransform.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(sculpture, kFinalizeUndoName);
            } else
            {
                var children = sculpture.transform;
                for (int i = children.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(children.GetChild(i).gameObject);
                Undo.RecordObject(sculpture, kFinalizeUndoName);
            }

            // SwitchToSculpture puts it where the model is, and names it
            sculpture.layer = modelObject.layer;
            sculpture.tag   = !modelObject.CompareTag(ChiselFinalize.kEditorOnlyTag) ? modelObject.tag :
                              (record == null || string.IsNullOrEmpty(record.tagBeforeFinalize)) ? kUntaggedTag : record.tagBeforeFinalize;
            GameObjectUtility.SetStaticEditorFlags(sculpture, GameObjectUtility.GetStaticEditorFlags(modelObject));

            if (!sculpture.TryGetComponent<ChiselSculptureComponent>(out var marker))
                marker = Undo.AddComponent<ChiselSculptureComponent>(sculpture);
            Undo.RecordObject(marker, kFinalizeUndoName);
            marker.source = model;
            return sculpture;
        }

        // A child of the sculpture at its origin, with the layer and static flags Chisel gave the generated object it copies
        static GameObject MakePart(string name, Transform parent, GameObject generatedObject)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.layer = generatedObject.layer;
            GameObjectUtility.SetStaticEditorFlags(part, GameObjectUtility.GetStaticEditorFlags(generatedObject));
            Undo.RegisterCreatedObjectUndo(part, kFinalizeUndoName);
            return part;
        }

        static void CopySettings(Component source, Component destination)
        {
            using var sourceObject      = new SerializedObject(source);
            using var destinationObject = new SerializedObject(destination);
            var property = sourceObject.GetIterator();
            var enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = false;
                if (kSkippedProperties.Contains(property.name))
                    continue;
                destinationObject.CopyFromSerializedProperty(property);
            }
            destinationObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Saves copies of <paramref name="sources"/> as one asset, the one the sculpture's meshes are in if it has
        /// one, else a new "Sculptures/&lt;Model&gt;.asset" beside the scene. Meshes already in it are overwritten in place,
        /// by name, so whatever else uses them keeps working.
        /// </summary>
        static Mesh[] SaveMeshes(ChiselModelComponent model, GameObject oldSculpture, List<Mesh> sources, List<string> names)
        {
            var path = FindMeshAssetPath(oldSculpture);
            if (sources.Count == 0)
            {
                if (path != null)
                    AssetDatabase.DeleteAsset(path);
                return new Mesh[0];
            }

            if (path == null)
            {
                var folder = Path.GetDirectoryName(model.gameObject.scene.path).Replace('\\', '/') + "/" + kMeshFolderName;
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), kMeshFolderName);
                path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{model.name}.asset");
            }

            var existing = new Dictionary<string, Mesh>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh)
                    existing[mesh.name] = mesh;
            }
            // The main asset can't be taken out of its file, so a main mesh that is no longer wanted means a new file
            var mainMesh = AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
            if (mainMesh && !names.Contains(mainMesh.name))
            {
                AssetDatabase.DeleteAsset(path);
                existing.Clear();
                mainMesh = null;
            }

            var meshes = new Mesh[sources.Count];
            for (int i = 0; i < sources.Count; i++)
            {
                if (existing.Remove(names[i], out var mesh))
                {
                    EditorUtility.CopySerialized(sources[i], mesh);
                    mesh.name = names[i];
                    EditorUtility.SetDirty(mesh);
                } else
                {
                    mesh = Object.Instantiate(sources[i]);
                    mesh.name      = names[i];
                    mesh.hideFlags = HideFlags.None;
                    if (!mainMesh)
                    {
                        AssetDatabase.CreateAsset(mesh, path);
                        mainMesh = mesh;
                    } else
                        AssetDatabase.AddObjectToAsset(mesh, path);
                }
                meshes[i] = mesh;
            }
            foreach (var leftover in existing.Values)
            {
                AssetDatabase.RemoveObjectFromAsset(leftover);
                Object.DestroyImmediate(leftover, allowDestroyingAssets: true);
            }
            AssetDatabase.SaveAssetIfDirty(mainMesh);
            return meshes;
        }

        static string FindMeshAssetPath(GameObject sculpture)
        {
            if (!sculpture)
                return null;
            foreach (var meshFilter in sculpture.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                var path = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            foreach (var meshCollider in sculpture.GetComponentsInChildren<MeshCollider>(includeInactive: true))
            {
                var path = AssetDatabase.GetAssetPath(meshCollider.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return null;
        }

        const string kFinalizeMenuName         = "CONTEXT/ChiselModelComponent/Finalize";
        const string kRebuildSculptureMenuName = "CONTEXT/ChiselModelComponent/Finalize, Force Rebuild";

        [MenuItem(kFinalizeMenuName)]
        static void FinalizeMenu(MenuCommand menuCommand) { FinalizeModel(menuCommand.context as ChiselModelComponent); }

        [MenuItem(kRebuildSculptureMenuName)]
        static void RebuildSculptureMenu(MenuCommand menuCommand) { FinalizeModel(menuCommand.context as ChiselModelComponent, rebuild: true); }

        [MenuItem(kFinalizeMenuName, true), MenuItem(kRebuildSculptureMenuName, true)]
        static bool ValidateFinalizeMenu(MenuCommand menuCommand) { return CanFinalize(menuCommand.context as ChiselModelComponent, out _); }

        [MenuItem("CONTEXT/ChiselModelComponent/Edit Model (Sculpture Off)")]
        static void EditModelMenu(MenuCommand menuCommand) { EditModel(menuCommand.context as ChiselModelComponent); }
    }
}
