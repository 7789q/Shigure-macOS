namespace Shigure;

public static class PixelProtocolDecoder
{
    private const int TopRowBlockCount = 510;
    private const int TopRowFirstSchemeMax = 255;
    private const int HealAbsorbMaxUnits = 30;

    public static Dictionary<int, int> DecodeTopRow(ReadOnlySpan<int> pixels)
    {
        var startX = FindTopRowStart(pixels);

        if (startX < 0)
        {
            return [];
        }

        var rowData = new Dictionary<int, int>(Math.Min(TopRowBlockCount, pixels.Length - startX));
        for (var x = startX; x < pixels.Length; x++)
        {
            if (TryDecodeTopRowBlock(pixels[x], out var step, out var value))
            {
                rowData[step] = value;
                if (step == TopRowBlockCount)
                {
                    break;
                }
            }
        }

        return rowData;
    }

    internal static int FindTopRowStart(ReadOnlySpan<int> pixels)
    {
        for (var x = 0; x < Math.Min(TopRowBlockCount, pixels.Length); x++)
        {
            if (TryDecodeTopRowBlock(pixels[x], out var step, out _) && step == 1)
            {
                return x;
            }
        }

        return -1;
    }

    public static int? FindCountBarsMarkerY(ReadOnlySpan<int> pixels)
    {
        for (var y = 0; y < pixels.Length; y++)
        {
            if (IsRedMarker(pixels[y]))
            {
                return y;
            }
        }

        return null;
    }

    public static Dictionary<int, int> DecodeCountBars(ReadOnlySpan<int> row)
    {
        var barData = new Dictionary<int, int>();
        var segIndex = 0;
        var x = 0;
        var pendingRed = false;

        while (x < row.Length)
        {
            var color = row[x];
            if (IsGrayEndMarker(color))
            {
                break;
            }

            if (pendingRed && IsRedGreenMarker(color))
            {
                pendingRed = false;
                segIndex++;
                var (value, nextX) = ConsumeValueFrom(row, x + 1, alreadySawWhite: false);
                barData[segIndex] = Math.Max(0, value - 1);
                x = nextX;
                continue;
            }

            if (IsRedMarker(color))
            {
                pendingRed = true;
                x++;
                continue;
            }

            if (IsWhite(color))
            {
                var prevWhite = x > 0 && IsWhite(row[x - 1]);
                if (!prevWhite)
                {
                    pendingRed = false;
                    segIndex++;
                    var (value, nextX) = ConsumeValueFrom(row, x + 1, alreadySawWhite: true);
                    barData[segIndex] = Math.Max(0, value - 1);
                    x = nextX;
                    continue;
                }
            }

            x++;
        }

        return barData;
    }

    public static void DecodeHealAbsorbRow(
        ReadOnlySpan<int> row,
        IDictionary<int, int> destination)
    {
        var x = 0;
        while (x < row.Length)
        {
            var color = row[x];
            if (!IsWhite(color))
            {
                x++;
                continue;
            }

            var prevWhite = x > 0 && IsWhite(row[x - 1]);
            if (prevWhite)
            {
                x++;
                continue;
            }

            var (green, blue, nextX) = ConsumeHealAbsorbPixel(row, x + 1);
            if (blue is >= 1 and <= HealAbsorbMaxUnits)
            {
                destination[blue] = Math.Max(0, green - 1);
            }

            x = nextX;
        }
    }

    private static (int Value, int NextX) ConsumeValueFrom(
        ReadOnlySpan<int> row,
        int fromX,
        bool alreadySawWhite)
    {
        var sx = fromX;
        var needWhite = !alreadySawWhite;
        while (sx < row.Length)
        {
            var color = row[sx];
            if (IsGrayEndMarker(color))
            {
                return (0, row.Length);
            }

            if (IsRedMarker(color))
            {
                return (0, sx);
            }

            if (needWhite)
            {
                if (IsWhite(color))
                {
                    needWhite = false;
                }

                sx++;
                continue;
            }

            if (IsWhite(color))
            {
                sx++;
                continue;
            }

            return (Green(color), sx + 1);
        }

        return (0, row.Length);
    }

    private static (int Green, int Blue, int NextX) ConsumeHealAbsorbPixel(
        ReadOnlySpan<int> row,
        int fromX)
    {
        var sx = fromX;
        while (sx < row.Length)
        {
            var color = row[sx];
            if (IsWhite(color))
            {
                sx++;
                continue;
            }

            return (Green(color), Blue(color), sx + 1);
        }

        return (0, 0, row.Length);
    }

    private static int Red(int color) => (color >> 16) & 0xFF;

    private static int Green(int color) => (color >> 8) & 0xFF;

    private static int Blue(int color) => color & 0xFF;

    private static bool IsRedMarker(int color) =>
        Red(color) == 1 && Green(color) == 0 && Blue(color) == 0;

    private static bool IsRedGreenMarker(int color) =>
        Red(color) == 1 && Green(color) == 1 && Blue(color) == 0;

    private static bool IsWhite(int color) =>
        Red(color) == 255 && Green(color) == 255 && Blue(color) == 255;

    private static bool IsGrayEndMarker(int color) =>
        Red(color) == 200 && Green(color) == 200 && Blue(color) == 200;

    private static bool TryDecodeTopRowBlock(int color, out int step, out int value)
    {
        step = 0;
        value = 0;

        var green = Green(color);
        if (green is < 1 or > TopRowFirstSchemeMax)
        {
            return false;
        }

        step = Red(color) switch
        {
            0 => green,
            1 => TopRowFirstSchemeMax + green,
            _ => 0
        };

        if (step is < 1 or > TopRowBlockCount)
        {
            step = 0;
            return false;
        }

        value = Blue(color);
        return true;
    }
}
