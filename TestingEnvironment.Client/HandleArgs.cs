using System;

namespace TestingEnvironment.Client
{
    public class HandleArgs<T>
    {
        public T ProcessArgs(string[] args, string helpText)
        {
            var instance = Activator.CreateInstance<T>();
            var members = typeof(T).GetMembers();
            foreach (var arg in args)
            {
                if (arg == "-h" || arg == "--help")
                {
                    Console.WriteLine(helpText);
                    Environment.Exit(0);
                }

                var split = arg.Split("--");
                if (split.Length != 2)
                {
                    Console.WriteLine($"Invalid argument {arg}. Needed format : '--option=value'");
                    Environment.Exit(1);
                }

                var option = split[1].Split("=");
                if (option.Length != 2)
                {
                    Console.WriteLine($"Invalid argument {arg}. Needed format : '--option=value'");
                    Environment.Exit(1);
                }

                bool found = false;
                foreach (var member in members)
                {
                    if (member.Name.ToLower().Equals(option[0].ToLower()))
                     {
                        found = true;
                        try
                        {
                            var prop = typeof(T).GetProperty(member.Name);
                            prop.SetValue(instance, option[1]);
                        }
                        catch (Exception)
                        {
                            Console.WriteLine($"Invalid value passed to argument {option[0]}");
                            Environment.Exit(1);
                        }
                        break;
                    }
                }

                if (found == false)
                {
                    Console.WriteLine($"Invalid argument {option[0]}");
                    Environment.Exit(1);
                }
            }

            return instance;
        }
    }
}