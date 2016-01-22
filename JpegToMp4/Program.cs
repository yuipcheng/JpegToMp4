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
        /// - value: list of jpeg files to be composed into the mp4
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
                    AForge.Video.FFMPEG.VideoFileWriter writer = null;
                    var framesOfExistingMp4 = new List<Bitmap>();

                    #region extracts frames out of existing mp4
                    if (File.Exists(videoFile))
                    {// if there is a video already, read the frames out then write to the new file again.
                        Console.WriteLine("Opening existing video file " + videoFile);

                        var reader = new AForge.Video.FFMPEG.VideoFileReader();
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

                            Console.WriteLine("Frames read: " + framesOfExistingMp4.Count().ToString());
                        }
                    }
                    #endregion

                    using (writer = new AForge.Video.FFMPEG.VideoFileWriter())
                    {// dimension of first picture will be used for the dimension of the entire movie.
                        var firstPicture = new Bitmap(pictureFiles[0].FullName);

                        // creates the new mp4. this will overwrite the existing mp4.
                        writer.Open(videoFile, firstPicture.Width, firstPicture.Height, 3, AForge.Video.FFMPEG.VideoCodec.MPEG4);

                        #region writes extracted frames to mp4
                        foreach (var bmp in framesOfExistingMp4)
                        {
                            writer.WriteVideoFrame(bmp);
                            bmp.Dispose();
                        }

                        Console.WriteLine("Frames written: " + framesOfExistingMp4.Count().ToString());
                        #endregion

                        #region writes jpgs to mp4
                        for (int i = 0; i < pictureFiles.Count; i++)
                        {
                            var fi = pictureFiles[i];

                            if (fi.Length > 0)
                            {
                                Bitmap bmp = null;
                                var badPic = false;

                                try
                                {
                                    bmp = new Bitmap(fi.FullName);
                                }
                                catch (Exception exc)
                                {
                                    badPic = true;
                                    Console.WriteLine(string.Format("Bad image. Message: {0} StackTrace: {1}", exc.Message, exc.StackTrace));
                                }

                                if (badPic == false)
                                {
                                    AddText(ref bmp, fi.Name);
                                    writer.WriteVideoFrame(bmp);
                                    bmp.Dispose();

                                    Console.WriteLine(fi.Name + " > " + new FileInfo(videoFile).Name + " remaining: " + (pictureFiles.Count - i).ToString());
                                }
                            }
                        }
                        #endregion
                    }
                }

                Backup(videoFile, pictureFiles);
            }
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

        public static void Backup(string logPath, List<FileInfo> files)
        {
            foreach (var file in files)
            {
                var bkupFolder = new DirectoryInfo(Path.Combine(file.Directory.FullName, "bkup"));

                if (bkupFolder.Exists == false)
                    bkupFolder.Create();

                var bkup = new FileInfo(Path.Combine(bkupFolder.FullName, file.Name)).FullName;

                try
                {
                    file.MoveTo(bkup);
                }
                catch (Exception exc)
                {
                    var evil = FileUtil.WhoIsLocking(file.FullName);
                    var log = new FileInfo(logPath + ".log");
                    var txt = "Failed " + file.FullName + " => " + bkup + " locked by " + string.Join(", ", evil.ToList());

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
