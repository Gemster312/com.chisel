using UnityEngine;

namespace Chisel.Components
{
    /// <summary>
    /// Marks a model's sculpture: the plain-mesh copy that Finalize made of it (see <see cref="ChiselFinalize"/>). The
    /// sculpture is what ships; the model it was made from stays in the scene, inactive and tagged EditorOnly, so it can
    /// be edited and finalized again.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("")]
    [Icon(ChiselNodeComponent.kIconBasePath + "csg_model" + ChiselNodeComponent.kIconExtension)]
    public sealed class ChiselSculptureComponent : MonoBehaviour
    {
        /// <summary>The model this is a sculpture of. Always null in a build, which leaves the model out.</summary>
        public ChiselModelComponent source;
    }
}
