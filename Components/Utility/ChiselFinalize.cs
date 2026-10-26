using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Chisel.Components
{
    /// <summary>What a model remembers of its last Finalize, see <see cref="ChiselFinalize"/></summary>
    [Serializable]
    public sealed class ChiselFinalizedRecord
    {
        /// <summary>The sculpture, a plain-mesh copy of the model with a <see cref="ChiselSculptureComponent"/></summary>
        [FormerlySerializedAs("finalCopy")] public GameObject sculpture;
        /// <summary>The model's <see cref="ChiselFinalize.GetInputHash"/> when the sculpture was made; invalid before the first time</summary>
        public Hash128    inputHash;
        /// <summary>The model's tag before it was tagged EditorOnly, given back when it is edited again</summary>
        public string     tagBeforeFinalize;
    }

    /// <summary>Where a model stands with its sculpture, from the worst to the only one that is ready to ship</summary>
    public enum ChiselFinalizeStatus
    {
        /// <summary>Never finalized</summary>
        NotFinalized,
        /// <summary>Finalized, but its sculpture was deleted</summary>
        SculptureMissing,
        /// <summary>The model changed since its sculpture was made</summary>
        Changed,
        /// <summary>Unchanged, but the model is the one switched on (it is being edited), not its sculpture</summary>
        Editing,
        /// <summary>The sculpture is up to date and switched on, the model off and EditorOnly</summary>
        Finalized
    }

    /// <summary>
    /// Finalize turns a model into its sculpture: plain meshes saved as assets (MeshFilter / MeshRenderer / MeshCollider)
    /// on a copy of it. The model is switched off and tagged EditorOnly, so a build ships only the sculpture while the
    /// model stays editable. This half answers whether a model needs finalizing; the editor half (ChiselModelFinalizer)
    /// makes the sculptures.
    /// </summary>
    public static class ChiselFinalize
    {
        public const string kEditorOnlyTag = "EditorOnly";

#if UNITY_EDITOR
        /// <summary>The hash of everything the model's sculpture is made from (see <see cref="ChiselStagedInputHashes.GetFinalizeInputHash"/>)</summary>
        public static Hash128 GetInputHash(ChiselModelComponent model)
        {
            return ChiselStagedInputHashes.GetFinalizeInputHash(model, ChiselModelManager.SurfaceParameterIdentityMethod);
        }

        public static ChiselFinalizeStatus GetStatus(ChiselModelComponent model)
        {
            var record = model ? model.finalized : null;
            if (record == null || !record.inputHash.isValid)
                return ChiselFinalizeStatus.NotFinalized;
            if (!record.sculpture)
                return ChiselFinalizeStatus.SculptureMissing;
            if (GetInputHash(model) != record.inputHash)
                return ChiselFinalizeStatus.Changed;
            if (model.gameObject.activeSelf || !record.sculpture.activeSelf || !model.CompareTag(kEditorOnlyTag))
                return ChiselFinalizeStatus.Editing;
            return ChiselFinalizeStatus.Finalized;
        }

        /// <summary>Whether this model would ship anything but an up to date sculpture</summary>
        public static bool NeedsFinalize(ChiselModelComponent model)
        {
            return GetStatus(model) != ChiselFinalizeStatus.Finalized;
        }

        public static string GetStatusText(ChiselFinalizeStatus status)
        {
            return status switch
            {
                ChiselFinalizeStatus.NotFinalized     => "Not finalized: the model itself ships.",
                ChiselFinalizeStatus.SculptureMissing => "The sculpture was deleted: finalize again.",
                ChiselFinalizeStatus.Changed          => "Changed since the sculpture was made: finalize again.",
                ChiselFinalizeStatus.Editing          => "Being edited: the model is on and its sculpture off.",
                ChiselFinalizeStatus.Finalized        => "Finalized: the sculpture is up to date and ships instead of the model.",
                _                                     => status.ToString()
            };
        }
#endif
    }
}
