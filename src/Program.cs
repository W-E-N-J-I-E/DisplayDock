using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DisplayDock
{
    internal static class Program
    {
        private const string Title = "显示器切换器";

        [STAThread]
        private static void Main(string[] args)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            bool resetConfig = false;
            foreach (string arg in args)
            {
                if (String.Equals(arg, "--reset-config", StringComparison.OrdinalIgnoreCase)) resetConfig = true;
            }
            if (resetConfig)
            {
                try
                {
                    string path = Path.Combine(baseDirectory, ConfigFile.FileName);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception) { }
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\DisplayDock.SingleInstance." + Environment.UserName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("“" + Title + "”已经在运行了，请查看任务栏或通知区域的图标。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    ReportCrash(baseDirectory, "界面线程异常", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    ReportCrash(baseDirectory, "未处理异常", e.ExceptionObject as Exception);
                };

                AppService service = null;
                try
                {
                    service = new AppService(baseDirectory);
                    service.Initialize();
                    using (var form = new MainForm(service))
                    {
                        Application.Run(form);
                    }
                }
                catch (Exception ex)
                {
                    ReportCrash(baseDirectory, "启动失败", ex);
                    MessageBox.Show("程序启动失败：" + ex.Message + Environment.NewLine + Environment.NewLine +
                        "详细信息已写入 logs\\error.log，请把该文件发给维护者。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (service != null)
                    {
                        try { service.Shutdown(); }
                        catch (Exception) { }
                        try { service.Dispose(); }
                        catch (Exception) { }
                    }
                }
            }
        }

        private static void ReportCrash(string baseDirectory, string caption, Exception exception)
        {
            if (exception == null) return;
            try
            {
                string directory = Path.Combine(baseDirectory, "logs");
                Directory.CreateDirectory(directory);
                var builder = new StringBuilder();
                builder.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + caption);
                builder.AppendLine(exception.ToString());
                builder.AppendLine();
                File.AppendAllText(Path.Combine(directory, "error.log"), builder.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
            try
            {
                string message = exception.Message;
                if (message.Length > 800) message = message.Substring(0, 800);
                MessageBox.Show(caption + "：" + message + Environment.NewLine + "（已写入 logs\\error.log）", Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }
    }
}
