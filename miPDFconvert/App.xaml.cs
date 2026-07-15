// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed WITHOUT ANY WARRANTY.
// See the GNU AGPL v3 (LICENSE file) for details and THIRD-PARTY-NOTICES.md
// for the licenses of bundled components.

using log4net;
using log4net.Config;
using Microsoft.Win32;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace miPDFconvert
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILog LOGGER = LogManager.GetLogger(typeof(App));

        private const string DOCUMENT_TARGET_KEY = "TARGET_APPLICATION";
        private const string CREATE_FILES_KEY = "CREATE_DOCUMENT_FILES";
        private const string PDF_SETTINGS_KEY = "PDF_SETTINGS";
        private const string DEFAULT_PDF_SETTINGS = "/printer";
        public void App_Startup(object sender, StartupEventArgs e)
        {
            // Bulletproof diagnostics: record startup and any unhandled crash to a plain text
            // file that does NOT depend on log4net or the application configuration. If the
            // deployed miPDFconvert.dll.config is malformed, log4net cannot initialize and the
            // normal log stays empty - this trace still tells us the process started and why it
            // failed. File: %ProgramData%\miPDFconvert\miPDFconvert.trace.log
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                WriteStartupTrace("UNHANDLED (AppDomain): " + ev.ExceptionObject);
            this.DispatcherUnhandledException += (s, ev) =>
            {
                WriteStartupTrace("UNHANDLED (Dispatcher): " + ev.Exception);
                ev.Handled = true;
                Environment.Exit(1);
            };
            WriteStartupTrace("App_Startup entered. Command line: " + Environment.CommandLine);

            try
            {
                XmlConfigurator.Configure();
            }
            catch (Exception ex)
            {
                WriteStartupTrace("XmlConfigurator.Configure() failed (log4net logging will be unavailable): " + ex);
            }
            CleanupOldTempPdfFiles();

            // Check if there is another miPDFconvert process running, if so, wait until it is
            // closed. The wait is bounded: a single hung instance (unanswered dialog, Ghostscript
            // stuck on malformed PostScript) must not silently block every following print job
            // forever - after the timeout we continue and serve this job anyway.
            var instanceWait = Stopwatch.StartNew();
            while (IsOldInstanceRunning())
            {
                if (instanceWait.Elapsed.TotalSeconds > 60)
                {
                    LOGGER.Warn("An older miPDFconvert instance is still running after 60 s - continuing anyway.");
                    break;
                }
                LOGGER.Debug("Waiting for old miPDFconvert processes to end...");
                Thread.Sleep(300);
            }

            string pdfInputFilePath = String.Empty;
            string psInputFilePath = String.Empty;
            string inputFileName = String.Empty;

            // Environment.GetCommandLineArgs()[0] is the executable path itself - skip it, the
            // remaining elements are the real arguments (like e.Args, but also available when
            // the app is launched via CreateProcessAsUser).
            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length == 0)
            {
                LOGGER.Error("miPDFconvert started without any arguments! Use -ps followed by PostScript standard input stream or specify a PDF input file path as first argument.");
                ShowErrorMessage("miPDFconvert started without any arguments! Use -ps followed by PostScript standard input stream or specify a PDF input file path as first argument.");
                Environment.Exit(1);
                return;
            }

            LOGGER.Info($"miPDFconvert started with the following arguments: {String.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg))}.");

            if (DoesCommandLineParameterExist(args, "-ps"))
            {
                LOGGER.Info("Awaiting PostScript document from standard input stream...");

                inputFileName = GetValueFromCommandLineParameter(args, "-ps");
            }
            else if (DoesCommandLineParameterExist(args, "-psfile"))
            {
                psInputFilePath = GetValueFromCommandLineParameter(args, "-psfile");

                if (String.IsNullOrEmpty(psInputFilePath))
                {
                    LOGGER.Error($"miPDFconvert argument '-psfile' requires a PostScript file path as next argument!");
                    ShowErrorMessage("miPDFconvert argument '-psfile' requires a PostScript file path as next argument!");
                    Environment.Exit(1);
                    return;
                }

                inputFileName = Path.GetFileName(psInputFilePath);
                LOGGER.Info($"Got argument '{psInputFilePath}' as PostScript input file path...");
            }
            else
            {
                pdfInputFilePath = String.Join(" ", args);
                inputFileName = Path.GetFileName(pdfInputFilePath);
                LOGGER.Info($"miPDFconvert started with command line argument '{pdfInputFilePath}'. Interpreting this as PDF input file path.");
            }
            Boolean.TryParse(ConfigurationManager.AppSettings[CREATE_FILES_KEY], out bool createDocumentFiles);
            byte[]? pdf = GetPdfDocument(pdfInputFilePath, psInputFilePath, createDocumentFiles);

            // The renamed spool file is consumed once the PDF exists - delete it so the spool
            // directory does not grow unbounded (nobody else cleans it up).
            if (pdf != null && !String.IsNullOrEmpty(psInputFilePath))
            {
                try
                {
                    File.Delete(psInputFilePath);
                    LOGGER.Debug($"Spool file \"{psInputFilePath}\" deleted.");
                }
                catch (Exception ex)
                {
                    LOGGER.Warn($"Could not delete spool file \"{psInputFilePath}\": {ex.Message}");
                }
            }

            ProcessInput(pdf, inputFileName, createDocumentFiles);
        }

        /// <summary>
        /// Appends a diagnostic line to %ProgramData%\miPDFconvert\miPDFconvert.trace.log.
        /// Deliberately independent of log4net/ConfigurationManager so it also works when the
        /// application configuration is broken. Never throws.
        /// </summary>
        private static void WriteStartupTrace(string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "miPDFconvert");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "miPDFconvert.trace.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.UserName}] {message}{Environment.NewLine}");
            }
            catch { /* diagnostics must never throw */ }
        }

        private static void ProcessInput(byte[]? pdf, string fileName, bool createDocumentFiles)
        {
            if (pdf == null)
            {
                LOGGER.Error("No PDF document available! Quitting application.");
                Environment.Exit(1);
                return;
            }

            LOGGER.Info($"Document name is '{fileName}'.");

            // Create document file for temporary testing purposes if turned on
            if (createDocumentFiles)
            {
                string strPdfFilePath = String.Empty;
                try
                {
                    strPdfFilePath = Path.Combine(Path.GetTempPath(), "miPDFconvert.pdf");
                    LOGGER.Debug($"Saving PDF document as \"{strPdfFilePath}\"...");
                    File.WriteAllBytes(strPdfFilePath, pdf);
                    LOGGER.Debug("PDF document saved.");
                }
                catch (Exception ex)
                {
                    LOGGER.Error($"Exception when saving PDF document to \"{strPdfFilePath}\". Saving document is intended only for testing purposes and can be controlled by the {CREATE_FILES_KEY} property in the application configuration.", ex);
                    // Continue without saving
                }
            }

            // Map target application setting
            string? documentTarget = ConfigurationManager.AppSettings[DOCUMENT_TARGET_KEY]?.Trim();
            LOGGER.Debug($"{DOCUMENT_TARGET_KEY} value \"{documentTarget}\" is configured in application configuration.");

            // Now open created PDF in finally determined target application...
            try
            {
                if (String.IsNullOrEmpty(documentTarget))
                {
                    //default implementation is a saveas dialog
                    SaveFileDialog saveFileDialog = new SaveFileDialog
                    {
                        FileName = Path.ChangeExtension(fileName, ".pdf"),
                        Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
                    };

                    // The application has no visible main window, so the dialog would have no owner
                    // and could appear behind other applications. We create a hidden, topmost owner
                    // window to force the save dialog into the foreground.
                    Window ownerWindow = CreateTopmostOwnerWindow();
                    bool? dialogResult;
                    try
                    {
                        dialogResult = saveFileDialog.ShowDialog(ownerWindow);
                    }
                    finally
                    {
                        ownerWindow.Close();
                    }

                    if (dialogResult == true)
                    {
                        // Save the PDF document to the selected file
                        File.WriteAllBytes(saveFileDialog.FileName, pdf);
                        LOGGER.Info($"PDF document saved to \"{saveFileDialog.FileName}\".");
                        Environment.Exit(0);
                    }
                    else
                    {
                        LOGGER.Info("Save file dialog was cancelled by user. Exiting application.");
                        Environment.Exit(0);
                    }
                }
                else
                {
                    // Resolve the target application. A relative path (or bare file name)
                    // is resolved against the application directory first, so a target
                    // shipped next to the executable also works.
                    string resolvedTarget = documentTarget;
                    if (!Path.IsPathRooted(resolvedTarget))
                    {
                        string localCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resolvedTarget);
                        if (File.Exists(localCandidate))
                            resolvedTarget = localCandidate;
                    }

                    // Try to start target application with PDF document as argument
                    LOGGER.Info($"Passing PDF document to target application \"{resolvedTarget}\"...");
                    // Create temporary file for PDF document. The "miPDFconvert_" prefix lets
                    // CleanupOldTempPdfFiles identify and remove stale files on later runs
                    // (the file cannot be deleted right away - the target application reads it).
                    string tempPdfFilePath = Path.Combine(Path.GetTempPath(), $"miPDFconvert_{Guid.NewGuid():N}.pdf");
                    File.WriteAllBytes(tempPdfFilePath, pdf);
                    LOGGER.Debug($"Temporary PDF file created as \"{tempPdfFilePath}\".");
                    try
                    {
                        ProcessStartInfo processStartInfo = new ProcessStartInfo
                        {
                            FileName = resolvedTarget,
                            Arguments = $"\"{tempPdfFilePath}\"",
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(resolvedTarget) ?? AppDomain.CurrentDomain.BaseDirectory
                        };
                        // The print pipeline starts miPDFconvert in the background, so a target
                        // application we launch inherits that state and its window would stay behind
                        // other windows (Windows foreground lock). Take the foreground ourselves via a
                        // hidden topmost owner window so we hold the right to hand it over, then actively
                        // bring the target window to the front.
                        Window foregroundOwner = CreateTopmostOwnerWindow();
                        Process? process = Process.Start(processStartInfo);
                        if (process == null)
                        {
                            foregroundOwner.Close();
                            LOGGER.Error($"Could not start target application \"{resolvedTarget}\"!");
                            ShowErrorMessage($"Could not start target application \"{resolvedTarget}\"!");
                            Environment.Exit(1);
                        }
                        else
                        {
                            LOGGER.Info($"Target application \"{resolvedTarget}\" started successfully (PID {process.Id}).");
                            BringProcessToForeground(process, foregroundOwner);
                            // IMPORTANT: exit explicitly. This is a WPF application without a
                            // main window, so without this call the dispatcher loop would keep
                            // running forever, the process would never terminate and the calling
                            // miPDFconvertBase launcher would block on its WaitForSingleObject
                            // timeout (up to 180 s). Exiting here releases the print pipeline
                            // immediately.
                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Error($"Exception when starting target application \"{resolvedTarget}\": ", ex);
                        ShowErrorMessage($"Exception when starting target application \"{resolvedTarget}\": {ex.Message}");
                        Environment.Exit(1);
                    }
                }
            }
            catch (Exception ex)
            {
                LOGGER.Error($"Exception when passing PDF to target application: ", ex);
                ShowErrorMessage($"Exception when passing PDF to target application: {ex.Message}");
                Environment.Exit(1);
            }
            finally
            {
                LOGGER.Info($"Exiting miPDFconvert application with code {Environment.ExitCode}.");
            }
        }

        /// <summary>
        /// Creates a hidden, topmost window that is activated and can be used as an owner for
        /// dialogs (e.g. the SaveFileDialog). Because the application runs without a visible main
        /// window, dialogs would otherwise have no owner and could appear behind other applications.
        /// Using a topmost owner forces the dialog into the foreground.
        /// </summary>
        private static Window CreateTopmostOwnerWindow()
        {
            Window ownerWindow = new Window
            {
                Width = 1,
                Height = 1,
                Left = -32000,   // position off-screen so it is never visible
                Top = -32000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true
            };

            ownerWindow.Show();
            ownerWindow.Activate();
            return ownerWindow;
        }

        /// <summary>
        /// Shows an error message box on top of other windows. Without a topmost owner the
        /// box would belong to a background process and could appear hidden behind other
        /// applications - the user would never see the error and the print job would seem
        /// to vanish silently.
        /// </summary>
        private static void ShowErrorMessage(string message)
        {
            Window ownerWindow = CreateTopmostOwnerWindow();
            try
            {
                _ = MessageBox.Show(ownerWindow, message, "miPDFconvert", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ownerWindow.Close();
            }
        }

        /// <summary>
        /// Removes temporary PDF files from earlier runs (target application mode). They cannot
        /// be deleted when they are created - the launched target application still reads them -
        /// so each new run cleans up files older than one day. Never throws.
        /// </summary>
        private static void CleanupOldTempPdfFiles()
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(Path.GetTempPath(), "miPDFconvert_*.pdf"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1))
                        {
                            File.Delete(file);
                            LOGGER.Debug($"Old temporary PDF \"{file}\" deleted.");
                        }
                    }
                    catch { /* file may be in use - try again on a later run */ }
                }
            }
            catch { /* cleanup must never break printing */ }
        }

        #region Native foreground helpers

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// Forces the main window of a target application into the foreground. Because the print
        /// pipeline starts miPDFconvert in the background, a process it launches inherits that state
        /// and Windows' foreground lock keeps the window behind others. This works around the lock by
        /// (1) granting the target the right to set the foreground window, (2) waiting for its window
        /// to appear, and (3) attaching to the current foreground thread's input queue before calling
        /// SetForegroundWindow. The hidden topmost owner window (created by the caller) keeps
        /// miPDFconvert itself in the foreground while this runs and is closed at the end.
        /// </summary>
        private static void BringProcessToForeground(Process process, Window foregroundOwner)
        {
            const int SW_RESTORE = 9;
            const int ASFW_ANY = -1;
            try
            {
                // Grant foreground rights broadly right away. This is the only lever that works for
                // single-instance / broker apps (e.g. Microsoft Edge), where the process we started
                // forwards the request to another, already running process and exits, so we never
                // obtain its window handle.
                AllowSetForegroundWindow(ASFW_ANY);

                // Wait until the process has pumped its message queue / created its UI.
                try { process.WaitForInputIdle(5000); }
                catch { /* console app, no message loop, or already exited - ignore */ }

                // MainWindowHandle stays 0 until the window exists; poll for a few seconds.
                IntPtr hWnd = IntPtr.Zero;
                for (int i = 0; i < 50; i++)
                {
                    if (process.HasExited)
                        break;
                    process.Refresh();
                    hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero)
                        break;
                    Thread.Sleep(100);
                }

                if (hWnd == IntPtr.Zero)
                {
                    // Broker / single-instance apps forwarded the request to another process and let
                    // ours exit, so there is no window to activate directly - the ASFW_ANY grant above
                    // is what lets the real owner pull itself to the front.
                    LOGGER.Warn($"No main window found for target process {process.Id} (likely a single-instance/broker app); relied on ASFW_ANY fallback.");
                    return;
                }

                // Grant the concrete target process the right to set itself foreground, even after we
                // exit, then activate its window directly.
                AllowSetForegroundWindow(process.Id);

                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);

                // Attach our input queue to the current foreground thread so Windows permits the
                // foreground hand-off; skip if we are already the foreground thread.
                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
                uint currentThread = GetCurrentThreadId();
                bool attached = false;
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);

                try
                {
                    BringWindowToTop(hWnd);
                    SetForegroundWindow(hWnd);
                }
                finally
                {
                    if (attached)
                        AttachThreadInput(currentThread, foregroundThread, false);
                }

                LOGGER.Info($"Target window of process {process.Id} brought to the foreground.");
            }
            catch (Exception ex)
            {
                LOGGER.Warn($"Failed to bring target process {process.Id} to the foreground: {ex.Message}");
            }
            finally
            {
                foregroundOwner.Close();
            }
        }

        #endregion

        private static byte[]? GetPdfDocument(string pdfFilePath, string psInputFilePath, bool createDocumentFiles)
        {
            // If a file path for an existing PDF file is provided, directly read and return it
            if (!String.IsNullOrEmpty(pdfFilePath))
            {
                try
                {
                    LOGGER.Info($"Reading PDF input document from \"{pdfFilePath}\"...");
                    byte[] pdfBinary = File.ReadAllBytes(pdfFilePath);
                    LOGGER.Info($"Input document successfully loaded with length of {pdfBinary.Length} bytes.");
                    return pdfBinary;
                }
                catch (Exception ex)
                {
                    LOGGER.Error($"Exception when reading PDF input document from \"{pdfFilePath}\":", ex);
                    ShowErrorMessage($"Exception when reading PDF input document from \"{pdfFilePath}\": {ex.Message}");
                    return null;
                }
            }

            byte[]? createdPdfBinary;

            if (!String.IsNullOrEmpty(psInputFilePath))
            {
                // FAST PATH: a real PostScript file exists on disk. Let Ghostscript read it
                // directly instead of loading it into memory, decoding it into a string and
                // feeding it back through StdIn in chunks. This avoids a large allocation, a
                // potentially lossy byte->string conversion of binary PostScript, and the
                // per-chunk Substring copies - and is the fastest way to hand data to gs.
                LOGGER.Info($"Converting PostScript input file \"{psInputFilePath}\" directly...");
                createdPdfBinary = ConvertPS2Pdf(psInputFilePath, null);
            }
            else
            {
                // Console input stream (-ps mode): data only exists in the pipe, so we must
                // buffer it and feed it to Ghostscript through StdIn.
                LOGGER.Info("Importing PostScript document from console input stream...");
                byte[]? psBinary = GetPostScriptDocumentFromInputStream();

                if (psBinary == null || psBinary.Length == 0)
                {
                    ShowErrorMessage("No PostScript data was received from the standard input stream.");
                    return null;
                }

                LOGGER.Info($"Imported PostScript document has {psBinary.Length} bytes length.");

                // Create document file for temporary testing purposes if turned on
                if (createDocumentFiles)
                {
                    string strPostScriptFilePath = String.Empty;
                    try
                    {
                        strPostScriptFilePath = Path.Combine(Path.GetTempPath(), "miPDFconvert.ps");
                        LOGGER.Debug($"Saving PostScript document as \"{strPostScriptFilePath}\"...");
                        File.WriteAllBytes(strPostScriptFilePath, psBinary);
                        LOGGER.Debug("PDF document saved.");
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Error($"Exception when saving PostScript document to \"{strPostScriptFilePath}\". Saving document is intended only for testing purposes and can be controlled by the {CREATE_FILES_KEY} property in the application configuration.", ex);
                        // Continue without saving
                    }
                }

                createdPdfBinary = ConvertPS2Pdf(null, psBinary);
            }

            if ((createdPdfBinary?.Length ?? 0) == 0)
            {
                LOGGER.Error("No PDF document could be created from PostScript document!");
                ShowErrorMessage("No PDF document could be created from PostScript document!");
                return null;
            }

            LOGGER.Info($"PDF document created with length of {createdPdfBinary?.Length} bytes.");
            return createdPdfBinary;
        }

        private static byte[]? GetPostScriptDocumentFromInputStream()
        {
            try
            {
                Stream inputStream = Console.OpenStandardInput();
                using (var outputStream = new MemoryStream())
                {
                    var buffer = new byte[0x1000];
                    int nTotalBytes = 0;
                    int nBufferBytes = 1;

                    while (nBufferBytes > 0)
                    {
                        nBufferBytes = inputStream.Read(buffer, 0, buffer.Length);
                        if (nBufferBytes > 0)
                        {
                            nTotalBytes += nBufferBytes;
                            LOGGER.Debug($"    {nTotalBytes} bytes read from standard input stream.");
                            outputStream.Write(buffer, 0, nBufferBytes);
                        }
                        else
                            LOGGER.Debug($"    No (more) bytes read from standard input stream.");
                    }

                    outputStream.Close();
                    return outputStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                LOGGER.Error($"Exception when reading from standard input stream:", ex);
                return null;
            }
        }

        /// <summary>
        /// Converts a PostScript document to PDF by invoking the installed Ghostscript executable
        /// as an external process. Exactly one of <paramref name="psFilePath"/> or
        /// <paramref name="postscriptDocument"/> must be supplied.
        ///
        /// Running Ghostscript out-of-process (instead of loading gsdllXX.dll into this process)
        /// makes the conversion independent of this application's bitness: a 32-bit process can
        /// drive a 64-bit Ghostscript and vice versa, so whichever build the user installed works.
        /// </summary>
        private static byte[]? ConvertPS2Pdf(string? psFilePath, byte[]? postscriptDocument)
        {
            string? tempPsFile = null;
            string tempPdfFile = Path.Combine(Path.GetTempPath(), $"miPDFconvert_gs_{Guid.NewGuid():N}.pdf");
            try
            {
                string inputPsPath;
                if (!String.IsNullOrEmpty(psFilePath))
                {
                    inputPsPath = psFilePath!;
                }
                else
                {
                    // stdin mode: persist the raw bytes to a temp .ps so Ghostscript reads them
                    // directly - no encoding guesswork, the binary PostScript is preserved exactly.
                    tempPsFile = Path.Combine(Path.GetTempPath(), $"miPDFconvert_gs_{Guid.NewGuid():N}.ps");
                    File.WriteAllBytes(tempPsFile, postscriptDocument!);
                    inputPsPath = tempPsFile;
                }

                string gsExecutable = FindGhostscriptExecutable();

                List<string> switches = BuildGhostscriptSwitches(tempPdfFile);
                switches.Add("-f");
                switches.Add(inputPsPath);

                LOGGER.Info($"Converting PostScript to PDF via \"{gsExecutable}\" (switches: {String.Join(" ", switches)})...");

                var psi = new ProcessStartInfo
                {
                    FileName = gsExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string s in switches)
                    psi.ArgumentList.Add(s);

                using (Process? gsProcess = Process.Start(psi))
                {
                    if (gsProcess == null)
                    {
                        LOGGER.Error($"Could not start Ghostscript process \"{gsExecutable}\".");
                        return null;
                    }

                    // Read both streams asynchronously to avoid a pipe-buffer deadlock.
                    Task<string> stdoutTask = gsProcess.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = gsProcess.StandardError.ReadToEndAsync();

                    if (!gsProcess.WaitForExit(120000))
                    {
                        LOGGER.Error("Ghostscript did not finish within 120 s - terminating it.");
                        try { gsProcess.Kill(true); } catch { }
                        return null;
                    }
                    gsProcess.WaitForExit(); // ensure the async stdout/stderr readers have flushed

                    string stdout = stdoutTask.GetAwaiter().GetResult();
                    string stderr = stderrTask.GetAwaiter().GetResult();
                    if (!String.IsNullOrWhiteSpace(stdout)) LOGGER.Debug($"Ghostscript output: {stdout.Trim()}");
                    if (!String.IsNullOrWhiteSpace(stderr)) LOGGER.Debug($"Ghostscript messages: {stderr.Trim()}");

                    if (gsProcess.ExitCode != 0)
                    {
                        LOGGER.Error($"Ghostscript exited with code {gsProcess.ExitCode}.");
                        return null;
                    }
                }

                if (!File.Exists(tempPdfFile))
                {
                    LOGGER.Error("Ghostscript did not produce an output PDF file.");
                    return null;
                }

                byte[] pdf = File.ReadAllBytes(tempPdfFile);
                LOGGER.Debug("Conversion finished without errors.");
                return pdf;
            }
            catch (Exception ex)
            {
                LOGGER.Error("Exception when converting PostScript document to PDF format:", ex);
                return null;
            }
            finally
            {
                try { if (tempPsFile != null && File.Exists(tempPsFile)) File.Delete(tempPsFile); } catch { }
                try { if (File.Exists(tempPdfFile)) File.Delete(tempPdfFile); } catch { }
            }
        }

        /// <summary>
        /// Builds the Ghostscript switch list shared by both input paths.
        /// The PDF quality preset is configurable via the <c>PDF_SETTINGS</c> app setting
        /// (default <c>/printer</c>). Use <c>/prepress</c> for maximum quality/size,
        /// <c>/ebook</c> or <c>/screen</c> for smaller/faster output.
        /// </summary>
        private static List<string> BuildGhostscriptSwitches(string outputPdfPath)
        {
            string pdfSettings = ConfigurationManager.AppSettings[PDF_SETTINGS_KEY] ?? DEFAULT_PDF_SETTINGS;
            if (String.IsNullOrWhiteSpace(pdfSettings))
                pdfSettings = DEFAULT_PDF_SETTINGS;
            pdfSettings = pdfSettings.Trim();
            if (!pdfSettings.StartsWith("/"))
                pdfSettings = "/" + pdfSettings;

            LOGGER.Debug($"Using PDF quality preset \"{pdfSettings}\" (configurable via {PDF_SETTINGS_KEY}).");

            return new List<string>
            {
                "-dCompatibilityLevel=1.4",
                "-dSAFER",
                "-dBATCH",
                "-dNOPAUSE",
                "-dNOPROMPT",
                "-dPDFSETTINGS=" + pdfSettings,
                "-dASCII85EncodePages=false",
                // Performance / size: skip per-page rotation analysis and reuse identical images.
                "-dAutoRotatePages=/None",
                "-dDetectDuplicateImages=true",
                // Use all available cores for any rasterization work.
                "-dNumRenderingThreads=" + Environment.ProcessorCount,
                "-sDEVICE=pdfwrite",
                "-sOutputFile=" + outputPdfPath
            };
        }

        private static bool DoesCommandLineParameterExist(string[] args, string cmdArgNameLower)
        {
            return args.Any(arg => arg.ToLower() == cmdArgNameLower);
        }

        private static string GetValueFromCommandLineParameter(string[] args, string cmdArgNameLower)
        {
            var cmdArgIndex = Array.FindIndex(args, arg => arg.ToLower() == cmdArgNameLower);

            return cmdArgIndex >= 0 && args.Length > cmdArgIndex + 1 ? args[cmdArgIndex + 1] : String.Empty;
        }

        private static bool IsOldInstanceRunning()
        {
            Process currentProcess = Process.GetCurrentProcess();

            foreach (Process prc in Process.GetProcessesByName("miPDFconvert"))
            {
                if (prc.Id == currentProcess.Id)
                    continue;
                try
                {
                    if (prc.StartTime < currentProcess.StartTime)
                        return true;
                }
                catch
                {
                    // Process already exited or its start time is not accessible - ignore it.
                }
            }

            return false;
        }

        /// <summary>
        /// Locates the installed Ghostscript console executable (gswin64c.exe / gswin32c.exe).
        /// Scans both registry views (64- and 32-bit) and the GPL and Artifex products, derives
        /// the bin directory from each registered GS_DLL and picks the highest version whose
        /// console executable exists, preferring the 64-bit build. Because Ghostscript runs as a
        /// separate process, its bitness is independent of this application's - either build works.
        /// </summary>
        private static string FindGhostscriptExecutable()
        {
            var candidates = new List<(Version version, string exePath, bool is64)>();

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (string product in new[] { "GPL Ghostscript", "Artifex Ghostscript" })
                {
                    try
                    {
                        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                        using RegistryKey? productKey = baseKey.OpenSubKey($@"SOFTWARE\{product}");
                        if (productKey == null)
                            continue;

                        foreach (string versionName in productKey.GetSubKeyNames())
                        {
                            using RegistryKey? versionKey = productKey.OpenSubKey(versionName);
                            string? dll = versionKey?.GetValue("GS_DLL") as string;
                            string? binDir = String.IsNullOrEmpty(dll) ? null : Path.GetDirectoryName(dll);
                            if (String.IsNullOrEmpty(binDir))
                                continue;

                            bool is64 = dll!.EndsWith("gsdll64.dll", StringComparison.OrdinalIgnoreCase);
                            string[] exeNames = is64
                                ? new[] { "gswin64c.exe", "gswin64.exe" }
                                : new[] { "gswin32c.exe", "gswin32.exe" };

                            foreach (string exeName in exeNames)
                            {
                                string exePath = Path.Combine(binDir, exeName);
                                if (File.Exists(exePath))
                                {
                                    Version version = Version.TryParse(versionName, out Version? v) ? v : new Version(0, 0, 0);
                                    candidates.Add((version, exePath, is64));
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Debug($"    Could not read registry for \"{product}\" ({view}): {ex.Message}");
                    }
                }
            }

            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    "Ghostscript is not installed. Please install Ghostscript (32-bit or 64-bit) from https://www.ghostscript.com/releases/gsdnld.html.");

            // Highest version wins; on a tie prefer the 64-bit build.
            var best = candidates.OrderByDescending(c => c.version).ThenByDescending(c => c.is64).First();
            LOGGER.Info($"    Using Ghostscript executable \"{best.exePath}\" (version {best.version}).");
            return best.exePath;
        }

    }

}
