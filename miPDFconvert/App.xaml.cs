// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// Licensed under the Apache License, Version 2.0 (see LICENSE).

using Microsoft.Win32;
using MiPdf;
using MiPdf.PostScript;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace miPDFconvert
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string DOCUMENT_TARGET_KEY = "TARGET_APPLICATION";
        private const string CREATE_FILES_KEY = "CREATE_DOCUMENT_FILES";
        private const int CONVERSION_TIMEOUT_SECONDS = 120;
        public void App_Startup(object sender, StartupEventArgs e)
        {
            // Record any unhandled crash; the log does not depend on the application configuration.
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                Log.Error("Unhandled exception (AppDomain): " + ev.ExceptionObject);
            this.DispatcherUnhandledException += (s, ev) =>
            {
                Log.Error("Unhandled exception (Dispatcher):", ev.Exception);
                ev.Handled = true;
                Environment.Exit(1);
            };
            Log.Info($"miPDFconvert started by {Environment.UserName}.");
            CleanupOldTempPdfFiles();

            // Check if there is another miPDFconvert process running, if so, wait until it is
            // closed. The wait is bounded: a single hung instance (unanswered dialog, a conversion
            // stuck on malformed PostScript) must not silently block every following print job
            // forever - after the timeout we continue and serve this job anyway.
            var instanceWait = Stopwatch.StartNew();
            while (IsOldInstanceRunning())
            {
                if (instanceWait.Elapsed.TotalSeconds > 60)
                {
                    Log.Warn("An older miPDFconvert instance is still running after 60 s - continuing anyway.");
                    break;
                }
                Log.Debug("Waiting for old miPDFconvert processes to end...");
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
                const string usage = "miPDFconvert started without any arguments! Use -psfile <PostScript file> [-title <document title>] or specify a PDF file path as argument.";
                Log.Error(usage);
                ShowErrorMessage(usage);
                Environment.Exit(1);
                return;
            }

            Log.Info($"miPDFconvert started with the following arguments: {String.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg))}.");

            if (DoesCommandLineParameterExist(args, "-psfile"))
            {
                psInputFilePath = GetValueFromCommandLineParameter(args, "-psfile");

                if (String.IsNullOrEmpty(psInputFilePath))
                {
                    Log.Error($"miPDFconvert argument '-psfile' requires a PostScript file path as next argument!");
                    ShowErrorMessage("miPDFconvert argument '-psfile' requires a PostScript file path as next argument!");
                    Environment.Exit(1);
                    return;
                }

                // The print monitor passes the document title separately (the spool file has a
                // neutral name); it becomes the suggested file name of the PDF.
                string title = GetValueFromCommandLineParameter(args, "-title");
                inputFileName = String.IsNullOrWhiteSpace(title)
                    ? Path.GetFileName(psInputFilePath)
                    : SanitizeFileName(Path.GetFileNameWithoutExtension(title)) + ".ps";
                Log.Info($"Got argument '{psInputFilePath}' as PostScript input file path (document title '{title}')...");
            }
            else
            {
                pdfInputFilePath = String.Join(" ", args);
                inputFileName = Path.GetFileName(pdfInputFilePath);
                Log.Info($"miPDFconvert started with command line argument '{pdfInputFilePath}'. Interpreting this as PDF input file path.");
            }
            Boolean.TryParse(ConfigurationManager.AppSettings[CREATE_FILES_KEY], out bool createDocumentFiles);
            byte[]? pdf = GetPdfDocument(pdfInputFilePath, psInputFilePath, createDocumentFiles);

            // The spool file is consumed once the PDF exists - delete it right away.
            if (pdf != null && !String.IsNullOrEmpty(psInputFilePath))
            {
                try
                {
                    File.Delete(psInputFilePath);
                    Log.Debug($"Spool file \"{psInputFilePath}\" deleted.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not delete spool file \"{psInputFilePath}\": {ex.Message}");
                }
            }

            ProcessInput(pdf, inputFileName, createDocumentFiles);
        }

        private static void ProcessInput(byte[]? pdf, string fileName, bool createDocumentFiles)
        {
            if (pdf == null)
            {
                Log.Error("No PDF document available! Quitting application.");
                Environment.Exit(1);
                return;
            }

            Log.Info($"Document name is '{fileName}'.");

            // Create document file for temporary testing purposes if turned on
            if (createDocumentFiles)
            {
                string strPdfFilePath = String.Empty;
                try
                {
                    strPdfFilePath = Path.Combine(Path.GetTempPath(), "miPDFconvert.pdf");
                    Log.Debug($"Saving PDF document as \"{strPdfFilePath}\"...");
                    File.WriteAllBytes(strPdfFilePath, pdf);
                    Log.Debug("PDF document saved.");
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception when saving PDF document to \"{strPdfFilePath}\". Saving document is intended only for testing purposes and can be controlled by the {CREATE_FILES_KEY} property in the application configuration.", ex);
                    // Continue without saving
                }
            }

            // Map target application setting
            string? documentTarget = ConfigurationManager.AppSettings[DOCUMENT_TARGET_KEY]?.Trim();
            Log.Debug($"{DOCUMENT_TARGET_KEY} value \"{documentTarget}\" is configured in application configuration.");

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
                        Log.Info($"PDF document saved to \"{saveFileDialog.FileName}\".");
                        Environment.Exit(0);
                    }
                    else
                    {
                        Log.Info("Save file dialog was cancelled by user. Exiting application.");
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
                    Log.Info($"Passing PDF document to target application \"{resolvedTarget}\"...");
                    // Create temporary file for PDF document. The "miPDFconvert_" prefix lets
                    // CleanupOldTempPdfFiles identify and remove stale files on later runs
                    // (the file cannot be deleted right away - the target application reads it).
                    string tempPdfFilePath = Path.Combine(Path.GetTempPath(), $"miPDFconvert_{Guid.NewGuid():N}.pdf");
                    File.WriteAllBytes(tempPdfFilePath, pdf);
                    Log.Debug($"Temporary PDF file created as \"{tempPdfFilePath}\".");
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
                            Log.Error($"Could not start target application \"{resolvedTarget}\"!");
                            ShowErrorMessage($"Could not start target application \"{resolvedTarget}\"!");
                            Environment.Exit(1);
                        }
                        else
                        {
                            Log.Info($"Target application \"{resolvedTarget}\" started successfully (PID {process.Id}).");
                            BringProcessToForeground(process, foregroundOwner);
                            // IMPORTANT: exit explicitly. This is a WPF application without a
                            // main window, so without this call the dispatcher loop would keep
                            // running forever, the process would never terminate and every
                            // following print job would wait for it (see IsOldInstanceRunning).
                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Exception when starting target application \"{resolvedTarget}\": ", ex);
                        ShowErrorMessage($"Exception when starting target application \"{resolvedTarget}\": {ex.Message}");
                        Environment.Exit(1);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Exception when passing PDF to target application: ", ex);
                ShowErrorMessage($"Exception when passing PDF to target application: {ex.Message}");
                Environment.Exit(1);
            }
            finally
            {
                Log.Info($"Exiting miPDFconvert application with code {Environment.ExitCode}.");
            }
        }

        /// <summary>
        /// Creates a hidden, topmost window that is activated and can be used as an owner for
        /// dialogs (e.g. the SaveFileDialog). Because the application runs without a visible main
        /// window, dialogs would otherwise have no owner and could appear behind other applications.
        /// The print monitor starts miPDFconvert in the background, so a plain Activate() is
        /// refused by Windows' foreground lock: the owner is forced into the foreground instead,
        /// which gives the dialog the keyboard focus as well.
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
            ForceForegroundWindow(new WindowInteropHelper(ownerWindow).Handle);
            ownerWindow.Activate();
            return ownerWindow;
        }

        /// <summary>
        /// Brings a window to the foreground although this process is not the foreground process:
        /// attaching to the input queue of the current foreground thread lets Windows accept the
        /// SetForegroundWindow call.
        /// </summary>
        private static bool ForceForegroundWindow(IntPtr hWnd)
        {
            IntPtr foreground = GetForegroundWindow();
            uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = AttachThreadInput(currentThread, foregroundThread, true);

            try
            {
                BringWindowToTop(hWnd);
                return SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
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
                            Log.Debug($"Old temporary PDF \"{file}\" deleted.");
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
                    Log.Warn($"No main window found for target process {process.Id} (likely a single-instance/broker app); relied on ASFW_ANY fallback.");
                    return;
                }

                // Grant the concrete target process the right to set itself foreground, even after we
                // exit, then activate its window directly.
                AllowSetForegroundWindow(process.Id);

                if (IsIconic(hWnd))
                    ShowWindow(hWnd, SW_RESTORE);

                ForceForegroundWindow(hWnd);
                Log.Info($"Target window of process {process.Id} brought to the foreground.");
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to bring target process {process.Id} to the foreground: {ex.Message}");
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
                    Log.Info($"Reading PDF input document from \"{pdfFilePath}\"...");
                    byte[] pdfBinary = File.ReadAllBytes(pdfFilePath);
                    Log.Info($"Input document successfully loaded with length of {pdfBinary.Length} bytes.");
                    return pdfBinary;
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception when reading PDF input document from \"{pdfFilePath}\":", ex);
                    ShowErrorMessage($"Exception when reading PDF input document from \"{pdfFilePath}\": {ex.Message}");
                    return null;
                }
            }

            // Keep a copy of the print job for diagnostics (e.g. to attach it to an issue) if turned on
            if (createDocumentFiles)
            {
                string strPostScriptFilePath = String.Empty;
                try
                {
                    strPostScriptFilePath = Path.Combine(Path.GetTempPath(), "miPDFconvert.ps");
                    Log.Debug($"Saving a copy of the PostScript document as \"{strPostScriptFilePath}\"...");
                    File.Copy(psInputFilePath, strPostScriptFilePath, true);
                }
                catch (Exception ex)
                {
                    Log.Error($"Exception when saving PostScript document to \"{strPostScriptFilePath}\". Saving document is intended only for testing purposes and can be controlled by the {CREATE_FILES_KEY} property in the application configuration.", ex);
                    // Continue without saving
                }
            }

            Log.Info($"Converting PostScript input file \"{psInputFilePath}\"...");
            byte[]? createdPdfBinary = ConvertPS2Pdf(psInputFilePath);

            if ((createdPdfBinary?.Length ?? 0) == 0)
            {
                Log.Error("No PDF document could be created from PostScript document!");
                ShowErrorMessage("No PDF document could be created from PostScript document!");
                return null;
            }

            Log.Info($"PDF document created with length of {createdPdfBinary?.Length} bytes.");
            return createdPdfBinary;
        }

        /// <summary>
        /// Converts a PostScript file to PDF with miPDFlib's managed PostScript interpreter
        /// (miPDFlib.PostScript), in this process and without any external engine.
        /// </summary>
        private static byte[]? ConvertPS2Pdf(string psFilePath)
        {
            try
            {
                var options = new PostScriptOptions
                {
                    // The printer driver states the page size and orientation itself; do not let
                    // the converter guess an orientation from the text direction.
                    AutoRotatePages = false,
                    // A job that hangs must not block every following print job (see the bounded
                    // instance wait in App_Startup).
                    Timeout = TimeSpan.FromSeconds(CONVERSION_TIMEOUT_SECONDS)
                };

                var conversionTime = Stopwatch.StartNew();
                using FileStream psStream = File.OpenRead(psFilePath);
                using PdfDocument document = PostScriptConverter.ToPdf(psStream, options);
                byte[] pdf = document.ToArray();
                Log.Debug($"Conversion finished without errors ({document.Pages.Count} page(s), {conversionTime.ElapsedMilliseconds} ms).");
                return pdf;
            }
            catch (PostScriptException ex)
            {
                Log.Error($"PostScript error \"{ex.ErrorName}\" (offending command: {ex.OffendingCommand}) when converting the document to PDF. Program output: {ex.Output}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Exception when converting PostScript document to PDF format:", ex);
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return String.IsNullOrWhiteSpace(name) ? "document" : name.Trim();
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

    }

}
