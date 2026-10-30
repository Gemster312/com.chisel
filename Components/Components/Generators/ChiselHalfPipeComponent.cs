using Chisel.Core;
using UnityEngine;

namespace Chisel.Components
{
    [ExecuteInEditMode, HelpURL(kDocumentationBaseURL + kNodeTypeName + kDocumentationExtension)]
    [DisallowMultipleComponent, AddComponentMenu("Chisel/" + kNodeTypeName)]
    public sealed class ChiselHalfPipeComponent : ChiselBranchGeneratorComponent<Core.ChiselHalfPipe, ChiselHalfPipeDefinition>
    {
        public const string kNodeTypeName = Core.ChiselHalfPipeDefinition.kNodeTypeName;
        public override string ChiselNodeTypeName { get { return kNodeTypeName; } }
    }
}
