using System;
using System.Threading.Tasks;

namespace CorruptedCasino
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await Casino.Bootstrap();
            Casino.Instance.RunActualTest();
            Console.ReadLine();
        }
    }
}
