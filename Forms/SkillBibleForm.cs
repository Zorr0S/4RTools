using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    // The full scraped RTM skill database (Model/RTMSkillReference.cs) as one
    // browsable, filterable table instead of a search-as-you-go popup - the
    // "skill bible". Each row carries everything needed to go from "I found the
    // skill" to "it's wired into Autobuff" without leaving this tab: its icon
    // (bundled, or assign your own), a temporary test key (backed by the same
    // StatusCapture.triggers used in Skill Capture), an optional per-skill
    // cooldown (Model/Autobuff.cs reads this to stop spamming a key while a
    // skill's really on cooldown), and the Buff/Status ID once linked.
    public class SkillBibleForm : Form, IObserver
    {
        private ComboBox cboClass;
        private TextBox txtSearch;
        private DataGridView grid;

        private TextBox txtKeyCapture;

        public SkillBibleForm(Subject subject)
        {
            BuildUi();
            subject.Attach(this);

            // This data (Key/Cooldown/Buff ID) only ever changes on an explicit write -
            // including from a background thread when a Test's result lands a few
            // seconds later. Container bridges that Model-level event into a proper
            // Subject.Notify(MessageCode.SKILL_MAP_CHANGED / SKILL_TRIGGERS_CHANGED),
            // handled below in Update() the same way every other cross-form refresh in
            // this app works - no per-Form event subscription needed.
            RenderGrid();
        }

        private void BuildUi()
        {
            this.Text = "Skill Bible";

            Panel filterPanel = new Panel { Dock = DockStyle.Top, Height = 34 };

            Label lblClass = new Label { Text = "Class:", Location = new Point(5, 8), AutoSize = true };
            cboClass = new ComboBox { Location = new Point(50, 5), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            cboClass.Items.Add("All Classes");
            foreach (string cls in RTMSkillReference.All.Select(s => s.Class).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => RTMClassProgression.RankOf(c)))
            {
                cboClass.Items.Add(cls);
            }
            cboClass.SelectedIndex = 0;
            cboClass.SelectedIndexChanged += (s, e) => RenderGrid();

            Label lblSearch = new Label { Text = "Search:", Location = new Point(220, 8), AutoSize = true };
            txtSearch = new TextBox { Location = new Point(270, 5), Width = 180 };
            txtSearch.TextChanged += (s, e) => RenderGrid();

            Label hint = new Label
            {
                Text = $"Showing only self-buffs (Type = Supportive, Aimed At = Yourself) - the only kind Autobuff can actually use, since it only ever re-presses a key on yourself.",
                Location = new Point(460, 8),
                AutoSize = true
            };

            filterPanel.Controls.Add(lblClass);
            filterPanel.Controls.Add(cboClass);
            filterPanel.Controls.Add(lblSearch);
            filterPanel.Controls.Add(txtSearch);
            filterPanel.Controls.Add(hint);

            Panel actionPanel = new Panel { Dock = DockStyle.Top, Height = 34 };

            Label lblKey = new Label { Text = "Key for selected:", Location = new Point(5, 8), AutoSize = true };
            txtKeyCapture = new TextBox { Location = new Point(110, 5), Width = 90, ReadOnly = true, Text = Key.None.ToString() };
            txtKeyCapture.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtKeyCapture.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);

            Button btnSetKey = new Button { Text = "Set Key", Location = new Point(210, 4), Width = 80 };
            btnSetKey.Click += BtnSetKey_Click;

            Button btnClearKey = new Button { Text = "Clear Key", Location = new Point(295, 4), Width = 80 };
            btnClearKey.Click += BtnClearKey_Click;

            Button btnAssignIcon = new Button { Text = "Assign Icon...", Location = new Point(390, 4), Width = 100 };
            btnAssignIcon.Click += BtnAssignIcon_Click;

            Button btnClearIcon = new Button { Text = "Clear Custom Icon", Location = new Point(495, 4), Width = 130 };
            btnClearIcon.Click += BtnClearIcon_Click;

            actionPanel.Controls.Add(lblKey);
            actionPanel.Controls.Add(txtKeyCapture);
            actionPanel.Controls.Add(btnSetKey);
            actionPanel.Controls.Add(btnClearKey);
            actionPanel.Controls.Add(btnAssignIcon);
            actionPanel.Controls.Add(btnClearIcon);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowTemplate = { Height = 26 },
                EditMode = DataGridViewEditMode.EditOnEnter // one click on Cooldown starts editing, no double-click needed
            };

            // DataGridView repaints every cell (icons included) on every row rebuild;
            // without this the whole grid visibly flickers each refresh. DoubleBuffered
            // is protected on Control, so it has to be set via reflection from outside.
            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, grid, new object[] { true });

            DataGridViewImageColumn iconCol = new DataGridViewImageColumn { Name = "Icon", HeaderText = "Icon", Width = 40, ImageLayout = DataGridViewImageCellLayout.Zoom };
            iconCol.DefaultCellStyle.NullValue = null;
            grid.Columns.Add(iconCol);

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Class", HeaderText = "Class", ReadOnly = true, Width = 120 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillName", HeaderText = "Skill Name", ReadOnly = true, Width = 200 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", ReadOnly = true, Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AimedAt", HeaderText = "Aimed At", ReadOnly = true, Width = 100 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", ReadOnly = true, Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cooldown", HeaderText = "Cooldown (s)", ReadOnly = false, Width = 90 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "BuffId", HeaderText = "Buff ID", ReadOnly = true, Width = 70 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Test", HeaderText = "", Text = "Test", UseColumnTextForButtonValue = true, Width = 60 });

            grid.CellEndEdit += Grid_CellEndEdit;
            grid.CellContentClick += Grid_CellContentClick;
            grid.CellClick += Grid_CellClick;

            this.Controls.Add(grid);
            this.Controls.Add(actionPanel);
            this.Controls.Add(filterPanel);
        }

        private void Grid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (!(grid.Rows[e.RowIndex].Tag is RTMSkillRef skill)) return;

            SkillTrigger trigger = FindTriggerFor(skill);
            txtKeyCapture.Text = trigger != null ? trigger.Key : Key.None.ToString();
        }

        private RTMSkillRef SelectedSkill()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as RTMSkillRef;
        }

        private SkillTrigger FindTriggerFor(RTMSkillRef skill)
        {
            int skillId = skill.SkillId ?? 0;
            return ProfileSingleton.GetCurrent().StatusCapture.triggers.FirstOrDefault(t =>
                (skillId != 0 && t.SkillId == skillId) ||
                (skillId == 0 && string.Equals(t.SkillName, skill.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsSelfBuffCandidate(RTMSkillRef skill)
        {
            return string.Equals(skill.Type, "Supportive", StringComparison.OrdinalIgnoreCase)
                && string.Equals(skill.AimedAt, "Yourself", StringComparison.OrdinalIgnoreCase);
        }

        private SkillMapEntry FindMapEntryFor(RTMSkillRef skill)
        {
            int skillId = skill.SkillId ?? 0;
            return SkillMapManager.Entries.Values.FirstOrDefault(e =>
                (skillId != 0 && e.SkillId == skillId) ||
                (skillId == 0 && string.Equals(e.SkillName, skill.Name, StringComparison.OrdinalIgnoreCase)));
        }

        private void RenderGrid()
        {
            if (grid == null) return;

            RTMSkillRef previouslySelected = SelectedSkill();
            int scrollIndex = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;

            string classFilter = cboClass.SelectedItem as string;
            string search = txtSearch.Text.Trim();

            // Search always narrows the list, including an already-linked skill - typing
            // a name is an explicit "find this one" request, so a linked skill with an
            // unrelated name has no business cluttering those results.
            //
            // Baseline (Class + Type/AimedAt): only self-cast buffs are relevant here at
            // all, since AutoBuff only ever re-presses a key on yourself - it has no
            // notion of selecting a target, so a "Supportive" skill aimed at an ally (a
            // vampire-mask-style buff) isn't something this tab can wire up anyway.
            // Exception: a skill that already has a Buff ID linked bypasses just this
            // part - it's already confirmed usable (by test or by the vanilla-reuse
            // seeding), so hiding it over a Type/AimedAt/Class classification that might
            // just be wrong or imprecise in the scraped data would only hide a working
            // mapping.
            var filtered = RTMSkillReference.All
                .Where(s => string.IsNullOrEmpty(search) || (s.Name != null && s.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(s => FindMapEntryFor(s) != null || (
                    IsSelfBuffCandidate(s)
                    && (classFilter == "All Classes" || string.Equals(s.Class, classFilter, StringComparison.OrdinalIgnoreCase))
                ))
                .OrderBy(s => RTMClassProgression.RankOf(s.Class))
                .ThenBy(s => s.Name)
                .ToList();

            grid.Rows.Clear();

            foreach (RTMSkillRef skill in filtered)
            {
                Bitmap icon = RTMSkillReference.GetIconForSkill(skill);
                SkillTrigger trigger = FindTriggerFor(skill);
                SkillMapEntry mapEntry = FindMapEntryFor(skill);

                int rowIndex = grid.Rows.Add(
                    icon,
                    skill.Class,
                    skill.Name,
                    skill.Type,
                    skill.AimedAt,
                    trigger?.Key ?? "",
                    mapEntry != null && mapEntry.CooldownSeconds > 0 ? mapEntry.CooldownSeconds.ToString() : "",
                    mapEntry != null ? mapEntry.StatusId.ToString() : "",
                    null
                );
                grid.Rows[rowIndex].Tag = skill;
            }

            if (previouslySelected != null)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.Tag is RTMSkillRef s && s.Key == previouslySelected.Key)
                    {
                        row.Selected = true;
                        grid.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }

            if (scrollIndex >= 0 && scrollIndex < grid.Rows.Count)
            {
                grid.FirstDisplayedScrollingRowIndex = scrollIndex;
            }
        }

        private void Grid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (grid.Columns[e.ColumnIndex].Name != "Cooldown") return;
            if (!(grid.Rows[e.RowIndex].Tag is RTMSkillRef skill)) return;

            SkillMapEntry mapEntry = FindMapEntryFor(skill);
            string text = Convert.ToString(grid.Rows[e.RowIndex].Cells["Cooldown"].Value ?? "").Trim();

            if (mapEntry == null)
            {
                // Don't call RenderGrid() (Rows.Clear() + rebuild) from inside
                // CellEndEdit - the grid is still mid-commit on this very cell, and
                // tearing every row down under it crashes. Just revert this one cell.
                grid.Rows[e.RowIndex].Cells["Cooldown"].Value = "";
                MessageBox.Show("This skill has no Buff ID linked yet - set a key and Test it first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(text, out int seconds))
            {
                seconds = 0; // blank/invalid clears it rather than erroring - cooldown is optional
            }

            SkillMapManager.SetCooldown(mapEntry.StatusId, seconds);
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (grid.Columns[e.ColumnIndex].Name != "Test") return;
            if (!(grid.Rows[e.RowIndex].Tag is RTMSkillRef skill)) return;

            SkillTrigger trigger = FindTriggerFor(skill);
            if (trigger == null)
            {
                MessageBox.Show("Set a key for this skill first (select the row, press a key, click Set Key).", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (ClientSingleton.GetClient() == null)
            {
                MessageBox.Show("Select and attach a Ragnarok client first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProfileSingleton.GetCurrent().StatusCapture.TestTrigger(trigger);
        }

        private void BtnSetKey_Click(object sender, EventArgs e)
        {
            RTMSkillRef skill = SelectedSkill();
            if (skill == null)
            {
                MessageBox.Show("Select a skill row first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Key key;
            try
            {
                key = (Key)Enum.Parse(typeof(Key), txtKeyCapture.Text);
            }
            catch
            {
                MessageBox.Show("Press a valid key first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!FormUtils.IsValidKey(key))
            {
                MessageBox.Show("Press a valid key first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProfileSingleton.GetCurrent().StatusCapture.AddTrigger(skill.SkillId ?? 0, skill.Name, key);
            ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().StatusCapture);
            RenderGrid();
        }

        private void BtnClearKey_Click(object sender, EventArgs e)
        {
            RTMSkillRef skill = SelectedSkill();
            if (skill == null) return;

            SkillTrigger trigger = FindTriggerFor(skill);
            if (trigger == null) return;

            ProfileSingleton.GetCurrent().StatusCapture.RemoveTrigger(trigger);
            ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().StatusCapture);
            txtKeyCapture.Text = Key.None.ToString();
            RenderGrid();
        }

        private void BtnAssignIcon_Click(object sender, EventArgs e)
        {
            RTMSkillRef skill = SelectedSkill();
            if (skill == null)
            {
                MessageBox.Show("Select a skill row first.", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    CustomSkillIcons.SetIcon(skill.Key, dialog.FileName);
                    RenderGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not use this image: {ex.Message}", "Skill Bible", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnClearIcon_Click(object sender, EventArgs e)
        {
            RTMSkillRef skill = SelectedSkill();
            if (skill == null) return;

            CustomSkillIcons.RemoveIcon(skill.Key);
            RenderGrid();
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                case MessageCode.SKILL_MAP_CHANGED:
                case MessageCode.SKILL_TRIGGERS_CHANGED:
                    RenderGrid();
                    break;
            }
        }
    }
}
