using Chisel.Core;
using UnityEngine;

namespace Chisel.Components
{
    [ExecuteInEditMode, HelpURL(kDocumentationBaseURL + kNodeTypeName + kDocumentationExtension)]
    [DisallowMultipleComponent, AddComponentMenu("Chisel/" + kNodeTypeName)]
    public sealed class ChiselFilletComponent : ChiselBranchGeneratorComponent<Core.ChiselFillet, ChiselFilletDefinition>
    {
        public const string kNodeTypeName = Core.ChiselFilletDefinition.kNodeTypeName;
        public override string ChiselNodeTypeName { get { return kNodeTypeName; } }
    }
}
