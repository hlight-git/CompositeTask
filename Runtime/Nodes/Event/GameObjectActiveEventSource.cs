namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>Fires when this GameObject becomes active.</summary>
    public class GameObjectActiveEventSource : TaskEventSource
    {
        private void OnEnable() => Fire();
    }
}
