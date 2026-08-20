using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Notify
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            NotifyManager.ShowNotification("No load", "Servers may not work right now.", 3f, NotifyState.Warning);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            NotifyManager.ShowNotification("No load", "Subscription expired.", 3f, NotifyState.Error);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            NotifyManager.ShowNotification("Long time no see", "Welcome back, idwit", 3f, NotifyState.Success);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            NotifyManager.ShowNotification(textBox1.Text, textBox2.Text, 3f, NotifyState.Neutral);
        }
    }
}
