using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class RepairInstaller
{
    internal static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    internal static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue, RecursionLimit = 500 };
    internal static Dictionary<string, object> Obj(object o) { return (Dictionary<string, object>)o; }
    internal static object[] Arr(object o) { return (object[])o; }
    internal static byte[] Resource(string name) { using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) { using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); } } }
    internal static string Hash(byte[] data) { using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }
    internal static string HashFile(string path) { using (FileStream f = File.OpenRead(path)) using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    internal static string Canonical(string p) { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar); }
    internal static bool Within(string p, string root) { return p.Equals(root, StringComparison.OrdinalIgnoreCase) || p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
    internal static void RejectReparse(string path)
    {
        for (string p = path; !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are unsupported: " + p);
    }
    internal static string MemberPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("Absolute manifest path rejected.");
        string p = Canonical(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (p.Equals(root, StringComparison.OrdinalIgnoreCase) || !Within(p, root)) throw new IOException("Unsafe manifest path.");
        RejectReparse(p); return p;
    }
    internal static Dictionary<string, object> Header(byte[] prefix)
    {
        if (prefix.Length < 16 || BitConverter.ToUInt32(prefix, 0) != 4) throw new IOException("Invalid ASAR header.");
        int length = checked((int)BitConverter.ToUInt32(prefix, 12));
        if (length < 2 || length > prefix.Length - 16) throw new IOException("Truncated ASAR header.");
        return Obj(Json.DeserializeObject(Utf8.GetString(prefix, 16, length)));
    }
    internal static byte[] ReadHeader(Stream f)
    {
        byte[] first = ReadExactly(f, 16);
        int total = checked((int)BitConverter.ToUInt32(first, 4) + 8);
        if (total < 16 || total > 32 * 1024 * 1024) throw new IOException("Unsupported ASAR header length.");
        byte[] prefix = new byte[total]; Buffer.BlockCopy(first, 0, prefix, 0, 16);
        byte[] rest = ReadExactly(f, total - 16); Buffer.BlockCopy(rest, 0, prefix, 16, rest.Length); return prefix;
    }
    internal static byte[] ReadExactly(Stream f, int count)
    {
        byte[] b = new byte[count]; int offset = 0;
        while (offset < count) { int read = f.Read(b, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        return b;
    }
    internal static void Entries(Dictionary<string, object> tree, string prefix, Dictionary<string, Dictionary<string, object>> result)
    {
        foreach (var pair in Obj(tree["files"]))
        {
            var e = Obj(pair.Value); string name = prefix + pair.Key;
            if (e.ContainsKey("files")) Entries(e, name + "/", result); else result.Add(name, e);
        }
    }
    internal static byte[] Apply(byte[] original, Dictionary<string, object> patch)
    {
        if (Hash(original) != (string)patch["originalSha256"]) throw new IOException("Original JavaScript hash mismatch.");
        using (MemoryStream output = new MemoryStream())
        {
            byte[] prefix = Utf8.GetBytes((string)patch["prefix"]); output.Write(prefix, 0, prefix.Length);
            int cursor = 0;
            foreach (var edit in Arr(patch["edits"]).Select(Obj).OrderBy(e => Convert.ToInt32(e["start"])))
            {
                int start = Convert.ToInt32(edit["start"]), remove = Convert.ToInt32(edit["remove"]);
                if (start < cursor || start > original.Length || remove < 0 || remove > original.Length - start) throw new IOException("Invalid patch offsets.");
                output.Write(original, cursor, start - cursor); byte[] insert = Utf8.GetBytes((string)edit["insert"]); output.Write(insert, 0, insert.Length); cursor = start + remove;
            }
            output.Write(original, cursor, original.Length - cursor); byte[] result = output.ToArray();
            if (Hash(result) != (string)patch["patchedSha256"]) throw new IOException("Patched JavaScript hash mismatch."); return result;
        }
    }
    internal static void BuildArchive(string original, string output, Dictionary<string, object> manifest)
    {
        byte[] targetPrefix;
        using (GZipStream gz = new GZipStream(new MemoryStream(Resource("header.gz")), CompressionMode.Decompress))
        using (MemoryStream m = new MemoryStream()) { gz.CopyTo(m); targetPrefix = m.ToArray(); }
        var next = new Dictionary<string, Dictionary<string, object>>(); Entries(Header(targetPrefix), "", next);
        var patches = Arr(manifest["patches"]).Select(Obj).ToDictionary(p => (string)p["member"]);
        using (FileStream source = File.OpenRead(original))
        {
            byte[] oldPrefix = ReadHeader(source); var old = new Dictionary<string, Dictionary<string, object>>(); Entries(Header(oldPrefix), "", old);
            using (FileStream dest = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            {
                dest.Write(targetPrefix, 0, targetPrefix.Length);
                byte[] buffer = new byte[1024 * 1024];
                foreach (var pair in next.Where(p => !p.Value.ContainsKey("unpacked") && !p.Value.ContainsKey("link")).OrderBy(p => Convert.ToInt64(p.Value["offset"])))
                {
                    var prior = old[pair.Key]; long size = Convert.ToInt64(prior["size"]);
                    if (dest.Position != targetPrefix.Length + Convert.ToInt64(pair.Value["offset"])) throw new IOException("Unexpected archive offset.");
                    source.Position = oldPrefix.Length + Convert.ToInt64(prior["offset"]);
                    if (patches.ContainsKey(pair.Key)) { byte[] b = Apply(ReadExactly(source, checked((int)size)), patches[pair.Key]); dest.Write(b, 0, b.Length); }
                    else while (size > 0) { int n = source.Read(buffer, 0, (int)Math.Min(size, buffer.Length)); if (n == 0) throw new EndOfStreamException(); dest.Write(buffer, 0, n); size -= n; }
                }
            }
        }
        if (HashFile(output) != (string)manifest["copyArchiveSha256"]) throw new IOException("Rebuilt archive failed final SHA-256 verification.");
    }
    internal static void Install(string sourceInput, string destinationInput, Action<string> report)
    {
        var manifest = Obj(Json.DeserializeObject(Utf8.GetString(Resource("manifest.json"))));
        string source = Canonical(sourceInput), destination = Canonical(destinationInput);
        RejectReparse(source); RejectReparse(destination);
        if (!Directory.Exists(source)) throw new IOException("Select the installed app folder containing ChatGPT.exe.");
        if (destination.IndexOf("\\WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0 || Within(source, destination) || Within(destination, source)) throw new IOException("The destination must be a separate folder outside the installed application.");
        if (File.Exists(destination) || (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())) throw new IOException("The destination must be new or empty. Existing copies are never overwritten.");
        var files = Arr(manifest["sourceFiles"]).Select(Obj).ToArray();
        report("Verifying the installed x64 build. No original files will be changed.");
        foreach (var file in files)
        {
            string p = MemberPath(source, (string)file["path"]);
            if (!File.Exists(p) || new FileInfo(p).Length != Convert.ToInt64(file["size"]) || HashFile(p) != (string)file["sha256"])
                throw new IOException("Unsupported or modified installation: " + file["path"] + ". This installer supports only the exact Windows x64 26.1007.2314.0 build.");
        }
        Directory.CreateDirectory(destination);
        string marker = Path.Combine(destination, "INSTALL-INCOMPLETE.txt"); File.WriteAllText(marker, "Do not run this copy until installation completes.", Utf8);
        string app = Path.Combine(destination, "app"); Directory.CreateDirectory(app);
        report("Creating a private application copy. This may take a minute.");
        foreach (var file in files)
        {
            string relative = (string)file["path"], from = MemberPath(source, relative), to = MemberPath(app, relative);
            if (relative == "resources/app.asar") continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            using (FileStream input = File.OpenRead(from)) using (FileStream output = new FileStream(to, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
            if (HashFile(to) != (string)file["sha256"]) throw new IOException("Copied file verification failed: " + relative);
        }
        report("Applying the five JavaScript repairs and rebuilding ASAR integrity metadata.");
        BuildArchive(MemberPath(source, "resources/app.asar"), MemberPath(app, "resources/app.asar"), manifest);
        string exePath = MemberPath(app, "ChatGPT.exe"); byte[] exe = File.ReadAllBytes(exePath);
        byte[] before = Encoding.ASCII.GetBytes((string)manifest["oldHeaderSha256"]), after = Encoding.ASCII.GetBytes((string)manifest["newHeaderSha256"]);
        int at = Convert.ToInt32(manifest["exeHeaderOffset"]);
        if (at < 0 || at + before.Length > exe.Length || !exe.Skip(at).Take(before.Length).SequenceEqual(before)) throw new IOException("Unexpected executable integrity resource.");
        Buffer.BlockCopy(after, 0, exe, at, after.Length);
        if (Hash(exe) != (string)manifest["copyExecutableSha256"]) throw new IOException("Copied executable failed SHA-256 verification.");
        File.WriteAllBytes(exePath, exe);
        File.WriteAllText(MemberPath(app, "resources/owl-app.ini"), "[Owl]\r\nUserDataDirectoryName=CodexMemoryFixed\r\nAppVersion=26.1007.21434\r\n", Utf8);
        File.WriteAllBytes(Path.Combine(destination, "Codex-Fixed.exe"), Resource("launcher.exe"));
        Directory.CreateDirectory(Path.Combine(destination, "browser-profile"));
        var receipt = new Dictionary<string, object> { {"at", DateTime.UtcNow.ToString("o")}, {"sourceArchiveSha256", manifest["sourceArchiveSha256"]}, {"copyArchiveSha256", manifest["copyArchiveSha256"]}, {"copyExecutableSha256", manifest["copyExecutableSha256"]}, {"originalModified", false}, {"unofficialUnsignedCopy", true} };
        File.WriteAllText(Path.Combine(destination, "installation-receipt.json"), Json.Serialize(receipt), Utf8);
        File.Delete(marker); report("Complete. The original installation is unchanged. Open Codex-Fixed.exe in the destination folder.");
    }
    internal static string FindInstalled()
    {
        try
        {
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1).InstallLocation\"");
            start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            using (Process p = Process.Start(start)) { string root = p.StandardOutput.ReadToEnd().Trim(); if (p.WaitForExit(10000) && Directory.Exists(Path.Combine(root, "app"))) return Path.Combine(root, "app"); }
        }
        catch { }
        return "";
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--build-copy")
        {
            try { Install(args[1], args[2], s => { }); return 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "installer-test-error.txt"), e.ToString(), Utf8); return 1; }
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new InstallerForm()); return 0;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox source = new TextBox(), destination = new TextBox(), status = new TextBox();
    private readonly Button build = new Button(), open = new Button();
    private bool working;
    public InstallerForm()
    {
        Text = "Codex Fixed — unofficial local-copy installer"; ClientSize = new Size(700, 410); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        var notice = new Label { Text = "Windows x64 package 26.1007.2314.0 only. Creates a separate modified copy.\nThe copy is unofficial and its OpenAI signature is invalidated. Your installed app stays unchanged.", AutoSize = false, Bounds = new Rectangle(18, 16, 664, 45) }; Controls.Add(notice);
        Controls.Add(new Label { Text = "Installed app folder", AutoSize = true, Location = new Point(18, 76) }); source.SetBounds(18, 98, 570, 25); source.Text = RepairInstaller.FindInstalled(); Controls.Add(source);
        Controls.Add(new Label { Text = "New destination folder", AutoSize = true, Location = new Point(18, 139) }); destination.SetBounds(18, 161, 570, 25); destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexFixed", "26.1007.2314.0"); Controls.Add(destination);
        BrowseButton(source, 98); BrowseButton(destination, 161);
        status.Multiline = true; status.ReadOnly = true; status.ScrollBars = ScrollBars.Vertical; status.SetBounds(18, 204, 664, 130); status.Text = "No account data, conversations, or OpenAI application binaries are included in this installer.\r\nFull-history operations have explicit memory budgets. See README for limits."; Controls.Add(status);
        build.Text = "Create fixed copy"; build.SetBounds(18, 354, 155, 34); build.Click += BeginInstall; Controls.Add(build);
        open.Text = "Open Codex Fixed"; open.SetBounds(497, 354, 185, 34); open.Enabled = false; open.Click += (s, e) => Process.Start(new ProcessStartInfo(Path.Combine(destination.Text, "Codex-Fixed.exe")) { UseShellExecute = false, CreateNoWindow = true }); Controls.Add(open);
        FormClosing += (s, e) => { if (working) { e.Cancel = true; MessageBox.Show(this, "Please wait for copying and verification to finish.", Text); } };
    }
    private void BrowseButton(TextBox target, int top)
    {
        var b = new Button { Text = "Browse…", Bounds = new Rectangle(598, top - 1, 84, 27) };
        b.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { SelectedPath = target.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath; }; Controls.Add(b);
    }
    private void BeginInstall(object sender, EventArgs e)
    {
        string from = source.Text, to = destination.Text; working = true; foreach (Control c in Controls) if (c != status) c.Enabled = false;
        var worker = new BackgroundWorker { WorkerReportsProgress = true };
        worker.DoWork += (s, a) => RepairInstaller.Install(from, to, message => worker.ReportProgress(0, message));
        worker.ProgressChanged += (s, a) => status.AppendText("\r\n" + (string)a.UserState);
        worker.RunWorkerCompleted += (s, a) => { working = false; foreach (Control c in Controls) c.Enabled = true; open.Enabled = a.Error == null; if (a.Error != null) { status.AppendText("\r\nStopped: " + a.Error.Message); MessageBox.Show(this, a.Error.Message, "Installation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error); } else { build.Enabled = false; source.ReadOnly = destination.ReadOnly = true; } };
        worker.RunWorkerAsync();
    }
}
