using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using _4RTools.Utils;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net;

namespace _4RTools.Forms
{
    public partial class AutoPatcher : Form
    {
        private HttpClient client = new HttpClient();
        public AutoPatcher()
        {
            InitializeComponent();
            /**
             * Autopatch Process
             * 1. Fetch the latest tag from AppConfig._4RLatestVersionURL
             * 2. Compare with current version code in AppConfig.cs
             * 3. If different, download the release's .zip asset
             * 3.1  Extract it to a staging folder and verify 4RTools.exe is present
             *      and not suspiciously smaller than the currently-running exe
             * 3.2  Rename the current 4RTools.exe to 4RTools_old.exe
             * 3.3  Copy the staged files over the install, delete the staging
             *      folder and the downloaded zip
             * 3.4  Relaunch
             * 4. If equal, or if anything above fails, just continue running
             *    the current version.
             */
            StartAutopatcher();
        }

        private async void StartAutopatcher()
        {

            //Get Latest Version
            //List[0] = Tag
            //List[1] = Url
            try
            {
                // All paths here are anchored to the exe's own folder, not the process's
                // current working directory - those are NOT the same thing whenever this
                // is launched from a shortcut/debugger/terminal with a different working
                // directory (e.g. Visual Studio's F5 default working directory is the
                // *project* folder, not bin\Debug). Bare relative names like "4RTools.exe"
                // previously resolved wherever the process happened to be started from,
                // which could silently try to patch a copy of the exe that doesn't exist
                // there at all.
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string oldFileName = Path.Combine(appDir, "4RTools_old.exe");
                string sourceFileName = Path.Combine(appDir, "4RTools.exe");
                File.Delete(oldFileName); //Delete old 4RTools
                //Fetch Github latest Tag
                client.Timeout = TimeSpan.FromSeconds(5);
                client.DefaultRequestHeaders.Add("User-Agent", "request");
                string latestVersion = await client.GetStringAsync(AppConfig._4RLatestVersionURL);
                JObject obj = JsonConvert.DeserializeObject<JObject>(latestVersion);

                string tag = obj["name"].ToString(); //Tag Name

                if (tag != AppConfig.Version)
                {
                    string downloadUrl = obj["assets"][0]["browser_download_url"].ToString(); //Latest download url
                    string fileName = Path.Combine(appDir, obj["assets"][0]["name"].ToString()); //Latest file name
                    //If different, 4R is outdated.
                    //Need to download and update
                    await Download(downloadUrl, fileName); //Download the update archive

                    // Extract into a staging folder and verify the new exe is actually
                    // there BEFORE touching the live exe. The previous version renamed
                    // sourceFileName away first and extracted straight into "." - if the
                    // archive was ever the wrong format or missing the exe, that left the
                    // install with no working 4RTools.exe and no way to recover short of
                    // a fresh download.
                    string stagingDir = Path.Combine(appDir, "4RTools_update_staging");
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                    Directory.CreateDirectory(stagingDir);

                    if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        // Every release this fork publishes is a .zip. RAR support was
                        // dropped entirely: it depended on Aspose.Zip, which was never
                        // licensed in this project, and its unlicensed evaluation mode
                        // silently truncates extracted output (confirmed: a real 4.15MB
                        // exe came out as exactly 524,288 bytes - no error, no warning,
                        // just a corrupted exe overwriting the working one). There's no
                        // safe way to support an archive format whose extraction can
                        // silently lie about having succeeded.
                        throw new Exception($"Downloaded update ({fileName}) is not a .zip - refusing to apply it.");
                    }

                    System.IO.Compression.ZipFile.ExtractToDirectory(fileName, stagingDir);

                    string extractedExePath = Path.Combine(stagingDir, "4RTools.exe");
                    if (!File.Exists(extractedExePath))
                    {
                        throw new Exception($"Downloaded update ({fileName}) did not contain 4RTools.exe - aborting update.");
                    }

                    // Sanity-check the size against the exe currently running - catches
                    // any other silent-truncation scenario (a bad download, a different
                    // mis-licensed library down the line, ...) before it can overwrite a
                    // working install with a corrupted one.
                    long extractedSize = new FileInfo(extractedExePath).Length;
                    long currentSize = File.Exists(sourceFileName) ? new FileInfo(sourceFileName).Length : 0;
                    if (currentSize > 0 && extractedSize < currentSize / 2)
                    {
                        throw new Exception($"Downloaded 4RTools.exe is suspiciously small ({extractedSize} bytes vs current {currentSize} bytes) - aborting update.");
                    }

                    File.Move(sourceFileName, oldFileName);
                    CopyDirectoryContents(stagingDir, appDir);

                    Directory.Delete(stagingDir, true);
                    File.Delete(fileName); //Delete the downloaded archive
                    Environment.Exit(0);
                }

            }
            catch (Exception ex)
            {
                // A failed update (bad/incompatible archive, network hiccup, ...) should
                // never leave the app unusable - log it and keep running the current
                // version instead of crashing or leaving the exe half-swapped.
                Console.Error.WriteLine($"[AutoPatcher] Update check/apply failed, continuing with current version: {ex.Message}");
            }
            finally
            {
                new ClientUpdaterForm().Show();
                Hide();
            }
        }

        private static void CopyDirectoryContents(string sourceDir, string destDir)
        {
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                File.Copy(filePath, Path.Combine(destDir, Path.GetFileName(filePath)), true);
            }

            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dirPath));
                Directory.CreateDirectory(destSubDir);
                CopyDirectoryContents(dirPath, destSubDir);
            }
        }

        private async Task<bool> Download(string url, string filename)
        {
            WebClient client = new WebClient();
            client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(_4RTools_DownloadProgressChanged);
            await client.DownloadFileTaskAsync(url, @filename);
            return true;
        }

        void _4RTools_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            this.BeginInvoke((MethodInvoker)delegate {
                double bytesIn = double.Parse(e.BytesReceived.ToString());
                double totalBytes = double.Parse(e.TotalBytesToReceive.ToString());
                double percentage = bytesIn / totalBytes * 100;
                pbPatcher.Value = int.Parse(Math.Truncate(percentage).ToString());
            });
        }


    }
}
