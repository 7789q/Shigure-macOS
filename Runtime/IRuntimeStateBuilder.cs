namespace Shigure;

public interface IRuntimeStateBuilder
{
    GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null);
}
