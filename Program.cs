namespace _2DPatterns
{
    class Program
    {
        static void Main(string[] args)
        {
            using var patterns = new Patterns(args[0]);
            patterns.Recognize();
        }
    }
}
