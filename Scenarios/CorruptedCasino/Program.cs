using System;
using System.Threading.Tasks;

namespace CorruptedCasino
{
    class Program
    {
        static async Task Main()
        {
            await Casino.Bootstrap().ConfigureAwait(false);
            Casino.Instance.RunActualTest();
            Console.ReadLine();
        }
    }
}
