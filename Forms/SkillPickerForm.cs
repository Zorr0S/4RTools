using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using _4RTools.Model;

namespace _4RTools.Forms
{
    // Modal "search by name, get the skill id" picker over the scraped RTM skill
    // database (Model/RTMSkillReference.cs) - the actual payoff of bundling that
    // data: instead of hunting a skill's numeric id externally, search its name
    // here and the matching SkillId/Name fill in for you.
    public class SkillPickerForm : Form
    {
        private TextBox txtSearch;
        private ListBox lstResults;

        public RTMSkillRef SelectedSkill { get; private set; }

        public SkillPickerForm()
        {
            this.Text = "Browse Skills";
            this.Size = new Size(420, 420);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MinimizeBox = false;
            this.MaximizeBox = false;

            Label lbl = new Label { Text = "Search by name:", Location = new Point(10, 10), AutoSize = true };

            txtSearch = new TextBox { Location = new Point(10, 30), Width = 380 };
            txtSearch.TextChanged += (s, e) => RenderResults();

            lstResults = new ListBox { Location = new Point(10, 60), Size = new Size(380, 280), DisplayMember = "Display" };
            lstResults.DoubleClick += (s, e) => AcceptSelection();

            Button btnOk = new Button { Text = "Select", Location = new Point(230, 350), Width = 75 };
            btnOk.Click += (s, e) => AcceptSelection();

            Button btnCancel = new Button { Text = "Cancel", Location = new Point(315, 350), Width = 75, DialogResult = DialogResult.Cancel };

            this.Controls.Add(lbl);
            this.Controls.Add(txtSearch);
            this.Controls.Add(lstResults);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            txtSearch.Focus();
        }

        private void RenderResults()
        {
            lstResults.Items.Clear();
            foreach (RTMSkillRef skill in RTMSkillReference.Search(txtSearch.Text, 50))
            {
                lstResults.Items.Add(new PickerItem(skill));
            }
        }

        private void AcceptSelection()
        {
            if (!(lstResults.SelectedItem is PickerItem item)) return;

            SelectedSkill = item.Skill;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private class PickerItem
        {
            public RTMSkillRef Skill { get; }

            public PickerItem(RTMSkillRef skill)
            {
                Skill = skill;
            }

            public override string ToString()
            {
                string id = Skill.SkillId.HasValue ? $"[{Skill.SkillId}] " : "[no id] ";
                return $"{id}{Skill.Name} - {Skill.Class}";
            }
        }
    }
}
