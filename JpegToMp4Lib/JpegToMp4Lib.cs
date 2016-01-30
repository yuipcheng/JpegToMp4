using AForge.Video.FFMPEG;
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
using System.Threading;
using System.Threading.Tasks;

namespace JpegToMp4Lib
{
    public class JpegToMp4Lib
    {
        public JpegToMp4Lib() { }

        #region props
        private string JpegPath
        {
            get
            {
                var ret = new DirectoryInfo(ConfigurationManager.AppSettings["JpegToMp4Lib_Jpeg"]);
                return ret.FullName;
            }
        }

        private string QueuePath
        {
            get
            {
                var ret = new DirectoryInfo(ConfigurationManager.AppSettings["JpegToMp4Lib_Queue"]);
                return ret.FullName;
            }
        }

        private string Mp4Path
        {
            get
            {
                var ret = new DirectoryInfo(ConfigurationManager.AppSettings["JpegToMp4Lib_Mp4"]);
                return ret.FullName;
            }
        }

        private string BackupPath
        {
            get
            {
                var ret = new DirectoryInfo(ConfigurationManager.AppSettings["JpegToMp4Lib_Backup"]);
                return ret.FullName;
            }
        }

        private string RootFilename
        {
            get
            {
                var ret = Assembly.GetExecutingAssembly().Location;
                return ret;
            }
        }

        private string RootPath
        {
            get
            {
                var ret = new FileInfo(this.RootFilename).Directory.FullName;
                return ret;
            }
        }

        private string LogFilename
        {
            get
            {
                var ret = new FileInfo(Path.Combine(this.RootPath, "Log", DateTime.Today.ToString("yyyy-MM-dd") + ".log"));
                return ret.FullName;
            }
        }
        #endregion

        public void Start()
        {
            if (this.ValidateArgs() == false)
            {
                Console.WriteLine("Invalid settings.");
            }
            else
            {
                var pics = this.GetPictures();

                if (pics.Keys.Count > 0)
                {
                    this.JoinPicturesIntoMovie(pics);
                }
            }
        }

        public bool ValidateArgs()
        {
            var ret = false;

            ret = Directory.Exists(this.JpegPath) && Directory.Exists(this.Mp4Path) && Directory.Exists(this.BackupPath);

            return ret;
        }

        public void Log(string msg)
        {
            try
            {
                var logDir = new FileInfo(this.LogFilename).Directory.FullName;

                if (Directory.Exists(logDir) == false)
                {
                    Directory.CreateDirectory(logDir);
                }

                using (var file = File.AppendText(this.LogFilename))
                {
                    var line = string.Format("{0}: {1}", DateTime.Now.ToLongTimeString(), msg);
                    file.WriteLine(line);
                    Console.WriteLine(line);
                }
            }
            catch (Exception exc)
            {
                Console.Error.WriteLine("Failed to log error. Message: " + exc.Message + " StackTrace: " + exc.StackTrace);
            }
        }

        /// <summary>
        /// outputs a dictionary with:
        /// - key: mp4 file path
        /// - value: list of jpeg files to be composed into the mp4
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public Dictionary<string, List<string>> GetPictures()
        {
            var ret = new Dictionary<string, List<string>>();

            var jpeg = new DirectoryInfo(this.JpegPath);
            var mp4 = new DirectoryInfo(this.Mp4Path);
            var files = jpeg.GetFiles("*.jpg", SearchOption.AllDirectories).OrderBy(o => o.FullName).ToList();

            foreach (var file in files)
            {
                // eg C4D655370473(CAM_01)_1_20150603183804_3969.jpg
                var tokens = file.Name.Split("_".ToCharArray());
                var videoFileName = tokens[0] + "_" + tokens[1] + "_" + tokens[2] + "_" + tokens[3].Substring(0, 8).Insert(4, "-").Insert(7, "-") + ".mp4";
                var key = Path.Combine(mp4.FullName, videoFileName);

                if (ret.ContainsKey(key) == false)
                    ret[key] = new List<string>();

                ret[key].Add(file.FullName);
            }

            return ret;
        }

        public void JoinPicturesIntoMovie(Dictionary<string, List<string>> makeFile)
        {
            var rator = makeFile.GetEnumerator();

            while (rator.MoveNext())
            {
                var videoFile = rator.Current.Key;
                var pictureFiles = rator.Current.Value;

                if (pictureFiles.Count > 0)
                {
                    VideoFileWriter writer = null;
                    var framesOfExistingMp4 = new List<Bitmap>();

                    #region extracts frames out of existing mp4
                    if (File.Exists(videoFile))
                    {// if there is a video already, read the frames out then write to the new file again.
                        using (var reader = new VideoFileReader())
                        {
                            var isExistingMp4Damaged = false;

                            try
                            {
                                reader.Open(videoFile);
                            }
                            catch (Exception exc)
                            {// hack: may want to catch a more specific exception.
                                isExistingMp4Damaged = true;
                            }

                            if (isExistingMp4Damaged == false)
                            {
                                var tmp = reader.ReadVideoFrame();

                                while (tmp != null)
                                {
                                    framesOfExistingMp4.Add(tmp);
                                    tmp = reader.ReadVideoFrame();
                                }
                            }
                        }

                        this.Log(string.Format("Read {0} frames from {1}.", framesOfExistingMp4.Count().ToString(), videoFile));
                    }
                    #endregion

                    using (writer = new VideoFileWriter())
                    {// dimension of first picture will be used for the dimension of the entire movie.
                        var width = Convert.ToInt32(ConfigurationManager.AppSettings["JpegToMp4Lib_Width"]);
                        var height = Convert.ToInt32(ConfigurationManager.AppSettings["JpegToMp4Lib_Height"]);
                        var videoFileIsReady = true;

                        try
                        {
                            writer.Open(videoFile, width, height, 3, VideoCodec.MPEG4);
                        }
                        catch (Exception exc)
                        {
                            videoFileIsReady = false;
                        }

                        if (videoFileIsReady)
                        {
                            #region writes extracted frames to mp4
                            if (framesOfExistingMp4.Count > 0)
                            {
                                var icnt = 0;
                                foreach (var bmp in framesOfExistingMp4)
                                {
                                    try
                                    {
                                        writer.WriteVideoFrame(bmp);
                                        icnt += 1;
                                    }
                                    catch (Exception exc)
                                    {
                                        var msg = string.Format("ERROR! Failed to preserve 1 frame for {0}.", videoFile);
                                        this.Log(msg);
                                    }

                                    bmp.Dispose();
                                }

                                this.Log(string.Format("Written {0} frames back to {1}", icnt.ToString(), videoFile));
                            }
                            #endregion

                            #region writes jpgs to mp4
                            for (int i = 0; i < pictureFiles.Count; i++)
                            {
                                var jpegFile = pictureFiles[i];
                                var jpegQueue = string.Empty;

                                if (jpegFile.Length > 0)
                                {
                                    Bitmap bmp = null;
                                    var badPic = false;

                                    do
                                    {
                                        jpegQueue = MoveToQueue(jpegFile);
                                        Thread.Sleep(100);
                                    } while (string.IsNullOrEmpty(jpegQueue) == true);

                                    try
                                    {
                                        bmp = new Bitmap(jpegQueue);
                                    }
                                    catch (Exception exc)
                                    {
                                        badPic = true;
                                        this.Log(string.Format("ERROR! Skipped bad image {0}. Message: {1} StackTrace: {2}", jpegQueue, exc.Message, exc.StackTrace));
                                    }

                                    if (badPic == false)
                                    {
                                        var fi = new FileInfo(jpegQueue);

                                        AddText(ref bmp, fi.Name);

                                        try
                                        {
                                            writer.WriteVideoFrame(bmp);
                                            this.Log(jpegQueue + " > " + videoFile + " > COMPOSED");
                                        }
                                        catch (Exception exc)
                                        {
                                            this.Log("ERROR! " + jpegQueue + " > " + videoFile + " > FAILED. Message: " + exc.Message + " StackTrace: " + exc.StackTrace);
                                        }
                                        finally
                                        {
                                            bmp.Dispose();
                                        }
                                    }

                                    this.Backup(jpegQueue);
                                }
                            }
                            #endregion}
                        }
                    }
                }
            }
        }

        public string MoveToQueue(string jpeg)
        {
            var ret = string.Empty;

            try
            {
                if (File.Exists(jpeg))
                {
                    var fi = new FileInfo(jpeg);

                    ret = Path.Combine(this.QueuePath, fi.Name);
                    fi.MoveTo(ret);

                    this.Log(jpeg + " > " + ret + " > QUEUED");
                }
            }
            catch { }

            return ret;
        }

        public void AddText(ref Bitmap bmp, string text)
        {
            try
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
            catch (Exception exc)
            {
                this.Log("ERROR! Failed to write text to video. Text: " + text);
            }
        }

        public void Backup(string file)
        {
            var dateFolder = new FileInfo(file).Name.Split("_".ToCharArray())[3].Substring(0, 8).Insert(4, "-").Insert(7, "-");
            var moveTo = Path.Combine(this.BackupPath, dateFolder, new FileInfo(file).Name);

            if (Directory.Exists(Path.Combine(this.BackupPath, dateFolder)) == false)
            {// creates a date folder to put all jpegs in there.
                Directory.CreateDirectory(Path.Combine(this.BackupPath, dateFolder));
            }

            try
            {
                File.Move(file, moveTo);

                this.Log(file + " > " + moveTo + " > BACKED UP");
            }
            catch (IOException exc)
            {
                if (exc.Message.Trim() == "Cannot create a file when that file already exists.")
                {
                    File.Delete(moveTo);
                    File.Move(file, moveTo);

                    this.Log(file + " > " + moveTo + " > BACKED UP");
                }
            }
        }
    }
}
