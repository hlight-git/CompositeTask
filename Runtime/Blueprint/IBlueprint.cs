namespace Hlight.Structures.CompositeTask.Runtime.Blueprint
{
    /// <summary>
    /// Marker interface for all blueprint nodes (Type 1 — pure data, no execution logic).
    /// </summary>
    public interface IBlueprint
    {
        string Name { get; set; }
        bool Enabled { get; set; }
        float Weight { get; set; }
    }
}
