namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface IDependencyInjectionVisitor
    {
        void Visit<T>(T target);
    }
}