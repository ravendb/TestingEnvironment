namespace CorruptedCasino
{
    class Program
    {
        static void Main(string[] args)
        {
            using (var client = new CasinoTest(args[0], "CorruptedCasinoTest"))
            {
                client.Initialize();
                client.RunTest();
            }            
        }
    }
}
