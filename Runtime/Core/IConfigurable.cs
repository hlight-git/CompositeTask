namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Internal interface for applying config from Blueprint to TaskNode without reflection.
    /// Implemented explicitly by TaskNode&lt;TConfig&gt;.
    /// </summary>
    internal interface IConfigurable
    {
        void ApplyConfig(object config);
    }
}
