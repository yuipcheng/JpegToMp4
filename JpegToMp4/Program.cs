using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace JpegToMp4
{

    /// <summary>
    /// reads jpeg and outputs mp4 and then moves jpeg to a bkup folder.
    /// mp4 file is named by date.
    /// 
    /// assumes jpeg file names are in the following format:
    ///     C4D655370473(CAM_01)_1_20150603183804_3969.jpg
    /// 
    /// Usages:
    /// 3 parameters - src, target, backup
    ///     reads jpeg in src folder and outputs mp4s in target folder,
    ///     then jpeg are moved to backup folder.
    /// </summary>
    /// <remarks>
    /// To debug the executable, launch it with "debug" as an argument and then attach to debugger.
    /// </remarks>
    class Program
    {
        static void Main(string[] args)
        {
            if (args.ToList().IndexOf("debug") >= 0)
            {
                var pams = args.ToList();
                pams.Remove("debug");
                args = pams.ToArray();
                Console.WriteLine("Attach debugger now, then press enter.");
                Console.ReadLine();
            }

            var jpeg = ConfigurationManager.AppSettings["JpegToMp4Lib_Jpeg"];
            var watcher = new FileSystemWatcher(jpeg, "*.jpg");
            watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
           | NotifyFilters.FileName | NotifyFilters.DirectoryName;


            watcher.Created += new FileSystemEventHandler(Watcher_Created);
            watcher.EnableRaisingEvents = true;

            Console.WriteLine("Press 'q' to quit monitoring " + jpeg);
            while (Console.Read() != 'q')
            {
            }
            Console.WriteLine("Stopped monitoring " + jpeg);
        }

        private static void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            var jpegToMp4 = new JpegToMp4Lib.JpegToMp4Lib();

            jpegToMp4.Start();
        }
    }
}
