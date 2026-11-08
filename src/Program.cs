// ============================================================================
//  SpideyCompanion - a tiny helper for the Spider-Man live
//  wallpaper (Wallpaper Engine). Version 1.3.0.
//
//  WHY THIS EXISTS
//  Wallpaper Engine's web-wallpaper API can READ what is playing (through
//  Windows' media session) but offers NO way to control it - there is no
//  play/pause/next/seek function a wallpaper is allowed to call. So the
//  wallpaper's deck buttons send a tiny HTTP request to this program on
//  127.0.0.1:8471, and this program presses the buttons on the REAL Windows
//  media session (the same one the Windows volume overlay controls), which
//  works for Spotify, YouTube in a browser, and anything else that
//  publishes to the Windows media session. If the session refuses a
//  command, it falls back to the standard Windows media keys.
//
//  WHAT IT DOES, COMPLETELY:
//    * Listens on 127.0.0.1 (this PC only) port 8471 - nothing external
//      can reach it, and it never connects to the internet itself.
//    * GET /play        -> play or pause the current media session
//    * GET /next        -> next track
//    * GET /prev        -> previous track
//    * GET /seek?pos=N  -> jump to N seconds in the current track
//    * GET /status      -> plain-text report of what it can see/control
//    * GET /update      -> checks the update feed right now
//    * GET /temps       -> passes through LibreHardwareMonitor's data
//                          (127.0.0.1:8085) with the CORS header LHM omits,
//                          so the wallpaper may read CPU/GPU temperatures
//    * GET /health      -> replies "ok" (so you can check it is running)
//    * Everything else  -> "ok" and ignored.
//    * A pixel Spider-Man buddy who lives on the desktop - he walks,
//      blinks, shows emotions and web-swing jumps (see Buddy.cs). His
//      tray icon controls him, and he hides during fullscreen apps.
//  The buddy's preferences (size, pace, display, on/off) are kept in
//  %AppData%\SpideyCompanion\settings.txt - nothing else is stored, and
//  the helper still never connects to the internet itself (the update
//  check only runs if YOU paste a feed URL into update-feed.txt).
//  To stop everything: right-click the tray icon and choose Quit.
//  The buddy's poses are the 46 frames of a Shimeji Spider-Man
//  desktop-pet pack (sprite art by haydentheartist on Reddit).
//  Full source ships with the app - read it yourself.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;

namespace SpideyCompanion
{
    internal static class Program
    {
        private const string Version = "1.3.0";

        // Standard Windows media-key codes, used only as a fallback.
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // All Windows media-session (GSMTC) work runs on ONE dedicated MTA
        // worker thread (requests queue up here). Blocking on media calls
        // from an STA thread without a message pump can silently starve
        // them; a plain MTA worker is the pattern that works.
        private static readonly BlockingCollection<Action> GsmJobs = new BlockingCollection<Action>();
        private static readonly ManualResetEventSlim ExitEvent = new ManualResetEventSlim(false);
        private static string _updateStatus = "not checked yet";
        private static int _applyStarted = 0;
        private static GlobalSystemMediaTransportControlsSessionManager _manager; // worker thread only

        private sealed class Response
        {
            public string Status = "200 OK";
            public string ContentType = "text/plain";
            public string Body = "ok";
        }

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--apply-update")
            {
                ApplyUpdate(args[1], args[2]);
                return;
            }
            bool createdNew;
            try
            {
                var mutex = new Mutex(true, "SpideyCompanion-SingleInstance", out createdNew);
                if (!createdNew) return;
                GC.KeepAlive(mutex);
            }
            catch (AbandonedMutexException) { }

            var listener = new TcpListener(IPAddress.Loopback, 8471);
            try { listener.Start(); }
            catch { return; } // Port already serving - nothing more to do.

            var worker = new Thread(GsmLoop) { IsBackground = true };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();

            var server = new Thread(() => ServerLoop(listener)) { IsBackground = true };
            server.Start();

            var checker = new Thread(UpdateCheckerLoop) { IsBackground = true };
            checker.Start();

            // v1.3: the buddy owns the visible side of the app (tray icon +
            // desktop buddy window), pumped on this STA thread. The HTTP
            // listener and media worker keep running on their own threads
            // exactly as before. A watchdog preserves the old exit path:
            // when ExitEvent is set (e.g. by the updater), the process
            // still exits. If the UI cannot start at all, fall back to
            // the plain wait so the wallpaper link never dies.
            var watchdog = new Thread(() => { ExitEvent.Wait(); Environment.Exit(0); }) { IsBackground = true };
            watchdog.Start();
            if (!BuddyHost.Run()) ExitEvent.Wait();
        }

        private static void GsmLoop()
        {
            foreach (var job in GsmJobs.GetConsumingEnumerable())
            {
                try { job(); } catch { }
            }
        }

        // Runs an action on the GSMTC worker thread and waits for it.
        private static void RunGsm(Action action)
        {
            using (var done = new ManualResetEventSlim(false))
            {
                GsmJobs.Add(() =>
                {
                    try { action(); }
                    catch { }
                    finally { done.Set(); }
                });
                done.Wait(6000);
            }
        }

        private static void ServerLoop(TcpListener listener)
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { continue; }

                try
                {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        string requestLine = reader.ReadLine();
                        var resp = new Response();
                        if (!string.IsNullOrEmpty(requestLine))
                        {
                            resp = Handle(requestLine);
                        }
                        byte[] body = Encoding.UTF8.GetBytes(resp.Body);
                        byte[] head = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 " + resp.Status + "\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Access-Control-Allow-Methods: GET, OPTIONS\r\n" +
                            "Content-Type: " + resp.ContentType + "\r\n" +
                            "Content-Length: " + body.Length + "\r\n" +
                            "Connection: close\r\n\r\n");
                        stream.Write(head, 0, head.Length);
                        stream.Write(body, 0, body.Length);
                        stream.Flush();
                    }
                }
                catch { /* A broken request must never kill the helper. */ }
            }
        }

        private static Response Handle(string requestLine)
        {
            var resp = new Response();
            string[] parts = requestLine.Split(' ');
            if (parts.Length < 2) return resp;
            string method = parts[0];
            string path = parts[1];

            if (method == "OPTIONS") { resp.Body = ""; return resp; }
            if (path.StartsWith("/status"))
            {
                string st = BuildStatus();
                resp.Body = (st.StartsWith("Spidey Companion v") ? st : "Spidey Companion v" + Version + " - running\r\n" + st);
                return resp;
            }
            if (path.StartsWith("/update")) { CheckForUpdates(); resp.Body = "Update check: " + _updateStatus; return resp; }
            if (path.StartsWith("/temps")) { return FetchTemps(); }
            if (path.StartsWith("/next")) RunGsm(Next);
            else if (path.StartsWith("/prev")) RunGsm(Previous);
            else if (path.StartsWith("/play")) RunGsm(PlayPause);
            else if (path.StartsWith("/seek"))
            {
                long seconds;
                if (TryParseSeek(path, out seconds)) RunGsm(() => Seek(seconds));
            }
            return resp;
        }

        private static bool TryParseSeek(string path, out long seconds)
        {
            seconds = 0;
            int i = path.IndexOf("pos=", StringComparison.Ordinal);
            if (i < 0) return false;
            string rest = path.Substring(i + 4);
            int amp = rest.IndexOf('&');
            if (amp >= 0) rest = rest.Substring(0, amp);
            return long.TryParse(rest, out seconds) && seconds >= 0;
        }

        private static Response FetchTemps()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:8085/data.json");
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    return new Response { ContentType = "application/json", Body = sr.ReadToEnd() };
                }
            }
            catch
            {
                return new Response { Status = "502 Bad Gateway", Body = "LibreHardwareMonitor not reachable on port 8085" };
            }
        }

        private static string BuildStatus()
        {
            string report = "unknown";
            RunGsm(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("Spidey Companion v" + Version + " - running");
                try
                {
                    var session = PickSession();
                    if (session == null)
                    {
                        sb.AppendLine("Media session: NONE FOUND");
                        sb.AppendLine("Start something playing (Spotify, YouTube in a browser), then reload this page.");
                    }
                    else
                    {
                        var info = session.GetPlaybackInfo();
                        var c = info.Controls;
                        sb.AppendLine("Media session: " + session.SourceAppUserModelId);
                        sb.AppendLine("State: " + info.PlaybackStatus);
                        sb.AppendLine("Controls: play=" + YesNo(c.IsPlayEnabled) +
                                      " pause=" + YesNo(c.IsPauseEnabled) +
                                      " next=" + YesNo(c.IsNextEnabled) +
                                      " prev=" + YesNo(c.IsPreviousEnabled) +
                                      " seek=" + YesNo(c.IsPlaybackPositionEnabled));
                        try
                        {
                            var t = session.GetTimelineProperties();
                            sb.AppendLine("Position: " + (long)t.Position.TotalSeconds + "s of " + (long)t.EndTime.TotalSeconds + "s");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Media session check failed: " + ex.GetType().Name);
                }
                report = sb.ToString();
            });
            return report + "Version: " + Version + "\r\nUpdates: " + _updateStatus + "\r\n";
        }

        private static string YesNo(bool b) { return b ? "yes" : "no"; }

        // ---- Windows media session (GSMTC) - worker thread only ----

        private static T Await<T>(IAsyncOperation<T> op)
        {
            var task = op.AsTask();
            if (!task.Wait(3000)) throw new TimeoutException("media call timed out");
            return task.Result;
        }

        private static GlobalSystemMediaTransportControlsSession PickSession()
        {
            if (_manager == null)
            {
                _manager = Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            }
            GlobalSystemMediaTransportControlsSession current = null;
            try { current = _manager.GetCurrentSession(); } catch { }
            try
            {
                foreach (var s in _manager.GetSessions())
                {
                    if (s == null) continue;
                    if (s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        return s; // Whatever is actually playing wins.
                    }
                }
            }
            catch { }
            return current; // May be null - that simply means nothing to control.
        }

        private static void PlayPause()
        {
            bool done = false;
            try
            {
                var session = PickSession();
                if (session != null)
                {
                    var info = session.GetPlaybackInfo();
                    if (info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        if (info.Controls.IsPauseEnabled) done = Await(session.TryPauseAsync());
                    }
                    else
                    {
                        if (info.Controls.IsPlayEnabled) done = Await(session.TryPlayAsync());
                    }
                }
            }
            catch { }
            if (!done) SendMediaKey(VK_MEDIA_PLAY_PAUSE);
        }

        private static void Next()
        {
            bool done = false;
            try
            {
                var session = PickSession();
                if (session != null && session.GetPlaybackInfo().Controls.IsNextEnabled)
                {
                    done = Await(session.TrySkipNextAsync());
                }
            }
            catch { }
            if (!done) SendMediaKey(VK_MEDIA_NEXT_TRACK);
        }

        private static void Previous()
        {
            bool done = false;
            try
            {
                var session = PickSession();
                if (session != null && session.GetPlaybackInfo().Controls.IsPreviousEnabled)
                {
                    done = Await(session.TrySkipPreviousAsync());
                }
            }
            catch { }
            if (!done) SendMediaKey(VK_MEDIA_PREV_TRACK);
        }

        private static void Seek(long seconds)
        {
            try
            {
                var session = PickSession();
                if (session != null && session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled)
                {
                    // GSMTC positions are in 100-nanosecond ticks.
                    Await(session.TryChangePlaybackPositionAsync(seconds * 10000000L));
                }
            }
            catch { /* Seeking is best-effort; there is no key fallback. */ }
        }


        // ---------------- SELF-UPDATE ----------------
        // If update-feed.txt (next to the exe) contains a line
        //     feed=<url of a small version file>
        // the companion checks that file on startup and every 12 hours.
        // The version file looks like:
        //     version=1.3.0
        //     url=<direct link to the new SpideyCompanion.exe>
        //     sha256=<sha-256 of that exe>        (optional, verified)
        // A newer exe is downloaded to the "update" folder, checked
        // against the hash, and then a temporary copy of this program
        // (running as --apply-update) waits for this process to exit,
        // swaps the exe, and starts the new one. No reinstall needed.
        // With no feed configured, nothing is ever downloaded.

        private static string InstallDir
        {
            get { return Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
        }

        private static void UpdateCheckerLoop()
        {
            Thread.Sleep(8000);
            while (true)
            {
                CheckForUpdates();
                Thread.Sleep(12 * 60 * 60 * 1000);
            }
        }

        private static string ReadFeedUrl()
        {
            try
            {
                string f = Path.Combine(InstallDir, "update-feed.txt");
                if (!File.Exists(f)) return null;
                foreach (var line in File.ReadAllLines(f))
                {
                    string t = line.Trim();
                    if (t.StartsWith("feed=", StringComparison.OrdinalIgnoreCase))
                    {
                        string u = t.Substring(5).Trim();
                        return u.Length > 0 ? u : null;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string DownloadText(string url, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "SpideyCompanion/" + Version;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        private static void CheckForUpdates()
        {
            try
            {
                string feed = ReadFeedUrl();
                if (feed == null) { _updateStatus = "no update feed configured (see update-feed.txt)"; return; }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string text = DownloadText(feed, 15000);
                string ver = null, url = null, sha = null;
                foreach (var raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("version=", StringComparison.OrdinalIgnoreCase)) ver = line.Substring(8).Trim();
                    else if (line.StartsWith("url=", StringComparison.OrdinalIgnoreCase)) url = line.Substring(4).Trim();
                    else if (line.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) sha = line.Substring(7).Trim();
                }
                System.Version remote, current;
                if (ver == null || url == null ||
                    !System.Version.TryParse(ver, out remote) ||
                    !System.Version.TryParse(Version + ".0", out current))
                {
                    _updateStatus = "update feed could not be read";
                    return;
                }
                if (remote <= current) { _updateStatus = "up to date (v" + Version + ")"; return; }

                string updDir = Path.Combine(InstallDir, "update");
                Directory.CreateDirectory(updDir);
                string tmp = Path.Combine(updDir, "SpideyCompanion.download");
                string dest = Path.Combine(updDir, "SpideyCompanion-v" + ver + ".exe");
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                req.UserAgent = "SpideyCompanion/" + Version;
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var input = res.GetResponseStream())
                using (var output = File.Create(tmp))
                {
                    input.CopyTo(output);
                }
                if (sha != null && sha.Length >= 32)
                {
                    string actual = Sha256Of(tmp);
                    if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tmp);
                        _updateStatus = "update v" + ver + " failed its safety hash check - not installed";
                        return;
                    }
                }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                File.WriteAllText(Path.Combine(updDir, "newname.txt"), Path.GetFileName(dest));
                _updateStatus = "v" + ver + " downloaded - restarting into the update";
                BeginApplyHandoff();
            }
            catch (Exception ex)
            {
                _updateStatus = "check failed (" + ex.GetType().Name + ") - will retry later";
            }
        }

        private static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                var sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void BeginApplyHandoff()
        {
            if (Interlocked.Exchange(ref _applyStarted, 1) != 0) return;
            var t = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(1500); // let any in-flight HTTP reply finish
                    string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string helper = Path.Combine(Path.GetTempPath(), "SpideyCompanion-Updater.exe");
                    File.Copy(self, helper, true);
                    var psi = new ProcessStartInfo
                    {
                        FileName = helper,
                        Arguments = "--apply-update \"" + InstallDir + "\" " + Process.GetCurrentProcess().Id,
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                }
                catch { }
                ExitEvent.Set();
            }) { IsBackground = true };
            t.Start();
        }

        private static void ApplyUpdate(string dir, string pidText)
        {
            try
            {
                int pid;
                if (int.TryParse(pidText, out pid))
                {
                    try { Process.GetProcessById(pid).WaitForExit(20000); } catch { }
                }
                Thread.Sleep(400);
                string newName = "SpideyCompanion.exe";
                try
                {
                    string nameFile = Path.Combine(dir, "update", "newname.txt");
                    if (File.Exists(nameFile)) newName = File.ReadAllText(nameFile).Trim();
                }
                catch { }
                string source = Path.Combine(dir, "update", newName);
                string target = Path.Combine(dir, newName);
                for (int i = 0; i < 20 && File.Exists(source); i++)
                {
                    try { File.Copy(source, target, true); break; }
                    catch { Thread.Sleep(500); }
                }
                try
                {
                    string wanted = Path.GetFileName(target);
                    foreach (var old in Directory.GetFiles(dir, "SpideyCompanion*.exe"))
                    {
                        if (!string.Equals(Path.GetFileName(old), wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(old); } catch { }
                        }
                    }
                }
                catch { }
                try
                {
                    using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (run != null) run.SetValue("SpideyCompanion", "\"" + target + "\"");
                    }
                }
                catch { }
                try { Directory.Delete(Path.Combine(dir, "update"), true); } catch { }
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        private static void SendMediaKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero); // key down
            keybd_event(vk, 0, 2, UIntPtr.Zero); // key up (2 = KEYEVENTF_KEYUP)
        }
    }
}
