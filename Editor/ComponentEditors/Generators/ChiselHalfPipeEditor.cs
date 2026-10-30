using Chisel.Core;
using Chisel.Components;
using UnityEditor;

namespace Chisel.Editors
{
    [CustomEditor(typeof(ChiselHalfPipeComponent))]
    public sealed class ChiselHalfPipeEditor : ChiselGeneratorDefinitionEditor<ChiselHalfPipeComponent, ChiselHalfPipeDefinition>
    {
        [MenuItem(kGameObjectMenuNodePath + ChiselHalfPipeComponent.kNodeTypeName, false, kGameObjectMenuNodePriority)]
        static void CreateAsGameObject(MenuCommand menuCommand) { CreateAsGameObjectMenuCommand(menuCommand, ChiselHalfPipeComponent.kNodeTypeName); }
    }
}
