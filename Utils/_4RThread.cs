using System;
using System.Threading;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _4RTools.Utils
{
    public class _4RThread
    {
        private readonly Thread thread;

        // Replaces deprecated Thread.Suspend(); Stop() just flips this off.
        private volatile bool running = true;
        public bool Running => running;

        // Passes itself to toRun so a feature's own inner loop (e.g. AHK/ATKDEFMode
        // spamming while a key is held) can check Running without going through a
        // mutable field that might get reassigned by a later Start().
        public _4RThread(Func<_4RThread, int> toRun)
        {
            this.thread = new Thread(() =>
            {
                while (running)
                {
                    try
                    {
                        toRun(this);
                    }catch(Exception ex) {
                        Console.WriteLine("[4RThread Exception] Error while Executing Thread Method ==== "+ex.Message);
                    }
                    finally
                    {
                        Thread.Sleep(5);
                    }
                }
            });
            this.thread.SetApartmentState(ApartmentState.STA);
        }

        public static void Start(_4RThread _4RThread)
        {
            _4RThread.thread.Start();
        }

        public static void Stop(_4RThread _4RThread)
        {
            if (_4RThread != null)
            {
                _4RThread.running = false;
            }
        }
    }
}
