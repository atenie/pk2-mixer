using System.Text;

namespace Pk2Mixer.Tests;

internal static class Program
{
    internal static int Failures;
    internal static int Passed;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("pk2-mixer core tests");
        Console.WriteLine("--------------------");

        CoreTests.Run();
        PaletteTests.Run();
        WorkflowTests.Run();
        ResolverTests.Run();
        RetailTests.Run();

        Console.WriteLine("--------------------");
        Console.WriteLine($"{Passed} passed, {Failures} failed");
        return Failures == 0 ? 0 : 1;
    }
}

internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"{what}: expected {expected}, got {actual}");
        }
    }

    public static void Throws<T>(Action action, string what) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        throw new Exception($"{what}: expected {typeof(T).Name}, nothing was thrown");
    }
}
