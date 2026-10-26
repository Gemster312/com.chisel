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
    /// The editor half of Finalize (Copy) (see <see cref="ChiselFinalize"/>): makes a model's final copy, a sibling
    /// GameObject "&lt;Model&gt; (Final)" holding the model's generated meshes as plain MeshFilter / MeshRenderer /
    /// MeshCollider components, with the meshes saved as one asset in a "Finalized" folder beside the scene. The model
    /// is then switched off and tagged EditorOnly; <see cref="EditModel"/> switches back to it.
    /// </summary>
    public static class ChiselModelFinalizer
    {
        public const string kFinalCopySuffix = " (Final)";
        public const string kMeshFolderName  = "Finalized";
        const string kUntaggedTag            = "Untagged";
        const string kFinalizeUndoName       = "Finalize (Copy)";
        const string kEditUndoName           = "Edit Model";

        // Properties that tie a component to its own object or prefab, never copied from the generated components
        static readonly HashSet<string> kSkippedProperties = new()
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Enabled", "m_Script", "m_EditorHideFlags", "m_EditorClassIdentifier"
        };

        /// <summary>Whether Finalize (Copy) can run on this model, and if not, why</summary>
        public static bool CanFinalize(ChiselModelComponent model, out string reason)
        {
            reason = null;
            if (!model)
                reason = "the model is missing.";
            else if (EditorApplication.isPlayingOrWillChangePlaymode)
                reason = "it can't be done in Play mode.";
            else if (model.IsDefaultModel)
                reason = "the default model can't be finalized. Move its brushes into a model first.";
            else if (string.IsNullOrEmpty(model.gameObject.scene.path))
                reason = "its scene has never been saved, so there is no folder to save the meshes in.";
            else if (model.transform.parent && !model.transform.parent.gameObject.activeInHierarchy)
                reason = "its parent is switched off, so it can't build its meshes.";
            return reason == null;
        }

        /// <summary>
        /// Makes the model's final copy and switches to it. When the copy is already up to date it only switches, which
        /// keeps the copy's baked lighting; <paramref name="rebuild"/> makes it again anyway. Returns whether the model
        /// ended up finalized.
        /// </summary>
        public static bool FinalizeCopy(ChiselModelComponent model, bool rebuild = false)
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
                if (rebuild || record == null || !record.finalCopy || record.inputHash != ChiselFinalize.GetInputHash(model))
                {
                    if (!MakeFinalCopy(model))
                        return false;
                }
                SwitchToFinalCopy(model);
                return true;
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }
        }

        public static void FinalizeCopy(IEnumerable<ChiselModelComponent> models, bool rebuild = false)
        {
            foreach (var model in models)
                FinalizeCopy(model, rebuild);
        }

        /// <summary>Switches the model back on, and its final copy off, so it can be edited. Finalize again when done.</summary>
        public static void EditModel(ChiselModelComponent model)
        {
            if (!model)
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(kEditUndoName);
            var undoGroup = Undo.GetCurrentGroup();

            var gameObject = model.gameObject;
            var record     = model.finalized;
            Undo.RecordObject(gameObject, kEditUndoName);
            if (gameObject.CompareTag(ChiselFinalize.kEditorOnlyTag))
                gameObject.tag = (record == null || string.IsNullOrEmpty(record.tagBeforeFinalize)) ? kUntaggedTag : record.tagBeforeFinalize;
            gameObject.SetActive(true);
            if (record != null && record.finalCopy)
            {
                Undo.RecordObject(record.finalCopy, kEditUndoName);
                record.finalCopy.SetActive(false);
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

            if (ChiselModelManager.Instance.IsSelectable(model))
                ChiselModelManager.Instance.ActiveModel = model;

            Undo.CollapseUndoOperations(undoGroup);
        }

        public static void EditModel(IEnumerable<ChiselModelComponent> models)
        {
            foreach (var model in models)
                EditModel(model);
        }

        static void SwitchToFinalCopy(ChiselModelComponent model)
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

            Undo.RecordObject(record.finalCopy, kFinalizeUndoName);
            record.finalCopy.SetActive(true);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        static bool MakeFinalCopy(ChiselModelComponent model)
        {
            var gameObject = model.gameObject;
            var scene      = gameObject.scene;

            // The meshes are built while the model is on, and their lightmap UVs are laid out for the active scene's
            // lighting settings, which have to be the model's own scene's
            if (!gameObject.activeSelf)
            {
                Undo.RecordObject(gameObject, kFinalizeUndoName);
                gameObject.SetActive(true);
            }
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
            var meshes = SaveMeshes(model, (record != null) ? record.finalCopy : null, sources, names);

            var copy = MakeOrClearCopy(model);
            var copyTransform = copy.transform;
            int meshIndex = 0;
            foreach (var renderable in renderables)
            {
                var part = MakePart(names[meshIndex], copyTransform, renderable.container);
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
                var part = MakePart("Collider", copyTransform, generated.colliderContainer);
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
            model.finalized.finalCopy = copy;
            model.finalized.inputHash = ChiselFinalize.GetInputHash(model);
            EditorUtility.SetDirty(model);
            return true;
        }

        /// <summary>The copy, made beside the model when it has none, otherwise emptied, placed and flagged like the model</summary>
        static GameObject MakeOrClearCopy(ChiselModelComponent model)
        {
            var modelObject    = model.gameObject;
            var modelTransform = model.transform;
            var record         = model.finalized;
            var copy           = (record != null) ? record.finalCopy : null;
            if (!copy)
            {
                // Made in the active scene, which MakeFinalCopy made the model's
                copy = new GameObject(model.name + kFinalCopySuffix);
                copy.SetActive(false);
                copy.transform.SetParent(modelTransform.parent, false);
                copy.transform.SetSiblingIndex(modelTransform.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(copy, kFinalizeUndoName);
            } else
            {
                var children = copy.transform;
                for (int i = children.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(children.GetChild(i).gameObject);
                Undo.RecordObject(copy, kFinalizeUndoName);
                Undo.RecordObject(copy.transform, kFinalizeUndoName);
                if (copy.transform.parent != modelTransform.parent)
                    Undo.SetTransformParent(copy.transform, modelTransform.parent, kFinalizeUndoName);
            }

            var copyTransform = copy.transform;
            copyTransform.SetLocalPositionAndRotation(modelTransform.localPosition, modelTransform.localRotation);
            copyTransform.localScale = modelTransform.localScale;
            copy.layer = modelObject.layer;
            copy.tag   = !modelObject.CompareTag(ChiselFinalize.kEditorOnlyTag) ? modelObject.tag :
                         (record == null || string.IsNullOrEmpty(record.tagBeforeFinalize)) ? kUntaggedTag : record.tagBeforeFinalize;
            GameObjectUtility.SetStaticEditorFlags(copy, GameObjectUtility.GetStaticEditorFlags(modelObject));

            if (!copy.TryGetComponent<ChiselFinalizedModelComponent>(out var marker))
                marker = Undo.AddComponent<ChiselFinalizedModelComponent>(copy);
            Undo.RecordObject(marker, kFinalizeUndoName);
            marker.source = model;
            return copy;
        }

        // A child of the copy at its origin, with the layer and static flags Chisel gave the generated object it copies
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
        /// Saves copies of <paramref name="sources"/> as one asset, the one the final copy's meshes are in if it has
        /// one, else a new "Finalized/&lt;Model&gt;.asset" beside the scene. Meshes already in it are overwritten in place,
        /// by name, so whatever else uses them keeps working.
        /// </summary>
        static Mesh[] SaveMeshes(ChiselModelComponent model, GameObject oldCopy, List<Mesh> sources, List<string> names)
        {
            var path = FindMeshAssetPath(oldCopy);
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

        static string FindMeshAssetPath(GameObject copy)
        {
            if (!copy)
                return null;
            foreach (var meshFilter in copy.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                var path = AssetDatabase.GetAssetPath(meshFilter.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            foreach (var meshCollider in copy.GetComponentsInChildren<MeshCollider>(includeInactive: true))
            {
                var path = AssetDatabase.GetAssetPath(meshCollider.sharedMesh);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return null;
        }

        [MenuItem("CONTEXT/ChiselModelComponent/Finalize (Copy)")]
        static void FinalizeCopyMenu(MenuCommand menuCommand) { FinalizeCopy(menuCommand.context as ChiselModelComponent); }

        [MenuItem("CONTEXT/ChiselModelComponent/Finalize (Copy), Force Rebuild")]
        static void RebuildFinalCopyMenu(MenuCommand menuCommand) { FinalizeCopy(menuCommand.context as ChiselModelComponent, rebuild: true); }

        [MenuItem("CONTEXT/ChiselModelComponent/Edit Model (Final Copy Off)")]
        static void EditModelMenu(MenuCommand menuCommand) { EditModel(menuCommand.context as ChiselModelComponent); }
    }
}
