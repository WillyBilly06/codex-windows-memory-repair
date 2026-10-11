using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class CodexFixedLauncher
{
    [STAThread]
    private static int Main()
    {
        string root = Path.GetDirectoryName(Application.ExecutablePath);
        try
        {
            string app = Path.GetFullPath(Path.Combine(root, "app", "ChatGPT.exe"));
            string profile = Path.GetFullPath(Path.Combine(root, "browser-profile"));
            if (app.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0 || !File.Exists(app))
                throw new InvalidOperationException("The separate fixed application copy was not found.");
            foreach (Process running in Process.GetProcessesByName("ChatGPT"))
            {
                using (running)
                {
                    try
                    {
                        if (String.Equals(running.MainModule.FileName, app, StringComparison.OrdinalIgnoreCase)
                            && running.MainWindowHandle != IntPtr.Zero)
                            return 0;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            Directory.CreateDirectory(profile);
            ProcessStartInfo start = new ProcessStartInfo(app);
            start.WorkingDirectory = Path.GetDirectoryName(app);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.Arguments = "--user-data-dir=\"" + profile + "\"";
            start.EnvironmentVariables["CODEX_ELECTRON_USER_DATA_PATH"] = profile;
            start.EnvironmentVariables["CODEX_SPARKLE_ENABLED"] = "false";
            using (Process process = Process.Start(start))
            {
                Dictionary<string, object> receipt = new Dictionary<string, object>();
                receipt["pid"] = process.Id;
                receipt["at"] = DateTime.UtcNow.ToString("o");
                receipt["exe"] = app;
                receipt["profile"] = profile;
                receipt["diagnostics"] = false;
                receipt["launcher"] = "Codex-Fixed.exe";
                File.WriteAllText(Path.Combine(root, "latest-launch.json"),
                    new JavaScriptSerializer().Serialize(receipt) + Environment.NewLine, new UTF8Encoding(false));
            }
            return 0;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(root, "launch-error.log"), error.ToString(), new UTF8Encoding(false)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            MessageBox.Show(error.Message, "Codex Fixed could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
