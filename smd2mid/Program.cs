using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace smd2mid
{
    class Program
    {
        static void Main(string[] args)
        {
            /*args = new string[1];
            args[0] = @"C:\Users\michael\Music\Pokemon\PMD\Rip\bgm0000.smd";*/

            foreach (string file in args)
            {
                if (!System.IO.File.Exists(file)) throw new Exception("Invalid input file: " + file);
    
                ProcyonSequenceLoader loader = new ProcyonSequenceLoader(file);
                Console.WriteLine("Loading Sequence: " + file);
                
                loader.LoadSequence();
                /*catch (Exception e)
                {
                    Console.WriteLine("FATAL ERROR: " + e.Message);
                    continue;
                }*/
                string outputfile = Path.Combine(
                        Path.GetDirectoryName(file), 
                        Path.GetFileNameWithoutExtension(file)
                        ) + ".mid";
                loader.WriteSequenceToFile(outputfile);
            }
        }
    }
}
