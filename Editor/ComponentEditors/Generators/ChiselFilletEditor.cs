using Chisel.Core;
using Chisel.Components;
using UnityEditor;

namespace Chisel.Editors
{
    [CustomEditor(typeof(ChiselFilletComponent))]
    public sealed class ChiselFilletEditor : ChiselGeneratorDefinitionEditor<ChiselFilletComponent, ChiselFilletDefinition>
    {
        [MenuItem(kGameObjectMenuNodePath + ChiselFilletComponent.kNodeTypeName, false, kGameObjectMenuNodePriority)]
        static void CreateAsGameObject(MenuCommand menuCommand) { CreateAsGameObjectMenuCommand(menuCommand, ChiselFilletComponent.kNodeTypeName); }
    }
}
