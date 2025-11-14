using System;
using System.Threading;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            var startedFile = System.IO.Path.Combine(AppContext.BaseDirectory, "started.txt");
            System.IO.File.WriteAllText(startedFile, DateTime.UtcNow.ToString("o"));
            // run until killed
            while (true)
            {
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            var err = System.IO.Path.Combine(AppContext.BaseDirectory, "error.txt");
            System.IO.File.WriteAllText(err, ex.ToString());
            return 1;
        }
    }
}
