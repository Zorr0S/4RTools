using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Input;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{
    // One status id ever observed in the client's status buffer, with when and
    // how often - used to tell "a real custom skill" apart from a one-off fluke,
    // and to let the user later match it against a SkillMapEntry by hand.
    public class CapturedStatus
    {
        public uint StatusId { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public int TimesSeen { get; set; }
    }

    // A key the user binds to "this is the hotkey I press to cast skill X" so
    // StatusCapture can watch for a status id appearing right after it's pressed
    // and auto-map it, instead of the user guessing which captured id was which
    // skill after the fact.
    public class SkillTrigger
    {
        public int SkillId { get; set; }
        public string SkillName { get; set; }
        public string Key { get; set; }
    }

    // Runtime-only (never persisted) record of what happened the last time a given
    // trigger key's rising edge was seen - lets the UI show whether the key press
    // was even detected at all, separately from whether the diff after it found a
    // clean single candidate to map. Keyed by SkillTrigger.Key in StatusCapture.
    public class TriggerDiagnostic
    {
        public DateTime LastFiredUtc { get; set; }
        public string LastResult { get; set; }
    }

    // Scans every slot of the client's status buffer on every tick and records
    // any status id we haven't already logged - this is the fallback for
    // identifying custom skills when the server source isn't available: play the
    // game normally, this quietly builds the list of every EFST id that showed up,
    // then the user labels them via SkillMapManager (see Forms/StatusCaptureForm).
    //
    // On top of that passive log, the user can register SkillTriggers: a key they
    // press to cast a known buff skill. When that key's rising edge is seen, we
    // snapshot the currently-active status ids, wait out a cast/animation window,
    // snapshot again, and diff. Exactly one new id => confident auto-map into
    // SkillMapManager. Zero or several => left for manual labeling in the grid,
    // same as everything the passive scan already finds.
    public class StatusCapture : Action
    {
        public static string ACTION_NAME_STATUS_CAPTURE = "StatusCapture";
        private static readonly string LogFileName = "CapturedStatusLog.json";
        private static readonly TimeSpan TriggerCaptureWindow = TimeSpan.FromSeconds(3);

        private static readonly object capturedLock = new object();
        private static Dictionary<uint, CapturedStatus> captured;
        private static HashSet<uint> currentlyActive = new HashSet<uint>();

        private static readonly object diagnosticsLock = new object();
        private static readonly Dictionary<string, TriggerDiagnostic> diagnostics = new Dictionary<string, TriggerDiagnostic>();

        public List<SkillTrigger> triggers { get; set; } = new List<SkillTrigger>();

        // Fired whenever the trigger list or a trigger's diagnostics change,
        // including from the background capture/watcher threads - same reasoning
        // as SkillMapManager.Changed, replacing what used to be blind polling.
        // Subscribers must marshal back to their own UI thread themselves.
        public static event System.Action TriggersChanged;

        private _4RThread thread;

        static StatusCapture()
        {
            LoadLog();
        }

        public static IReadOnlyDictionary<uint, CapturedStatus> Captured
        {
            get
            {
                lock (capturedLock)
                {
                    return new Dictionary<uint, CapturedStatus>(captured);
                }
            }
        }

        // Status ids active on the client as of the most recent scan tick (<=500ms old
        // while capture is running) - lets the UI filter the historical log down to
        // "what's actually lit up on my character right now", since a long play session
        // makes the full log hard to eyeball against the in-game buff bar.
        public static IReadOnlyCollection<uint> CurrentlyActive
        {
            get
            {
                lock (capturedLock)
                {
                    return new HashSet<uint>(currentlyActive);
                }
            }
        }

        public static IReadOnlyDictionary<string, TriggerDiagnostic> Diagnostics
        {
            get
            {
                lock (diagnosticsLock)
                {
                    return new Dictionary<string, TriggerDiagnostic>(diagnostics);
                }
            }
        }

        public void AddTrigger(int skillId, string skillName, Key key)
        {
            triggers.RemoveAll(t => t.Key == key.ToString());
            if (FormUtils.IsValidKey(key))
            {
                triggers.Add(new SkillTrigger { SkillId = skillId, SkillName = skillName, Key = key.ToString() });
            }
            TriggersChanged?.Invoke();
        }

        public void RemoveTrigger(SkillTrigger trigger)
        {
            triggers.Remove(trigger);
            TriggersChanged?.Invoke();
        }

        // Sends the trigger's key to the game itself (same PostMessage mechanism
        // AutoBuff/Autopot/AHK already use to send keys) instead of waiting to detect
        // the user physically pressing it. Sidesteps relying on any OS key-state API
        // working correctly from a background thread - 4RTools causes the press, so
        // it knows exactly when it happened, no detection needed at all.
        public void TestTrigger(SkillTrigger trigger)
        {
            Client client = ClientSingleton.GetClient();
            if (client == null || client.process == null) return;

            HashSet<uint> before = ReadActiveStatusIds(client);

            lock (diagnosticsLock)
            {
                diagnostics[trigger.Key] = new TriggerDiagnostic { LastFiredUtc = DateTime.UtcNow, LastResult = "sent by Test button, waiting on cast window..." };
            }
            TriggersChanged?.Invoke();

            try
            {
                System.Windows.Forms.Keys formsKey = (System.Windows.Forms.Keys)Enum.Parse(typeof(System.Windows.Forms.Keys), trigger.Key);
                Interop.PostMessage(client.process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, formsKey, 0);
            }
            catch
            {
                lock (diagnosticsLock)
                {
                    diagnostics[trigger.Key] = new TriggerDiagnostic { LastFiredUtc = DateTime.UtcNow, LastResult = "could not send this key - is it a valid key name?" };
                }
                TriggersChanged?.Invoke();
                return;
            }

            WatchForNewStatusAfterTrigger(client, trigger.Key, trigger.SkillId, trigger.SkillName, before);
        }

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                this.thread = CaptureThread(roClient);
                _4RThread.Start(this.thread);
            }
        }

        public _4RThread CaptureThread(Client c)
        {
            Dictionary<string, bool> wasKeyDown = new Dictionary<string, bool>();

            _4RThread captureThread = new _4RThread(_ =>
            {
                HashSet<uint> activeNow = ReadActiveStatusIds(c);
                bool discoveredNew = false;

                lock (capturedLock)
                {
                    currentlyActive = new HashSet<uint>(activeNow);

                    foreach (uint currentStatus in activeNow)
                    {
                        if (captured.TryGetValue(currentStatus, out CapturedStatus entry))
                        {
                            entry.LastSeenUtc = DateTime.UtcNow;
                            entry.TimesSeen++;
                        }
                        else
                        {
                            captured[currentStatus] = new CapturedStatus
                            {
                                StatusId = currentStatus,
                                FirstSeenUtc = DateTime.UtcNow,
                                LastSeenUtc = DateTime.UtcNow,
                                TimesSeen = 1
                            };
                            discoveredNew = true;
                        }
                    }
                }

                if (discoveredNew) SaveLog();

                // Edge-detect each configured trigger key and, on press, start a
                // delayed before/after diff to try to auto-map the skill it casts.
                foreach (SkillTrigger trigger in new List<SkillTrigger>(triggers))
                {
                    bool isDown;
                    try
                    {
                        // WPF's Keyboard.IsKeyDown wraps Win32 GetKeyState, which only
                        // reflects what the CALLING thread has seen via its own message
                        // queue - a plain background Thread (this one) never pumps
                        // messages, so it never saw the keypress at all. GetAsyncKeyState
                        // reads real hardware state system-wide regardless of that.
                        System.Windows.Forms.Keys formsKey = (System.Windows.Forms.Keys)Enum.Parse(typeof(System.Windows.Forms.Keys), trigger.Key);
                        isDown = Interop.IsKeyPhysicallyDown(formsKey);
                    }
                    catch
                    {
                        continue; // malformed/unparseable key string, skip it
                    }

                    bool previouslyDown = wasKeyDown.TryGetValue(trigger.Key, out bool prev) && prev;
                    wasKeyDown[trigger.Key] = isDown;

                    if (isDown && !previouslyDown)
                    {
                        lock (diagnosticsLock)
                        {
                            diagnostics[trigger.Key] = new TriggerDiagnostic { LastFiredUtc = DateTime.UtcNow, LastResult = "key seen, waiting on cast window..." };
                        }
                        TriggersChanged?.Invoke();
                        WatchForNewStatusAfterTrigger(c, trigger.Key, trigger.SkillId, trigger.SkillName, activeNow);
                    }
                }

                Thread.Sleep(500);
                return 0;
            });

            return captureThread;
        }

        private static void WatchForNewStatusAfterTrigger(Client c, string triggerKey, int skillId, string skillName, HashSet<uint> beforeSnapshot)
        {
            HashSet<uint> before = new HashSet<uint>(beforeSnapshot);

            Thread watcher = new Thread(() =>
            {
                Thread.Sleep(TriggerCaptureWindow);

                HashSet<uint> after = ReadActiveStatusIds(c);
                List<uint> newlyAppeared = new List<uint>();
                foreach (uint id in after)
                {
                    if (!before.Contains(id)) newlyAppeared.Add(id);
                }

                string result;
                if (newlyAppeared.Count == 1)
                {
                    SkillMapManager.SetSkill(newlyAppeared[0], skillId, skillName);
                    result = $"mapped status {newlyAppeared[0]}";
                }
                else if (newlyAppeared.Count == 0)
                {
                    // 0 candidates: either this skill doesn't leave a self-status at
                    // all (e.g. it's an attack, not a buff), or the cast/animation
                    // took longer than the capture window.
                    result = "no new status appeared - not a self-buff, or cast took longer than the window";
                }
                else
                {
                    result = $"{newlyAppeared.Count} new statuses appeared at once - too ambiguous to auto-map";
                }

                lock (diagnosticsLock)
                {
                    diagnostics[triggerKey] = new TriggerDiagnostic { LastFiredUtc = DateTime.UtcNow, LastResult = result };
                }
                TriggersChanged?.Invoke();
            })
            { IsBackground = true };

            watcher.Start();
        }

        private static HashSet<uint> ReadActiveStatusIds(Client c)
        {
            HashSet<uint> active = new HashSet<uint>();
            for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
            {
                uint currentStatus = c.CurrentBuffStatusCode(i);
                EffectStatusIDs status = (EffectStatusIDs)currentStatus;

                if (currentStatus == 0 || status == EffectStatusIDs.IGNORE || status == EffectStatusIDs.IGNORE_2)
                {
                    continue;
                }

                active.Add(currentStatus);
            }
            return active;
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
            return ACTION_NAME_STATUS_CAPTURE;
        }

        public static void ClearLog()
        {
            lock (capturedLock)
            {
                captured.Clear();
            }
            SaveLog();
        }

        private static void LoadLog()
        {
            lock (capturedLock)
            {
                captured = new Dictionary<uint, CapturedStatus>();
                if (!File.Exists(LogFileName)) return;

                try
                {
                    string json = File.ReadAllText(LogFileName);
                    List<CapturedStatus> list = JsonConvert.DeserializeObject<List<CapturedStatus>>(json) ?? new List<CapturedStatus>();
                    foreach (CapturedStatus entry in list)
                    {
                        captured[entry.StatusId] = entry;
                    }
                }
                catch
                {
                    // Corrupt file: start empty rather than crashing the app on load.
                }
            }
        }

        private static void SaveLog()
        {
            List<CapturedStatus> list;
            lock (capturedLock)
            {
                list = new List<CapturedStatus>(captured.Values);
            }

            string json = JsonConvert.SerializeObject(list, Formatting.Indented);
            File.WriteAllText(LogFileName, json);
        }
    }
}
