namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface IDependencyInjectionVisitable
    {
        void Accept(IDependencyInjectionVisitor visitor);
    }
}