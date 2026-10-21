using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using _4RTools.Model;
using _4RTools.Utils;

namespace _4RTools.Forms
{
    // Two things live here, deliberately kept visually separate:
    // - the Skill Map grid (top): the confirmed StatusId -> Skill links, i.e. the
    //   actual data SkillMapManager persists and Autobuff's Custom Skills section
    //   reads from. This is the only place a link is created or edited.
    // - the Captured/Active grid (bottom): a read-only log of every status id
    //   StatusCapture has ever seen, for *discovery* - double-click a row to carry
    //   its Status ID up into the Skill Map entry form instead of hand-typing it.
    public class StatusCaptureForm : Form, IObserver
    {
        private DataGridView mapGrid;
        private TextBox txtMapStatusId;
        private TextBox txtMapSkillId;
        private TextBox txtMapSkillName;
        private TextBox txtMapNote;
        private TabPage tabMap;

        private DataGridView capturedGrid;
        private CheckBox chkActiveOnly;
        private Label lblCapturedSummary;

        private DataGridView triggersGrid;
        private TextBox txtTriggerSkillId;
        private TextBox txtTriggerSkillName;
        private TextBox txtTriggerKey;
        private TabPage tabTriggers;

        private ToolTip toolTip;
        private Timer refreshTimer;

        public StatusCaptureForm(Subject subject)
        {
            BuildUi();

            subject.Attach(this);

            // The Captured grid reflects genuinely continuous live state (the status
            // buffer, scanned every 500ms by StatusCapture's own background thread),
            // so a timer is the right tool there. The Map and Triggers grids only
            // change on an explicit write - including from a background thread when a
            // Test/auto-map result lands a few seconds later - so Container bridges
            // that into a proper Subject.Notify(MessageCode.SKILL_MAP_CHANGED /
            // SKILL_TRIGGERS_CHANGED), handled below in Update() like everything else
            // in this app, instead of each Form subscribing to a raw Model event.
            refreshTimer = new Timer { Interval = 1000 };
            refreshTimer.Tick += (s, e) => RenderCapturedGrid();
            refreshTimer.Start();

            RenderCapturedGrid();
            RenderMapGrid();
            RenderTriggers();
        }

        private void BuildUi()
        {
            this.Text = "Skill Capture";
            toolTip = new ToolTip();

            // Map / Triggers / Captured each get their own sub-tab instead of being
            // stacked on one screen - with 70+ map entries, three sections fighting
            // over a fixed share of the window got cramped fast. Map opens by
            // default since it's where a link is actually created/edited; see
            // docs/specs/skill-capture.md.
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildMapTab());
            tabs.TabPages.Add(BuildTriggersTab());
            tabs.TabPages.Add(BuildCapturedTab());

            this.Controls.Add(tabs);
        }

        // Pressing Enter in a plain text field submits the section's primary
        // action instead of requiring a mouse click - skipped on key-capture
        // boxes (txtTriggerKey etc.), which already treat every keypress as the
        // key being bound.
        private static void SubmitOnEnter(System.Windows.Forms.KeyEventArgs e, EventHandler action)
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Enter)
            {
                e.SuppressKeyPress = true;
                action(null, EventArgs.Empty);
            }
        }

        // ---- Skill Triggers (auto-map on keypress) ----------------------------
        private TabPage BuildTriggersTab()
        {
            tabTriggers = new TabPage("Skill Triggers");

            Panel entryPanel = new Panel { Dock = DockStyle.Top, Height = 42 };

            Label lblTriggerSkillId = new Label { Text = "Skill ID:", Location = new System.Drawing.Point(10, 14), AutoSize = true };
            txtTriggerSkillId = new TextBox { Location = new System.Drawing.Point(65, 11), Width = 60 };
            txtTriggerSkillId.KeyDown += (s, e) => SubmitOnEnter(e, BtnAddTrigger_Click);

            Label lblTriggerSkillName = new Label { Text = "Skill Name:", Location = new System.Drawing.Point(135, 14), AutoSize = true };
            txtTriggerSkillName = new TextBox { Location = new System.Drawing.Point(210, 11), Width = 150 };
            txtTriggerSkillName.KeyDown += (s, e) => SubmitOnEnter(e, BtnAddTrigger_Click);

            Button btnBrowseTriggerSkill = new Button { Text = "Browse...", Location = new System.Drawing.Point(365, 9), Width = 70 };
            btnBrowseTriggerSkill.Click += (s, e) => BrowseInto(txtTriggerSkillId, txtTriggerSkillName);

            Label lblKey = new Label { Text = "Key:", Location = new System.Drawing.Point(445, 14), AutoSize = true };
            txtTriggerKey = new TextBox { Location = new System.Drawing.Point(475, 11), Width = 100, ReadOnly = true, Text = Key.None.ToString() };
            txtTriggerKey.KeyDown += new System.Windows.Forms.KeyEventHandler(FormUtils.OnKeyDown);
            txtTriggerKey.KeyPress += new KeyPressEventHandler(FormUtils.OnKeyPress);
            toolTip.SetToolTip(txtTriggerKey, "Click here, then press the key you use in-game to cast this skill.");

            Button btnAddTrigger = new Button { Text = "Add Trigger", Location = new System.Drawing.Point(585, 9), Width = 100 };
            btnAddTrigger.Click += BtnAddTrigger_Click;

            Button btnRemoveTrigger = new Button { Text = "Remove Selected", Location = new System.Drawing.Point(695, 9), Width = 120 };
            btnRemoveTrigger.Click += BtnRemoveTrigger_Click;

            Button btnTestTrigger = new Button { Text = "Test Selected", Location = new System.Drawing.Point(820, 9), Width = 110 };
            btnTestTrigger.Click += BtnTestTrigger_Click;
            toolTip.SetToolTip(btnTestTrigger, "Sends the key to the attached client and watches for a new status - no need to press it yourself.");

            entryPanel.Controls.AddRange(new Control[]
            {
                lblTriggerSkillId, txtTriggerSkillId, lblTriggerSkillName, txtTriggerSkillName,
                btnBrowseTriggerSkill, lblKey, txtTriggerKey, btnAddTrigger, btnRemoveTrigger, btnTestTrigger
            });

            triggersGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillId", HeaderText = "Skill ID", Width = 70 });
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillName", HeaderText = "Skill Name", Width = 200 });
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", Width = 100 });
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusId", HeaderText = "Mapped Status", ReadOnly = true, Width = 110 });
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastFired", HeaderText = "Last Fired", ReadOnly = true, Width = 90 });
            triggersGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastResult", HeaderText = "Result", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            triggersGrid.CellClick += TriggersGrid_CellClick;
            toolTip.SetToolTip(triggersGrid, "Click a row to edit it above. 'Test Selected' sends the key itself - the most reliable way to trigger a mapping.");

            // Dock=Top controls stack in reverse add order (last added ends up
            // highest), so the Fill grid is added first, then the Top entry row.
            tabTriggers.Controls.Add(triggersGrid);
            tabTriggers.Controls.Add(entryPanel);
            return tabTriggers;
        }

        // ---- Skill Map (the actual StatusId -> Skill link) --------------------
        private TabPage BuildMapTab()
        {
            tabMap = new TabPage("Skill Map");

            Panel toolbarPanel = new Panel { Dock = DockStyle.Top, Height = 30 };

            Button btnImport = new Button { Text = "Import...", Location = new System.Drawing.Point(10, 3), Width = 90 };
            btnImport.Click += BtnImport_Click;
            toolTip.SetToolTip(btnImport, "Load a skill map shared by another player of this server.");

            Button btnExport = new Button { Text = "Export...", Location = new System.Drawing.Point(105, 3), Width = 90 };
            btnExport.Click += BtnExport_Click;
            toolTip.SetToolTip(btnExport, "Save your skill map to a file, to share with other players.");

            toolbarPanel.Controls.AddRange(new Control[] { btnImport, btnExport });

            Panel entryPanel = new Panel { Dock = DockStyle.Top, Height = 42 };

            Label lblMapStatusId = new Label { Text = "Status ID:", Location = new System.Drawing.Point(10, 14), AutoSize = true };
            txtMapStatusId = new TextBox { Location = new System.Drawing.Point(70, 11), Width = 60 };
            toolTip.SetToolTip(txtMapStatusId, "Double-click a row in Captured / Active Statuses below to fill this in automatically.");

            Label lblMapSkillId = new Label { Text = "Skill ID:", Location = new System.Drawing.Point(140, 14), AutoSize = true };
            txtMapSkillId = new TextBox { Location = new System.Drawing.Point(195, 11), Width = 60 };

            Label lblMapSkillName = new Label { Text = "Skill Name:", Location = new System.Drawing.Point(265, 14), AutoSize = true };
            txtMapSkillName = new TextBox { Location = new System.Drawing.Point(335, 11), Width = 150 };
            txtMapSkillName.KeyDown += (s, e) => SubmitOnEnter(e, BtnSaveMapping_Click);

            Button btnBrowseMapSkill = new Button { Text = "Browse...", Location = new System.Drawing.Point(490, 9), Width = 70 };
            btnBrowseMapSkill.Click += (s, e) => BrowseInto(txtMapSkillId, txtMapSkillName);

            Label lblMapNote = new Label { Text = "Note:", Location = new System.Drawing.Point(570, 14), AutoSize = true };
            txtMapNote = new TextBox { Location = new System.Drawing.Point(605, 11), Width = 150 };
            txtMapNote.KeyDown += (s, e) => SubmitOnEnter(e, BtnSaveMapping_Click);

            Button btnSaveMapping = new Button { Text = "Save Mapping", Location = new System.Drawing.Point(760, 9), Width = 100 };
            btnSaveMapping.Click += BtnSaveMapping_Click;

            Button btnRemoveMapping = new Button { Text = "Remove Selected", Location = new System.Drawing.Point(865, 9), Width = 120 };
            btnRemoveMapping.Click += BtnRemoveMapping_Click;

            entryPanel.Controls.AddRange(new Control[]
            {
                lblMapStatusId, txtMapStatusId, lblMapSkillId, txtMapSkillId, lblMapSkillName, txtMapSkillName,
                btnBrowseMapSkill, lblMapNote, txtMapNote, btnSaveMapping, btnRemoveMapping
            });

            mapGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusId", HeaderText = "Status ID", Width = 80 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillId", HeaderText = "Skill ID", Width = 70 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SkillName", HeaderText = "Skill Name", Width = 200 });
            mapGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Note", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            mapGrid.CellClick += MapGrid_CellClick;
            toolTip.SetToolTip(mapGrid, "Click a row to edit it above.");

            // Add order (bottom-to-top visually): Fill grid, then entry row, then toolbar last.
            tabMap.Controls.Add(mapGrid);
            tabMap.Controls.Add(entryPanel);
            tabMap.Controls.Add(toolbarPanel);
            return tabMap;
        }

        // ---- Captured / Active log (read-only reference) -----------------------
        private TabPage BuildCapturedTab()
        {
            TabPage tabCaptured = new TabPage("Captured / Active");

            Panel toolbarPanel = new Panel { Dock = DockStyle.Top, Height = 30 };

            chkActiveOnly = new CheckBox { Text = "Show only currently active", Location = new System.Drawing.Point(10, 7), AutoSize = true };
            chkActiveOnly.CheckedChanged += (s, e) => RenderCapturedGrid();

            Button btnClear = new Button { Text = "Clear Captured Log", Location = new System.Drawing.Point(210, 4), Width = 140 };
            btnClear.Click += BtnClear_Click;
            toolTip.SetToolTip(btnClear, "Clears this discovery log only - never removes a Skill Map link.");

            lblCapturedSummary = new Label { Location = new System.Drawing.Point(360, 8), AutoSize = true, ForeColor = System.Drawing.Color.DimGray };

            toolbarPanel.Controls.AddRange(new Control[] { chkActiveOnly, btnClear, lblCapturedSummary });

            capturedGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            capturedGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ActiveNow", HeaderText = "Active Now", Width = 75 });
            capturedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "StatusId", HeaderText = "Status ID", Width = 80 });
            capturedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "FirstSeen", HeaderText = "First Seen", Width = 140 });
            capturedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LastSeen", HeaderText = "Last Seen", Width = 140 });
            capturedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "TimesSeen", HeaderText = "Times Seen", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            capturedGrid.CellDoubleClick += CapturedGrid_CellDoubleClick;
            toolTip.SetToolTip(capturedGrid, "Double-click a row to copy its Status ID into the Skill Map section above.");

            tabCaptured.Controls.Add(capturedGrid);
            tabCaptured.Controls.Add(toolbarPanel);
            return tabCaptured;
        }

        private void BrowseInto(TextBox skillIdBox, TextBox skillNameBox)
        {
            using (SkillPickerForm picker = new SkillPickerForm())
            {
                if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedSkill != null)
                {
                    skillIdBox.Text = picker.SelectedSkill.SkillId.HasValue ? picker.SelectedSkill.SkillId.ToString() : "";
                    skillNameBox.Text = picker.SelectedSkill.Name;
                }
            }
        }

        private void RenderTriggers()
        {
            PreserveGridState(triggersGrid, () =>
            {
                IReadOnlyDictionary<string, TriggerDiagnostic> diagnostics = StatusCapture.Diagnostics;
                List<SkillTrigger> triggers = ProfileSingleton.GetCurrent().StatusCapture.triggers;

                triggersGrid.Rows.Clear();
                foreach (SkillTrigger trigger in triggers)
                {
                    diagnostics.TryGetValue(trigger.Key, out TriggerDiagnostic diag);

                    int rowIndex = triggersGrid.Rows.Add(
                        trigger.SkillId != 0 ? trigger.SkillId.ToString() : "",
                        trigger.SkillName,
                        trigger.Key,
                        FindMappedStatusId(trigger),
                        diag != null ? diag.LastFiredUtc.ToLocalTime().ToString("T") : "never",
                        diag?.LastResult ?? "key press not detected yet"
                    );
                    triggersGrid.Rows[rowIndex].Tag = trigger;
                }

                if (tabTriggers != null)
                {
                    tabTriggers.Text = $"Skill Triggers ({triggers.Count})";
                }
            });
        }

        // A trigger only stores {SkillId, SkillName, Key} - the resulting Status ID,
        // if any, lives in SkillMapManager instead, so this just answers "did this
        // trigger's auto-map ever actually land, and on what id" by reverse lookup.
        private static string FindMappedStatusId(SkillTrigger trigger)
        {
            SkillMapEntry match = SkillMapManager.Entries.Values.FirstOrDefault(e =>
                (trigger.SkillId != 0 && e.SkillId == trigger.SkillId) ||
                (trigger.SkillId == 0 && !string.IsNullOrEmpty(trigger.SkillName) &&
                 string.Equals(e.SkillName, trigger.SkillName, StringComparison.OrdinalIgnoreCase)));

            return match != null ? match.StatusId.ToString() : "(not mapped yet)";
        }

        private void TriggersGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (!(triggersGrid.Rows[e.RowIndex].Tag is SkillTrigger trigger)) return;

            txtTriggerSkillId.Text = trigger.SkillId != 0 ? trigger.SkillId.ToString() : "";
            txtTriggerSkillName.Text = trigger.SkillName;
            txtTriggerKey.Text = trigger.Key;
        }

        private void BtnAddTrigger_Click(object sender, EventArgs e)
        {
            string skillIdText = txtTriggerSkillId.Text.Trim();
            string skillName = txtTriggerSkillName.Text.Trim();

            if (string.IsNullOrEmpty(skillIdText) && string.IsNullOrEmpty(skillName))
            {
                MessageBox.Show("Type at least a Skill ID or a Skill Name first.", "Add Trigger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(skillIdText) && !int.TryParse(skillIdText, out _))
            {
                MessageBox.Show("Skill ID must be a number.", "Add Trigger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int skillId = int.TryParse(skillIdText, out int parsedId) ? parsedId : 0;

            Key key;
            try
            {
                key = (Key)Enum.Parse(typeof(Key), txtTriggerKey.Text);
            }
            catch
            {
                MessageBox.Show("Press a valid key first.", "Add Trigger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!FormUtils.IsValidKey(key))
            {
                MessageBox.Show("Press a valid key first.", "Add Trigger", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProfileSingleton.GetCurrent().StatusCapture.AddTrigger(skillId, skillName, key);
            ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().StatusCapture);

            txtTriggerSkillId.Text = "";
            txtTriggerSkillName.Text = "";
            txtTriggerKey.Text = Key.None.ToString();
            RenderTriggers();
        }

        private void BtnRemoveTrigger_Click(object sender, EventArgs e)
        {
            if (triggersGrid.SelectedRows.Count == 0 || !(triggersGrid.SelectedRows[0].Tag is SkillTrigger trigger))
            {
                MessageBox.Show("Select a row in the Skill Triggers grid first.", "Skill Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProfileSingleton.GetCurrent().StatusCapture.RemoveTrigger(trigger);
            ProfileSingleton.SetConfiguration(ProfileSingleton.GetCurrent().StatusCapture);
            RenderTriggers();
        }

        private void BtnTestTrigger_Click(object sender, EventArgs e)
        {
            if (triggersGrid.SelectedRows.Count == 0 || !(triggersGrid.SelectedRows[0].Tag is SkillTrigger trigger))
            {
                MessageBox.Show("Select a row in the Skill Triggers grid first.", "Skill Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (ClientSingleton.GetClient() == null)
            {
                MessageBox.Show("Select and attach a Ragnarok client first.", "Skill Triggers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ProfileSingleton.GetCurrent().StatusCapture.TestTrigger(trigger);
            RenderTriggers();
        }

        // ---- Skill Map grid --------------------------------------------------

        // Every Render* method below rebuilds a grid's rows from scratch on every
        // refresh tick (the underlying data has no stable row identity to diff
        // against cheaply), which by itself resets DataGridView's selection and
        // scroll position back to the top every second. This wraps a rebuild to
        // restore both afterward, keyed by whatever the row's Tag was.
        private static void PreserveGridState(DataGridView grid, System.Action rebuild)
        {
            object selectedTag = grid.CurrentRow?.Tag;
            int scrollIndex = grid.Rows.Count > 0 ? grid.FirstDisplayedScrollingRowIndex : -1;

            rebuild();

            if (selectedTag != null)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (Equals(row.Tag, selectedTag))
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

        private void RenderMapGrid()
        {
            if (mapGrid == null) return;

            PreserveGridState(mapGrid, () =>
            {
                var entries = SkillMapManager.Entries.Values.OrderBy(e => e.StatusId).ToList();
                mapGrid.Rows.Clear();

                foreach (SkillMapEntry entry in entries)
                {
                    int rowIndex = mapGrid.Rows.Add(
                        entry.StatusId,
                        entry.SkillId != 0 ? entry.SkillId.ToString() : "",
                        entry.SkillName,
                        entry.Note
                    );
                    mapGrid.Rows[rowIndex].Tag = entry.StatusId;
                }

                if (tabMap != null)
                {
                    tabMap.Text = $"Skill Map ({entries.Count})";
                }
            });
        }

        private void MapGrid_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;

            DataGridViewRow row = mapGrid.Rows[e.RowIndex];
            if (!(row.Tag is uint statusId)) return;
            if (!SkillMapManager.Entries.TryGetValue(statusId, out SkillMapEntry entry)) return;

            txtMapStatusId.Text = entry.StatusId.ToString();
            txtMapSkillId.Text = entry.SkillId != 0 ? entry.SkillId.ToString() : "";
            txtMapSkillName.Text = entry.SkillName;
            txtMapNote.Text = entry.Note;
        }

        private void BtnSaveMapping_Click(object sender, EventArgs e)
        {
            if (!uint.TryParse(txtMapStatusId.Text.Trim(), out uint statusId))
            {
                MessageBox.Show("Status ID must be a number. Double-click a row below to fill it in automatically.", "Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string skillIdText = txtMapSkillId.Text.Trim();
            if (!string.IsNullOrEmpty(skillIdText) && !int.TryParse(skillIdText, out _))
            {
                MessageBox.Show("Skill ID must be a number.", "Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int skillId = int.TryParse(skillIdText, out int parsedId) ? parsedId : 0;

            string skillName = txtMapSkillName.Text.Trim();
            if (skillId == 0 && string.IsNullOrEmpty(skillName))
            {
                MessageBox.Show("Fill in a Skill ID or a Skill Name - use Browse... if you don't know the ID.", "Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SkillMapManager.SetSkill(statusId, skillId, skillName, txtMapNote.Text);
            RenderMapGrid();

            txtMapStatusId.Text = "";
            txtMapSkillId.Text = "";
            txtMapSkillName.Text = "";
            txtMapNote.Text = "";
        }

        private void BtnRemoveMapping_Click(object sender, EventArgs e)
        {
            if (mapGrid.SelectedRows.Count == 0 || !(mapGrid.SelectedRows[0].Tag is uint statusId))
            {
                MessageBox.Show("Select a row in the Skill Map grid first.", "Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SkillMapManager.Remove(statusId);
            RenderMapGrid();
        }

        private void BtnImport_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Skill map JSON (*.json)|*.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                DialogResult overwriteChoice = MessageBox.Show(
                    "Overwrite skill names you've already assigned locally if this file has the same status id?\n\nYes = overwrite, No = keep your existing names.",
                    "Import Skill Map", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (overwriteChoice == DialogResult.Cancel) return;

                try
                {
                    int applied = SkillMapManager.Import(dialog.FileName, overwriteChoice == DialogResult.Yes);
                    MessageBox.Show($"Imported {applied} skill mapping(s).", "Import Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RenderMapGrid();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not import this file: {ex.Message}", "Import Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Skill map JSON (*.json)|*.json", FileName = "SkillMap.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    SkillMapManager.Export(dialog.FileName);
                    MessageBox.Show("Skill map exported.", "Export Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not export this file: {ex.Message}", "Export Skill Map", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---- Captured / Active grid -------------------------------------------

        private void RenderCapturedGrid()
        {
            if (capturedGrid == null) return;

            PreserveGridState(capturedGrid, () =>
            {
                IReadOnlyCollection<uint> activeNow = StatusCapture.CurrentlyActive;
                bool activeOnly = chkActiveOnly.Checked;

                var captured = StatusCapture.Captured.Values
                    .Where(c => !activeOnly || activeNow.Contains(c.StatusId))
                    .OrderByDescending(c => activeNow.Contains(c.StatusId))
                    .ThenBy(c => c.StatusId)
                    .ToList();

                capturedGrid.Rows.Clear();

                foreach (CapturedStatus entry in captured)
                {
                    bool isActive = activeNow.Contains(entry.StatusId);

                    int rowIndex = capturedGrid.Rows.Add(
                        isActive,
                        entry.StatusId,
                        entry.FirstSeenUtc.ToLocalTime().ToString("g"),
                        entry.LastSeenUtc.ToLocalTime().ToString("g"),
                        entry.TimesSeen
                    );
                    capturedGrid.Rows[rowIndex].Tag = entry.StatusId;

                    if (isActive)
                    {
                        capturedGrid.Rows[rowIndex].DefaultCellStyle.BackColor = System.Drawing.Color.LightGreen;
                    }
                }

                if (lblCapturedSummary != null)
                {
                    int totalCount = StatusCapture.Captured.Count;
                    lblCapturedSummary.Text = totalCount == 0
                        ? "Nothing captured yet - turn the automation on and play normally."
                        : $"{totalCount} captured total, {activeNow.Count} active now";
                }
            });
        }

        private void CapturedGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (!(capturedGrid.Rows[e.RowIndex].Tag is uint statusId)) return;

            txtMapStatusId.Text = statusId.ToString();

            // Pre-fill from an existing mapping too, if this status is already linked.
            if (SkillMapManager.Entries.TryGetValue(statusId, out SkillMapEntry entry))
            {
                txtMapSkillId.Text = entry.SkillId != 0 ? entry.SkillId.ToString() : "";
                txtMapSkillName.Text = entry.SkillName;
                txtMapNote.Text = entry.Note;
            }

            txtMapSkillName.Focus();
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("This clears every captured status id (not your skill name mappings). Continue?", "Clear Captured Log", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            StatusCapture.ClearLog();
            RenderCapturedGrid();
        }

        public void Update(ISubject subject)
        {
            switch ((subject as Subject).Message.code)
            {
                case MessageCode.PROFILE_CHANGED:
                    RenderTriggers();
                    break;
                case MessageCode.TURN_ON:
                    ProfileSingleton.GetCurrent().StatusCapture.Start();
                    break;
                case MessageCode.TURN_OFF:
                    ProfileSingleton.GetCurrent().StatusCapture.Stop();
                    break;
                case MessageCode.SKILL_MAP_CHANGED:
                    RenderMapGrid();
                    break;
                case MessageCode.SKILL_TRIGGERS_CHANGED:
                    RenderTriggers();
                    break;
            }
        }
    }
}
