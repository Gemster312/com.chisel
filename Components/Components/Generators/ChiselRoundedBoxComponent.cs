using Chisel.Core;
using UnityEngine;

namespace Chisel.Components
{
    [ExecuteInEditMode, HelpURL(kDocumentationBaseURL + kNodeTypeName + kDocumentationExtension)]
    [DisallowMultipleComponent, AddComponentMenu("Chisel/" + kNodeTypeName)]
    public sealed class ChiselRoundedBoxComponent : ChiselBranchGeneratorComponent<Core.ChiselRoundedBox, ChiselRoundedBoxDefinition>
    {
        public const string kNodeTypeName = Core.ChiselRoundedBoxDefinition.kNodeTypeName;
        public override string ChiselNodeTypeName { get { return kNodeTypeName; } }
    }
}
