using System;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    // Replaces a separate AutoHotkey script (the user was running one just for
    // this) with a key-bind for each mouse side button directly in 4RTools -
    // Model/MouseRemap.cs polls XButton1/XButton2 and sends the configured key
    // to the game the same way Autopot/AutoBuff/AHK already send keys.
    public class MouseRemapForm : Form, IObserver
    {
        private TextBox txtXButton1;
        private TextBox txtXButton2;

        public MouseRemapForm(Subject subject)
        {
            this.KeyPreview = true;
            BuildUi();
            subject.Attach(this);
        }

        private void BuildUi()
        {
            this.Text = "Mouse Remap";

            Label hint = new Label
            {
                Text = "Bind a keyboard key to each mouse side button - pressing it in-game sends that key, no separate AutoHotkey script needed.",
                Location = new System.Drawing.Point(10, 10),
                AutoSize = true
            };

            Label lblX1 = new Label { Text = "Side Button 1 (Back / XButton1):", Location = new System.Drawing.Point(10, 45), AutoSize = true };
            txtXButton1 = new TextBox { Location = new System.Drawing.Point(230, 42), Width = 100, ReadOnly = true, Text = Key.None.ToString() };
            txtXButton1.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtXButton1.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtXButton1.TextChanged += TxtXButton1_TextChanged;

            Label lblX2 = new Label { Text = "Side Button 2 (Forward / XButton2):", Location = new System.Drawing.Point(10, 80), AutoSize = true };
            txtXButton2 = new TextBox { Location = new System.Drawing.Point(230, 77), Width = 100, ReadOnly = true, Text = Key.None.ToString() };
            txtXButton2.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtXButton2.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            txtXButton2.TextChanged += TxtXButton2_TextChanged;

            this.Controls.Add(hint);
            this.Controls.Add(lblX1);
            this.Controls.Add(txtXButton1);
            this.Controls.Add(lblX2);
            this.Controls.Add(txtXButton2);
        }

        private void TxtXButton1_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Key key = (Key)Enum.Parse(typeof(Key), txtXButton1.Text);
                ProfileSingleton.GetCurrent().MouseRemap.XButton1Key = FormUtils.IsValidKey(key) ? key : Key.None;
                ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().MouseRemap);
            }
            catch { }
        }

        private void TxtXButton2_TextChanged(object sender, EventArgs e)
        {
            try
            {
                Key key = (Key)Enum.Parse(typeof(Key), txtXButton2.Text);
                ProfileSingleton.GetCurrent().MouseRemap.XButton2Key = FormUtils.IsValidKey(key) ? key : Key.None;
                ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().MouseRemap);
            }
            catch { }
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    txtXButton1.Text = ProfileSingleton.GetCurrent().MouseRemap.XButton1Key.ToString();
                    txtXButton2.Text = ProfileSingleton.GetCurrent().MouseRemap.XButton2Key.ToString();
                    break;
                case MessageCode.TURN_ON:
                    ProfileSingleton.GetCurrent().MouseRemap.Start();
                    break;
                case MessageCode.TURN_OFF:
                    ProfileSingleton.GetCurrent().MouseRemap.Stop();
                    break;
            }
        }
    }
}
