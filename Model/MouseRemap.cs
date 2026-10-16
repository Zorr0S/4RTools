using System;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    // Replicates a simple AHK-style "mouse side button -> keyboard key" remap
    // directly in 4RTools (the user was running a separate AutoHotkey script for
    // exactly this - XButton1/XButton2 to a key - alongside the tool). Polls
    // physical button state the same way Model/StatusCapture.cs's Skill Triggers
    // do (Interop.GetAsyncKeyState, not System.Windows.Input.Keyboard.IsKeyDown -
    // the latter only reflects what a message-pumping thread has seen, and this
    // runs on a plain background Thread with none).
    public class MouseRemap : Action
    {
        public static string ACTION_NAME_MOUSE_REMAP = "MouseRemap";

        public Key XButton1Key { get; set; } = Key.None;
        public Key XButton2Key { get; set; } = Key.None;

        private _4RThread thread;

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                this.thread = RemapThread();
                _4RThread.Start(this.thread);
            }
        }

        private _4RThread RemapThread()
        {
            bool wasX1Down = false;
            bool wasX2Down = false;

            _4RThread remapThread = new _4RThread(_ =>
            {
                bool x1Down = Interop.IsKeyPhysicallyDown(Keys.XButton1);
                bool x2Down = Interop.IsKeyPhysicallyDown(Keys.XButton2);

                if (x1Down && !wasX1Down) SendKey(XButton1Key);
                if (x2Down && !wasX2Down) SendKey(XButton2Key);

                wasX1Down = x1Down;
                wasX2Down = x2Down;

                Thread.Sleep(15);
                return 0;
            });

            return remapThread;
        }

        private static void SendKey(Key key)
        {
            if (key == Key.None) return;

            Client client = ClientSingleton.GetClient();
            if (client == null || client.process == null) return;

            try
            {
                Keys formsKey = (Keys)Enum.Parse(typeof(Keys), key.ToString());
                Interop.PostMessage(client.process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, formsKey, 0);
            }
            catch
            {
                // Malformed/unmappable key string: nothing sensible to send, skip it.
            }
        }

        public void Stop()
        {
            _4RThread.Stop(this.thread);
        }

        public string GetConfiguration()
        {
            return JsonConvert.SerializeObject(this);
        }

        public string GetActionName()
        {
            return ACTION_NAME_MOUSE_REMAP;
        }
    }
}
