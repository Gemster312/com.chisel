using Chisel.Components;
using UnityEditor;
using UnityEngine;

namespace Chisel.Editors
{
    /// <summary>
    /// Keeps a finalized model where its sculpture is: whenever a sculpture is moved, rotated, scaled or reparented
    /// (by hand, by script or by undo), its model follows (see <see cref="ChiselModelFinalizer.FollowSculpture"/>).
    /// </summary>
    [InitializeOnLoad]
    static class ChiselSculptureFollower
    {
        static ChiselSculptureFollower()
        {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
        }

        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                EntityId entityId;
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var properties);
                        entityId = properties.entityId;
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out var parent);
                        entityId = parent.entityId;
                        break;
                    default:
                        continue;
                }

                var changed    = EditorUtility.EntityIdToObject(entityId);
                var gameObject = changed as GameObject;
                if (!gameObject && changed is Component component)
                    gameObject = component.gameObject;
                if (gameObject && gameObject.TryGetComponent<ChiselSculptureComponent>(out var sculpture))
                    ChiselModelFinalizer.FollowSculpture(sculpture);
            }
        }
    }
}
