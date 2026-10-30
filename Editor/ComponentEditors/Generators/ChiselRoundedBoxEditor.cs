using Chisel.Core;
using Chisel.Components;
using UnityEditor;

namespace Chisel.Editors
{
    [CustomEditor(typeof(ChiselRoundedBoxComponent))]
    public sealed class ChiselRoundedBoxEditor : ChiselGeneratorDefinitionEditor<ChiselRoundedBoxComponent, ChiselRoundedBoxDefinition>
    {
        [MenuItem(kGameObjectMenuNodePath + ChiselRoundedBoxComponent.kNodeTypeName, false, kGameObjectMenuNodePriority)]
        static void CreateAsGameObject(MenuCommand menuCommand) { CreateAsGameObjectMenuCommand(menuCommand, ChiselRoundedBoxComponent.kNodeTypeName); }
    }
}
