namespace Shigure;

public interface IShigureRuntimeFactory
{
    ShigureRuntime Create(AppOptions options);
}
