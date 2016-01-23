using System;
using System.Collections.Generic;
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
    /// ref: http://stackoverflow.com/a/20623311/310767
    /// </summary>
    static public class FileUtil
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        const int RmRebootReasonNone = 0;
        const int CCH_RM_MAX_APP_NAME = 255;
        const int CCH_RM_MAX_SVC_NAME = 63;

        enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        static extern int RmRegisterResources(uint pSessionHandle,
                                              UInt32 nFiles,
                                              string[] rgsFilenames,
                                              UInt32 nApplications,
                                              [In] RM_UNIQUE_PROCESS[] rgApplications,
                                              UInt32 nServices,
                                              string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll")]
        static extern int RmGetList(uint dwSessionHandle,
                                    out uint pnProcInfoNeeded,
                                    ref uint pnProcInfo,
                                    [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
                                    ref uint lpdwRebootReasons);

        /// <summary>
        /// Find out what process(es) have a lock on the specified file.
        /// </summary>
        /// <param name="path">Path of the file.</param>
        /// <returns>Processes locking the file</returns>
        /// <remarks>See also:
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa373661(v=vs.85).aspx
        /// http://wyupdate.googlecode.com/svn-history/r401/trunk/frmFilesInUse.cs (no copyright in code at time of viewing)
        /// 
        /// </remarks>
        static public List<Process> WhoIsLocking(string path)
        {
            uint handle;
            string key = Guid.NewGuid().ToString();
            List<Process> processes = new List<Process>();

            int res = RmStartSession(out handle, 0, key);
            if (res != 0) throw new Exception("Could not begin restart session.  Unable to determine file locker.");

            try
            {
                const int ERROR_MORE_DATA = 234;
                uint pnProcInfoNeeded = 0,
                     pnProcInfo = 0,
                     lpdwRebootReasons = RmRebootReasonNone;

                string[] resources = new string[] { path }; // Just checking on one resource.

                res = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);

                if (res != 0) throw new Exception("Could not register resource.");

                //Note: there's a race condition here -- the first call to RmGetList() returns
                //      the total number of process. However, when we call RmGetList() again to get
                //      the actual processes this number may have increased.
                res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);

                if (res == ERROR_MORE_DATA)
                {
                    // Create an array to store the process results
                    RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                    pnProcInfo = pnProcInfoNeeded;

                    // Get the list
                    res = RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                    if (res == 0)
                    {
                        processes = new List<Process>((int)pnProcInfo);

                        // Enumerate all of the results and add them to the 
                        // list to be returned
                        for (int i = 0; i < pnProcInfo; i++)
                        {
                            try
                            {
                                processes.Add(Process.GetProcessById(processInfo[i].Process.dwProcessId));
                            }
                            // catch the error -- in case the process is no longer running
                            catch (ArgumentException) { }
                        }
                    }
                    else throw new Exception("Could not list processes locking resource.");
                }
                else if (res != 0) throw new Exception("Could not list processes locking resource. Failed to get size of result.");
            }
            finally
            {
                RmEndSession(handle);
            }

            return processes;
        }
    }

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
        #region props
        private static string RootFullPath
        {
            get
            {
                var ret = Assembly.GetExecutingAssembly().Location;
                return ret;
            }
        }

        private static string RootFolder
        {
            get
            {
                var ret = new FileInfo(Program.RootFullPath).Directory.FullName;
                return ret;
            }
        }

        private static string LogFileName
        {
            get
            {
                var ret = new FileInfo(Path.Combine(RootFolder, DateTime.Today.ToString("yyyy-MM-dd") + ".log"));
                return ret.FullName;
            }
        }

        private static string JpegFolder = string.Empty;

        private static string Mp4Folder = string.Empty;

        private static string BackupFolder = string.Empty;
        #endregion

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

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: JpegToMp4.exe path-to-jpeg path-to-mp4 path-to-backup");
            }
            else
            {
                if (Program.ValidateArgs(args) == false)
                {
                    Console.WriteLine("Invalid path(s).");
                }
                else
                {
                    var pics = Program.GetPictures(args);

                    if (pics.Keys.Count > 0)
                    {
                        var backupPath = args[2];
                        Program.JoinPicturesIntoMovie(pics);
                    }
                }
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        public static bool ValidateArgs(string[] args)
        {
            var ret = true;

            if (args.Length >= 3)
            {
                var src = new DirectoryInfo(args[0]);
                var target = new DirectoryInfo(args[1]);
                var backup = new DirectoryInfo(args[2]);

                ret = src.Exists && target.Exists && backup.Exists;

                if (ret)
                {
                    Program.JpegFolder = src.FullName;
                    Program.Mp4Folder = target.FullName;
                    Program.BackupFolder = backup.FullName;
                }
            }

            return ret;
        }

        public static void Log(string msg)
        {
            try
            {
                using (var file = File.AppendText(Program.LogFileName))
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
        public static Dictionary<string, List<string>> GetPictures(string[] args)
        {
            var ret = new Dictionary<string, List<string>>();

            try
            {
                DirectoryInfo src = null;
                DirectoryInfo target = null;

                if (args == null || args.Length == 0)
                {
                    src = new FileInfo(Program.RootFullPath).Directory;
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
                        ret[key] = new List<string>();

                    ret[key].Add(file.FullName);
                }
            }
            catch (Exception exc)
            {
                Program.Log("Failed to get jpeg file paths with args: " + string.Join(", ", args) + " Error: " + exc.Message + " StackTrace: " + exc.StackTrace);
                throw;
            }

            return ret;
        }

        public static void JoinPicturesIntoMovie(Dictionary<string, List<string>> makeFile)
        {
            var rator = makeFile.GetEnumerator();

            while (rator.MoveNext())
            {
                var videoFile = rator.Current.Key;
                var pictureFiles = rator.Current.Value;

                if (pictureFiles.Count > 0)
                {
                    AForge.Video.FFMPEG.VideoFileWriter writer = null;
                    var framesOfExistingMp4 = new List<Bitmap>();

                    #region extracts frames out of existing mp4
                    if (File.Exists(videoFile))
                    {// if there is a video already, read the frames out then write to the new file again.
                        using (var reader = new AForge.Video.FFMPEG.VideoFileReader())
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

                        Program.Log(string.Format("Read {0} frames from {1}.", framesOfExistingMp4.Count().ToString(), videoFile));
                    }
                    #endregion

                    using (writer = new AForge.Video.FFMPEG.VideoFileWriter())
                    {// dimension of first picture will be used for the dimension of the entire movie.
                        int width, height = 0;
                        var videoFileIsReady = true;

                        Program.GetKeyFrameDimension(pictureFiles[0], out width, out height);

                        try
                        {
                            writer.Open(videoFile, width, height, 3, AForge.Video.FFMPEG.VideoCodec.MPEG4);
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
                                        Program.Log(msg);
                                    }
                                    bmp.Dispose();
                                }

                                Program.Log(string.Format("Written {0} frames back to {1}", icnt.ToString(), videoFile));
                            }
                            #endregion

                            #region writes jpgs to mp4
                            for (int i = 0; i < pictureFiles.Count; i++)
                            {
                                var jpegFile = pictureFiles[i];

                                if (jpegFile.Length > 0)
                                {
                                    Bitmap bmp = null;
                                    var badPic = false;

                                    try
                                    {
                                        bmp = new Bitmap(jpegFile);
                                    }
                                    catch (Exception exc)
                                    {
                                        badPic = true;
                                        Program.Log(string.Format("ERROR! Skipped bad image {0}. Message: {1} StackTrace: {2}", jpegFile, exc.Message, exc.StackTrace));
                                    }

                                    if (badPic == false)
                                    {
                                        var fi = new FileInfo(jpegFile);

                                        AddText(ref bmp, fi.Name);
                                        try
                                        {
                                            writer.WriteVideoFrame(bmp);
                                            Program.Log(jpegFile + " > " + videoFile + " > OK");
                                        }
                                        catch (Exception exc)
                                        {
                                            Program.Log("ERROR! " + jpegFile + " > " + videoFile + " > FAILED. Message: " + exc.Message + " StackTrace: " + exc.StackTrace);
                                        }
                                        finally
                                        {
                                            bmp.Dispose();
                                        }
                                    }

                                    Program.Backup(jpegFile);
                                }
                            }
                            #endregion}
                        }
                    }
                }
            }
        }

        public static void GetKeyFrameDimension(string jpegFile, out int width, out int height)
        {
            width = 0;
            height = 0;

            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(jpegFile);
                width = bmp.Width;
                height = bmp.Height;
            }
            finally
            {
                if (bmp != null)
                {
                    bmp.Dispose();
                }
            }
        }

        public static void AddText(ref Bitmap bmp, string text)
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
                Program.Log("ERROR! Failed to write text to video. Text: " + text);
            }
        }

        public static void Backup(string file)
        {
            try
            {
                var moveTo = Path.Combine(Program.BackupFolder, new FileInfo(file).Name);
                File.Move(file, moveTo);
            }
            catch (Exception exc)
            {
                var evil = FileUtil.WhoIsLocking(file);
                var txt = string.Format("ERROR! Failed to move {0} to {1} because file is used by {2}.", file, Program.BackupFolder, string.Join(", ", evil.ToList()));

                Program.Log(txt);
            }
        }
    }
}
