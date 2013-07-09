using System;
using System.Collections.Generic;
using System.Text;

namespace GitIrcBot
{
    class Program
    {
        static void Main(string[] args)
        {
            GitIrcBot bot = null;

            try
            {
                bot = new GitIrcBot();
                bot.Run();
            }
#if !DEBUG
            catch (Exception ex)
            {
                Console.WriteLine("Fatal error: " + ex.Message);
                Environment.ExitCode = 1;
            }
#endif
            finally
            {
                if (bot != null)
                    bot.Dispose();
            }
        }
    }
}
