using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("FramePrint Setup")]
[assembly: AssemblyProduct("FramePrint")]
[assembly: AssemblyVersion("1.5.0.0")]

namespace FramePrintSetup
{
    static class Program
    {
        const string Version = "1.5.0";
        const string Title = "FramePrint " + Version;
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FramePrint";

        static string BundleDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Autodesk\ApplicationPlugins\FramePrint.bundle"); }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            bool uninstall = args.Length > 0 && args[0].Trim('/', '-').Equals("uninstall", StringComparison.OrdinalIgnoreCase);
            try { return uninstall ? Uninstall() : Install(); }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        static bool WaitAcadClosed()
        {
            while (Process.GetProcessesByName("acad").Length > 0)
            {
                if (MessageBox.Show("Закройте AutoCAD и нажмите «Повтор».", Title,
                        MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return false;
            }
            return true;
        }

        static void Extract(string resource, string path)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (s == null) throw new InvalidOperationException("В установщике нет файла " + resource);
                using (FileStream f = File.Create(path)) s.CopyTo(f);
            }
        }

        static int Install()
        {
            if (MessageBox.Show("Установить " + Title + " — пакетная печать рамок в PDF для AutoCAD 2021–2024?",
                    Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1;
            if (!WaitAcadClosed()) return 1;

            string dir = BundleDir;
            Directory.CreateDirectory(Path.Combine(dir, "Contents"));
            Extract("PackageContents.xml", Path.Combine(dir, "PackageContents.xml"));
            Extract("FramePrint.dll", Path.Combine(dir, @"Contents\FramePrint.dll"));

            // копия установщика — для удаления из «Приложений»
            string self = Assembly.GetExecutingAssembly().Location;
            string uninst = Path.Combine(dir, "FramePrint_Setup.exe");
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninst, true);

            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", "FramePrint — пакетная печать рамок (AutoCAD)");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "akproekt");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", uninst);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }

            MessageBox.Show(Title + " установлен.\n\nЗапустите AutoCAD и введите команду FPRINT.\n" +
                "Если AutoCAD спросит про загрузку — выберите «Всегда загружать».\n\n" +
                "Удаление: Параметры Windows → Приложения → FramePrint.",
                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        static int Uninstall()
        {
            if (MessageBox.Show("Удалить FramePrint из AutoCAD?\nНастройки и шаблоны (%APPDATA%\\FramePrint) сохранятся.",
                    Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 1;
            if (!WaitAcadClosed()) return 1;
            try { Registry.CurrentUser.DeleteSubKeyTree(RegKey); } catch { }
            string dir = BundleDir;
            try { Directory.Delete(Path.Combine(dir, "Contents"), true); } catch { }
            try { File.Delete(Path.Combine(dir, "PackageContents.xml")); } catch { }
            // сам установщик запущен из этой папки — удалить её чуть позже
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true; psi.UseShellExecute = false;
            Process.Start(psi);
            MessageBox.Show("FramePrint удалён.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }
}
