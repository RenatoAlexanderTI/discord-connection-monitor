using System;
using System.Windows.Forms;

namespace DiscordMonitor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Single instance check
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "DiscordMonitor_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Discord Monitor ya está en ejecución.", "Discord Monitor",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.Run(new MainForm());
            }
        }
    }
}
