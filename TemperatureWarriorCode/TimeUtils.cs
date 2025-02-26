using System.Diagnostics;

public static class TimeUtils
{
    private static Stopwatch stopwatch = new Stopwatch();

    static TimeUtils()
    {
        stopwatch.Start();
    }

    // Imitar arduino millis()
    public static long millis()
    {
        return stopwatch.ElapsedMilliseconds;
    }
}