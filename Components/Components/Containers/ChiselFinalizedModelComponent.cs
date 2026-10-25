using UnityEngine;

namespace Chisel.Components
{
    /// <summary>
    /// Marks the plain-mesh copy that Finalize (Copy) made of a model (see <see cref="ChiselFinalize"/>). The copy is
    /// what ships; the model it was made from stays in the scene, inactive and tagged EditorOnly, so it can be edited
    /// and finalized again.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("")]
    [Icon(ChiselNodeComponent.kIconBasePath + "csg_model" + ChiselNodeComponent.kIconExtension)]
    public sealed class ChiselFinalizedModelComponent : MonoBehaviour
    {
        /// <summary>The model this is a copy of. Always null in a build, which leaves the model out.</summary>
        public ChiselModelComponent source;
    }
}
