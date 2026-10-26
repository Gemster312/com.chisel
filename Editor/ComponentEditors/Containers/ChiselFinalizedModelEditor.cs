using Chisel.Components;
using UnityEditor;
using UnityEngine;

namespace Chisel.Editors
{
    [CustomEditor(typeof(ChiselFinalizedModelComponent))]
    public sealed class ChiselFinalizedModelEditor : Editor
    {
        readonly static GUIContent kSourceContents        = new("Model", "The model this is a copy of. It is switched off and left out of builds.");
        readonly static GUIContent kEditModelContents     = new("Edit Model", "Switches the model back on and this copy off. Finalize again when done.");
        readonly static GUIContent kSelectModelContents   = new("Select Model");
        const string kNoSourceMessage = "The model this was copied from is gone, so it can no longer be edited or finalized again.";

        public override void OnInspectorGUI()
        {
            var source = ((ChiselFinalizedModelComponent)target).source;
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(kSourceContents, source, typeof(ChiselModelComponent), allowSceneObjects: true);

            if (!source)
            {
                EditorGUILayout.HelpBox(kNoSourceMessage, MessageType.Warning);
                return;
            }

            var status = ChiselFinalize.GetStatus(source);
            EditorGUILayout.HelpBox(ChiselFinalize.GetStatusText(status), (status == ChiselFinalizeStatus.Finalized) ? MessageType.Info : MessageType.Warning);
            if (targets.Length != 1)
                return;

            // Edit Model switches this copy off, so it waits until this inspector is drawn
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(kEditModelContents))
                    EditorApplication.delayCall += () => ChiselModelFinalizer.EditModel(source);
                if (GUILayout.Button(kSelectModelContents))
                    Selection.activeGameObject = source.gameObject;
            }
        }
    }
}
