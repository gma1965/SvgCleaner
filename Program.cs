using System;

namespace SvgCleaner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("SVG optimizer for laser cutting.\n(c) Gerard Manintveld. All rights reserved.\n");
            if (args.Length == 0)
            {
                return;
            }
            try
            {
                new Optimizer().Start(args[0]);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.ReadLine();
            }
        }
    }
}
