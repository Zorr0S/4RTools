using System;
using System.Threading;
using System.Windows.Input;
using System.Windows.Forms;
using System.Collections.Generic;
using Newtonsoft.Json;
using _4RTools.Utils;

namespace _4RTools.Model
{

    public class AutoBuff : Action
    {
        public static string ACTION_NAME_AUTOBUFF = "Autobuff";

        private _4RThread thread;
        public int delay { get; set; } = 1;
        public Dictionary<EffectStatusIDs, Key> buffMapping = new Dictionary<EffectStatusIDs, Key>();

        // Runtime-only: when a status has a CooldownSeconds configured in
        // SkillMapManager (see Forms/SkillBibleForm.cs), don't retry its key until
        // that many seconds have passed since the last attempt - otherwise a skill
        // whose real cooldown outlasts its buff duration gets spammed uselessly
        // for the gap between expiry and availability.
        private readonly Dictionary<EffectStatusIDs, DateTime> lastPressedUtc = new Dictionary<EffectStatusIDs, DateTime>();

        // Used only when DurationSeconds is also configured (see SkillMapEntry).
        // lastPressedUtc + CooldownSeconds assumes the buff ran its full natural
        // duration before expiring, which is wrong if it got cleared early (e.g.
        // death) - that leaves AutoBuff waiting out a stale timer. Instead, once the
        // known-unavailable gap (Cooldown - Duration) has passed, probe on a
        // shrinking schedule (halving each miss, floor 3s) so an early loss gets
        // caught quickly instead of waiting for the original window to run out.
        private const double MinRetryIntervalSeconds = 3;
        private class RetryState
        {
            public DateTime NextRetryUtc;
            public double NextIntervalSeconds;
        }
        private readonly Dictionary<EffectStatusIDs, RetryState> retryState = new Dictionary<EffectStatusIDs, RetryState>();

        // Debug-only, throttled to 1 line/status/second so the Output window stays
        // readable - temporary instrumentation for diagnosing why a specific status
        // isn't getting recast (see docs/specs/skill-capture.md for CooldownSeconds).
        private readonly Dictionary<EffectStatusIDs, DateTime> lastLoggedUtc = new Dictionary<EffectStatusIDs, DateTime>();
        private void LogDebug(EffectStatusIDs status, string message)
        {
            if (lastLoggedUtc.TryGetValue(status, out DateTime last) && (DateTime.UtcNow - last).TotalSeconds < 1) return;
            lastLoggedUtc[status] = DateTime.UtcNow;
            Console.WriteLine($"[AutoBuff Debug] {status}: {message}");
        }

        public void Start()
        {
            Stop();
            Client roClient = ClientSingleton.GetClient();
            if (roClient != null)
            {
                 this.thread = AutoBuffThread(roClient);
                _4RThread.Start(this.thread);
            }
        }

        public _4RThread AutoBuffThread(Client c)
        {
            _4RThread autobuffItemThread = new _4RThread(_ =>
            {

                bool foundQuag = false;
                Dictionary<EffectStatusIDs, Key> bmClone = new Dictionary<EffectStatusIDs, Key>(this.buffMapping);
                for (int i = 0; i < Constants.MAX_BUFF_LIST_INDEX_SIZE; i++)
                {
                    uint currentStatus = c.CurrentBuffStatusCode(i);
                    EffectStatusIDs status = (EffectStatusIDs)currentStatus;
                    
                    if (status == EffectStatusIDs.IGNORE) { continue; };
                    if (status == EffectStatusIDs.IGNORE_2) { continue; };

                    if (status == EffectStatusIDs.OVERTHRUSTMAX)
                    {
                        if (buffMapping.ContainsKey(EffectStatusIDs.OVERTHRUST))
                        {
                            bmClone.Remove(EffectStatusIDs.OVERTHRUST);
                        }
                    }

                    if (buffMapping.ContainsKey(status)) //CHECK IF STATUS EXISTS IN STATUS LIST AND DO ACTION
                    {
                        LogDebug(status, "currently active, not pressing");
                        bmClone.Remove(status);
                        retryState.Remove(status); // back up, restart backoff fresh next time it's missing
                    }

                    if (status == EffectStatusIDs.QUAGMIRE) foundQuag = true;
                }

                foreach (var item in bmClone)
                {
                    if (foundQuag && (item.Key == EffectStatusIDs.CONCENTRATION || item.Key == EffectStatusIDs.INC_AGI || item.Key == EffectStatusIDs.TRUESIGHT || item.Key == EffectStatusIDs.ADRENALINE || item.Key == EffectStatusIDs.SPEARQUICKEN))
                    {
                        LogDebug(item.Key, "missing but skipped, Quagmire is active");
                        continue; // Quagmire suppresses just these buffs, not the rest of the batch
                    }

                    SkillMapEntry mapEntry = SkillMapManager.Get((uint)item.Key);
                    bool hasUnavailableWindow = mapEntry != null && mapEntry.CooldownSeconds > 0
                        && mapEntry.DurationSeconds > 0 && mapEntry.DurationSeconds < mapEntry.CooldownSeconds;
                    bool everPressed = lastPressedUtc.TryGetValue(item.Key, out DateTime lastPressTime);

                    if (everPressed && hasUnavailableWindow)
                    {
                        double unavailableSeconds = mapEntry.CooldownSeconds - mapEntry.DurationSeconds;
                        if (!retryState.TryGetValue(item.Key, out RetryState retry))
                        {
                            retry = new RetryState { NextRetryUtc = lastPressTime.AddSeconds(unavailableSeconds), NextIntervalSeconds = unavailableSeconds };
                            retryState[item.Key] = retry;
                        }

                        if (DateTime.UtcNow < retry.NextRetryUtc)
                        {
                            LogDebug(item.Key, $"missing, next retry in {(retry.NextRetryUtc - DateTime.UtcNow).TotalSeconds:F0}s (backoff {retry.NextIntervalSeconds:F0}s)");
                            continue;
                        }
                    }
                    else if (everPressed && mapEntry != null && mapEntry.CooldownSeconds > 0
                        && (DateTime.UtcNow - lastPressTime).TotalSeconds < mapEntry.CooldownSeconds)
                    {
                        double remaining = mapEntry.CooldownSeconds - (DateTime.UtcNow - lastPressTime).TotalSeconds;
                        LogDebug(item.Key, $"missing but on configured cooldown, {remaining:F0}s left of {mapEntry.CooldownSeconds}s");
                        continue; // still on real cooldown, don't spam the key
                    }

                    if (c.ReadCurrentHp() >= Constants.MINIMUM_HP_TO_RECOVER)
                    {
                        LogDebug(item.Key, $"missing, pressing {item.Value}");
                        this.useAutobuff(item.Value);
                        lastPressedUtc[item.Key] = DateTime.UtcNow;

                        if (hasUnavailableWindow && retryState.TryGetValue(item.Key, out RetryState retryToAdvance))
                        {
                            retryToAdvance.NextIntervalSeconds = Math.Max(MinRetryIntervalSeconds, retryToAdvance.NextIntervalSeconds / 2);
                            retryToAdvance.NextRetryUtc = DateTime.UtcNow.AddSeconds(retryToAdvance.NextIntervalSeconds);
                        }

                        Thread.Sleep(10);
                    }
                    else
                    {
                        LogDebug(item.Key, "missing but HP below recovery threshold, skipping press");
                    }
                }

                Thread.Sleep(300);
                return 0;

            });

            return autobuffItemThread;
        }

        public void AddKeyToBuff(EffectStatusIDs status, Key key)
        {
            if (buffMapping.ContainsKey(status))
            {
                buffMapping.Remove(status);
            }

            if (FormUtils.IsValidKey(key))
            {
                buffMapping.Add(status, key);
            }
        }
        public void ClearKeyMapping()
        {
            buffMapping.Clear();
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
            return ACTION_NAME_AUTOBUFF;
        }

        private void useAutobuff(Key key)
        {
            // AutoBuffThread runs on a plain background thread with no message pump,
            // so System.Windows.Input.Keyboard.IsKeyDown (which reads the calling
            // thread's own message queue) can get stuck reporting Alt as held when it
            // isn't - same bug already found/fixed for Skill Capture and Mouse Remap.
            if ((key != Key.None) && !Interop.IsKeyPhysicallyDown(Keys.LMenu) && !Interop.IsKeyPhysicallyDown(Keys.RMenu))
                Interop.PostMessage(ClientSingleton.GetClient().process.MainWindowHandle, Constants.WM_KEYDOWN_MSG_ID, (Keys)Enum.Parse(typeof(Keys), key.ToString()), 0);
        }
    }
}
