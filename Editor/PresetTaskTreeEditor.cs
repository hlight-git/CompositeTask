using System.Collections.Generic;
using Hlight.Structures.CompositeTask.Runtime;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;
using UnityEditor;

namespace Hlight.Structures.CompositeTask.Editor
{
    [CustomEditor(typeof(PresetTaskTree))]
    public class PresetTaskTreeEditor : TaskTreeEditor
    {
        protected override IReadOnlyList<TaskNodePreset> GetPresets(TaskTree tree)
            => ((PresetTaskTree)tree).NodePresets;
    }
}
