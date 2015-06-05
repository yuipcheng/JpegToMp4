using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// 1. no parameters
    ///     reads jpeg in current folder and outputs mp4s in current folder
    ///     
    /// 2. 1 parameter - src
    ///     reads jpeg in src folder and outputs mp4s in current folder
    ///     
    /// 3. 2 parameters - src, target
    ///     reads jpeg in src folder and outputs mp4s in target folder
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

            var pics = GetPictures(args);

            if (pics.Keys.Count > 0)
            {
                JoinPicturesIntoMovie(pics);
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        /// <summary>
        /// outputs a dictionary with:
        /// - key: mp4 file path
        /// - value: list of jpeg files
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static Dictionary<string, List<FileInfo>> GetPictures(string[] args)
        {
            var ret = new Dictionary<string, List<FileInfo>>();

            try
            {
                DirectoryInfo src = null;
                DirectoryInfo target = null;

                if (args == null || args.Length == 0)
                {
                    src = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
                    target = src;
                }
                else
                {
                    src = new DirectoryInfo(args[0]);

                    if (args.Length == 1)
                        target = src;
                    else
                        target = new DirectoryInfo(args[1]);
                }

                var files = src.GetFiles("*.jpg").OrderBy(o => o.FullName).ToList();

                foreach (var file in files)
                {
                    // eg C4D655370473(CAM_01)_1_20150603183804_3969.jpg
                    var tokens = file.Name.Split("_".ToCharArray());
                    var videoFileName = tokens[0] + "_" + tokens[1] + "_" + tokens[2] + "_" + tokens[3].Substring(0, 8) + ".mp4";
                    var key = new FileInfo(Path.Combine(target.FullName, videoFileName)).FullName;

                    if (ret.ContainsKey(key) == false)
                        ret[key] = new List<FileInfo>();

                    ret[key].Add(file);
                }
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.Message);
                Console.WriteLine(exc.StackTrace);
                throw;
            }

            return ret;
        }


        public static void JoinPicturesIntoMovie(Dictionary<string, List<FileInfo>> makeFile)
        {
            var rator = makeFile.GetEnumerator();

            while (rator.MoveNext())
            {
                var videoFile = rator.Current.Key;
                var pictureFiles = rator.Current.Value;

                if (pictureFiles.Count > 0)
                {
                    // dimension of first picture will be used for the dimension of the entire movie.
                    using (var writer = new AForge.Video.FFMPEG.VideoFileWriter())
                    {
                        var firstPicture = new Bitmap(pictureFiles[0].FullName);

                        writer.Open(videoFile, firstPicture.Width, firstPicture.Height, 3, AForge.Video.FFMPEG.VideoCodec.MPEG4);

                        for (int i = 0; i < pictureFiles.Count; i++)
                        {
                            var fi = pictureFiles[i];

                            if (fi.Length > 0)
                            {
                                var bmp = new Bitmap(fi.FullName);

                                AddText(ref bmp, fi.Name);
                                writer.WriteVideoFrame(bmp);

                                Console.WriteLine(fi.Name + " > " + new FileInfo(videoFile).Name + " remaining: " + (pictureFiles.Count - i).ToString());
                            }
                        }
                    }
                }
            }

            Backup(makeFile);
        }

        public static void AddText(ref Bitmap bmp, string text)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                var rectf = new RectangleF(0, 0, bmp.Width / 2, 20);
                g.FillRectangle(Brushes.White, rectf.X, rectf.Y, rectf.Width, rectf.Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                g.DrawString(text, new Font("Tahoma", 8), Brushes.Black, rectf.X + 5, rectf.Y + (float)2.5);
                g.Flush();
            }
        }

        public static void Backup(Dictionary<string, List<FileInfo>> makeFile)
        {
            foreach (var key in makeFile.Keys)
            {
                foreach (var file in makeFile[key])
                {
                    var bkupFolder = new DirectoryInfo(Path.Combine(file.Directory.FullName, "bkup"));

                    if (bkupFolder.Exists == false)
                        bkupFolder.Create();

                    var bkup = new FileInfo(Path.Combine(bkupFolder.FullName, file.Name)).FullName;

                    try
                    {
                        file.MoveTo(bkup);
                    }
                    catch
                    {
                        var log = new FileInfo(key + ".log");
                        var txt = "Failed " + file.FullName + " => " + bkup;

                        if (log.Exists == false)
                            log.Create().Close();

                        using (var sr = log.AppendText())
                        {
                            sr.Write(txt);
                            sr.WriteLine();
                        }
                    }
                }
            }
        }
    }
}
