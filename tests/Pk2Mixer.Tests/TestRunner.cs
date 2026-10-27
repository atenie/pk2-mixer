namespace Pk2Mixer.Tests;

internal static class TestRunner
{
    public static void Test(string name, Action body)
    {
        try
        {
            body();
            Program.Passed++;
            Console.WriteLine("ok   " + name);
        }
        catch (Exception ex)
        {
            Program.Failures++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    public static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pk2-mixer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
