using System;
using UnityEngine;

namespace Chisel.Components
{
    /// <summary>What a model remembers of its last Finalize (Copy), see <see cref="ChiselFinalize"/></summary>
    [Serializable]
    public sealed class ChiselFinalizedRecord
    {
        /// <summary>The plain-mesh copy, a sibling of the model with a <see cref="ChiselFinalizedModelComponent"/></summary>
        public GameObject finalCopy;
        /// <summary>The model's <see cref="ChiselFinalize.GetInputHash"/> when the copy was made; invalid before the first time</summary>
        public Hash128    inputHash;
        /// <summary>The model's tag before it was tagged EditorOnly, given back when it is edited again</summary>
        public string     tagBeforeFinalize;
    }

    /// <summary>Where a model stands with its final copy, from the worst to the only one that is ready to ship</summary>
    public enum ChiselFinalizeStatus
    {
        /// <summary>Never finalized</summary>
        NotFinalized,
        /// <summary>Finalized, but its final copy was deleted</summary>
        FinalCopyMissing,
        /// <summary>The model changed since its final copy was made</summary>
        Changed,
        /// <summary>Unchanged, but the model is the one switched on (it is being edited), not its final copy</summary>
        Editing,
        /// <summary>The final copy is up to date and switched on, the model off and EditorOnly</summary>
        Finalized
    }

    /// <summary>
    /// Finalize (Copy) turns a model into plain meshes saved as assets (MeshFilter / MeshRenderer / MeshCollider) on a
    /// copy beside it, and switches the model off and tags it EditorOnly, so a build ships only the copy while the model
    /// stays editable. This half answers whether a model needs finalizing; the editor half (ChiselModelFinalizer) makes
    /// the copies.
    /// </summary>
    public static class ChiselFinalize
    {
        public const string kEditorOnlyTag = "EditorOnly";

#if UNITY_EDITOR
        /// <summary>The hash of everything the model's final copy is made from (see <see cref="ChiselStagedInputHashes.GetFinalizeInputHash"/>)</summary>
        public static Hash128 GetInputHash(ChiselModelComponent model)
        {
            return ChiselStagedInputHashes.GetFinalizeInputHash(model, ChiselModelManager.SurfaceParameterIdentityMethod);
        }

        public static ChiselFinalizeStatus GetStatus(ChiselModelComponent model)
        {
            var record = model ? model.finalized : null;
            if (record == null || !record.inputHash.isValid)
                return ChiselFinalizeStatus.NotFinalized;
            if (!record.finalCopy)
                return ChiselFinalizeStatus.FinalCopyMissing;
            if (GetInputHash(model) != record.inputHash)
                return ChiselFinalizeStatus.Changed;
            if (model.gameObject.activeSelf || !record.finalCopy.activeSelf || !model.CompareTag(kEditorOnlyTag))
                return ChiselFinalizeStatus.Editing;
            return ChiselFinalizeStatus.Finalized;
        }

        /// <summary>Whether this model would ship anything but an up to date final copy</summary>
        public static bool NeedsFinalize(ChiselModelComponent model)
        {
            return GetStatus(model) != ChiselFinalizeStatus.Finalized;
        }

        public static string GetStatusText(ChiselFinalizeStatus status)
        {
            return status switch
            {
                ChiselFinalizeStatus.NotFinalized     => "Not finalized: the model itself ships.",
                ChiselFinalizeStatus.FinalCopyMissing => "The final copy was deleted: finalize again.",
                ChiselFinalizeStatus.Changed          => "Changed since the final copy was made: finalize again.",
                ChiselFinalizeStatus.Editing          => "Being edited: the model is on and its final copy off.",
                ChiselFinalizeStatus.Finalized        => "Finalized: the final copy is up to date and ships instead of the model.",
                _                                     => status.ToString()
            };
        }
#endif
    }
}
