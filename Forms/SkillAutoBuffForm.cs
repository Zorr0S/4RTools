using System;
using System.Drawing;
using System.Windows.Forms;
using _4RTools.Utils;
using _4RTools.Model;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace _4RTools.Forms
{
    public partial class SkillAutoBuffForm : Form, IObserver
    {

        private List<BuffContainer> skillContainers = new List<BuffContainer>();
        private CheckBox chkShowOnlyPRM;
        private List<GroupBox> prmGroupBoxes = new List<GroupBox>();
        private int contentLeft;

        public SkillAutoBuffForm(Subject subject)
        {
            this.KeyPreview = true;
            InitializeComponent();

            skillContainers.Add(new BuffContainer(this.ArcherSkillsGP, Buff.GetArcherSkills()));
            skillContainers.Add(new BuffContainer(this.SwordmanSkillGP, Buff.GetSwordmanSkill()));
            skillContainers.Add(new BuffContainer(this.MageSkillGP, Buff.GetMageSkills()));
            skillContainers.Add(new BuffContainer(this.MerchantSkillsGP, Buff.GetMerchantSkills()));
            skillContainers.Add(new BuffContainer(this.ThiefSkillsGP, Buff.GetThiefSkills()));
            skillContainers.Add(new BuffContainer(this.AcolyteSkillsGP, Buff.GetAcolyteSkills()));
            skillContainers.Add(new BuffContainer(this.TKSkillGroupBox, Buff.GetTaekwonSkills()));
            skillContainers.Add(new BuffContainer(this.NinjaSkillsGP, Buff.GetNinjaSkills()));
            skillContainers.Add(new BuffContainer(this.GunsSkillsGP, Buff.GetGunsSkills()));
            skillContainers.Add(new BuffContainer(this.summonerSkillsGP, Buff.GeSummonerSkills()));
            skillContainers.Add(new BuffContainer(this.SoulAsceticSkillsGP, Buff.GetSoulAsceticSkills()));
            skillContainers.Add(new BuffContainer(this.NightWatchSkillsGP, Buff.GetNightWatchSkills()));
            skillContainers.Add(new BuffContainer(this.HyperNoviceSkillsGP, Buff.GetHyperNoviceSkills()));
            skillContainers.Add(new BuffContainer(this.HomunculusSkillsGP, Buff.GetHomunculusBuffs()));

            new BuffRenderer("Autobuff", skillContainers, toolTip1).doRender();

            contentLeft = this.ArcherSkillsGP.Location.X;

            SetupPrmToggle();
            RenderPrmGroups();
            RepositionGroups();

            subject.Attach(this);
        }

        // Defaults to ON: this server's own classes (scraped RTM database) are what
        // actually matters here, not the hardcoded stock job sections above - those
        // are from the original 4RTools buff list and mostly irrelevant noise for a
        // server where everything is reskinned under PRM's own class system.
        private void SetupPrmToggle()
        {
            chkShowOnlyPRM = new CheckBox
            {
                Text = "Show only PRM classes",
                Location = new Point(contentLeft, 2),
                AutoSize = true,
                Checked = true
            };
            chkShowOnlyPRM.CheckedChanged += (s, e) => RepositionGroups();

            this.Controls.Add(chkShowOnlyPRM);
        }

        private static bool IsSelfBuffCandidate(RTMSkillRef skill)
        {
            return string.Equals(skill.Type, "Supportive", StringComparison.OrdinalIgnoreCase)
                && string.Equals(skill.AimedAt, "Yourself", StringComparison.OrdinalIgnoreCase);
        }

        private static SkillMapEntry FindMapEntryFor(RTMSkillRef skill)
        {
            int skillId = skill.SkillId ?? 0;
            return SkillMapManager.Entries.Values.FirstOrDefault(e =>
                (skillId != 0 && e.SkillId == skillId) ||
                (skillId == 0 && string.Equals(e.SkillName, skill.Name, StringComparison.OrdinalIgnoreCase)));
        }

        // Every self-buff skill in the scraped database is listed here, grouped by
        // class, whether it already has a Buff ID linked or not - this is meant to
        // be browsable (like the hardcoded job sections above always show every
        // skill), not just a readout of what Skill Capture/Skill Bible happened to
        // confirm so far. A skill shared by several classes (e.g. "Ready to
        // Rip"/LK_CONCENTRATION across Deadeye, Duelist and Phantom Thief) is
        // grouped under every one of them.
        private Dictionary<string, List<RTMSkillRef>> GroupSelfBuffsByClass()
        {
            var result = new Dictionary<string, List<RTMSkillRef>>();

            foreach (RTMSkillRef skill in RTMSkillReference.All.Where(IsSelfBuffCandidate))
            {
                string cls = string.IsNullOrEmpty(skill.Class) ? "Other" : skill.Class;
                if (!result.TryGetValue(cls, out List<RTMSkillRef> list))
                {
                    list = new List<RTMSkillRef>();
                    result[cls] = list;
                }
                list.Add(skill);
            }

            return result;
        }

        // Rebuilds every PRM class GroupBox from scratch - SkillMap.json can grow or
        // shrink at any time (Skill Capture/Skill Bible run independently of this
        // tab), so which rows are bindable (vs. still "not linked yet") isn't fixed.
        private void RenderPrmGroups()
        {
            foreach (GroupBox gp in prmGroupBoxes)
            {
                this.Controls.Remove(gp);
                gp.Dispose();
            }
            prmGroupBoxes.Clear();

            Dictionary<string, List<RTMSkillRef>> grouped = GroupSelfBuffsByClass();
            Dictionary<EffectStatusIDs, Key> buffMapping = ProfileSingleton.GetCurrent().Autobuff.buffMapping;

            foreach (var kvp in grouped.OrderBy(k => RTMClassProgression.RankOf(k.Key)))
            {
                GroupBox gp = new GroupBox { AutoSize = false, Size = new Size(520, 30), Text = kvp.Key, TabStop = false };
                int y = 20;

                foreach (RTMSkillRef skill in kvp.Value.OrderBy(s => s.Name))
                {
                    SkillMapEntry mapEntry = FindMapEntryFor(skill);
                    bool isLinked = mapEntry != null;

                    string label = skill.SkillId.HasValue && skill.SkillId != 0
                        ? $"[{skill.SkillId}] {skill.Name}"
                        : skill.Name;
                    label += isLinked ? $" (status {mapEntry.StatusId})" : " (not linked yet)";

                    Bitmap icon = RTMSkillReference.GetIconForSkill(skill);
                    if (icon != null)
                    {
                        PictureBox pb = new PictureBox
                        {
                            Image = icon,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Location = new Point(10, y),
                            Size = new Size(20, 20)
                        };
                        gp.Controls.Add(pb);
                    }

                    Label lbl = new Label
                    {
                        Text = label,
                        Location = new Point(icon != null ? 35 : 10, y + 3),
                        AutoSize = true,
                        ForeColor = isLinked ? SystemColors.ControlText : Color.Gray
                    };

                    System.Windows.Forms.TextBox textBox = new System.Windows.Forms.TextBox
                    {
                        Size = new Size(70, 20),
                        Location = new Point(400, y),
                        Enabled = isLinked,
                        ReadOnly = !isLinked
                    };

                    if (isLinked)
                    {
                        EffectStatusIDs statusID = (EffectStatusIDs)mapEntry.StatusId;
                        textBox.Tag = mapEntry.StatusId;
                        textBox.Name = "prmIn" + mapEntry.StatusId;
                        textBox.Text = buffMapping.TryGetValue(statusID, out Key boundKey) ? boundKey.ToString() : Key.None.ToString();
                        textBox.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
                        textBox.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
                        textBox.TextChanged += CustomSkillKey_TextChanged;
                    }
                    else
                    {
                        textBox.Text = "-";
                    }

                    gp.Controls.Add(lbl);
                    gp.Controls.Add(textBox);

                    y += 24;
                }

                gp.Size = new Size(520, y + 15);
                this.Controls.Add(gp);
                prmGroupBoxes.Add(gp);
            }

            RepositionGroups();
        }

        // Stacks whichever hardcoded job sections are currently visible (none, if the
        // PRM-only toggle is on) followed by every PRM class group, top to bottom.
        private void RepositionGroups()
        {
            bool onlyPrm = chkShowOnlyPRM.Checked;
            int y = chkShowOnlyPRM.Location.Y + chkShowOnlyPRM.Height + 10;

            foreach (BuffContainer c in skillContainers)
            {
                c.container.Visible = !onlyPrm;
                if (!onlyPrm)
                {
                    c.container.Location = new Point(contentLeft, y);
                    y += c.container.Height + 10;
                }
            }

            foreach (GroupBox gp in prmGroupBoxes)
            {
                gp.Location = new Point(contentLeft, y);
                y += gp.Height + 10;
            }
        }

        private void CustomSkillKey_TextChanged(object sender, EventArgs e)
        {
            try
            {
                System.Windows.Forms.TextBox txtBox = (System.Windows.Forms.TextBox)sender;
                if (txtBox.Text != string.Empty)
                {
                    Key key = (Key)Enum.Parse(typeof(Key), txtBox.Text);
                    EffectStatusIDs statusID = (EffectStatusIDs)(uint)txtBox.Tag;

                    ProfileSingleton.GetCurrent().Autobuff.AddKeyToBuff(statusID, key);
                    ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().Autobuff);
                }
            }
            catch { }
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    BuffRenderer.doUpdate(new Dictionary<EffectStatusIDs, Key>(ProfileSingleton.GetCurrent().Autobuff.buffMapping), this);
                    RenderPrmGroups();
                    break;
                case MessageCode.SKILL_MAP_CHANGED:
                    RenderPrmGroups();
                    break;
            }
        }
    }
}
