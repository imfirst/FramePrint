using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FramePrint
{
    public class TemplatesForm : Form
    {
        Settings st;
        List<PrintTemplate> work = new List<PrintTemplate>();
        ComboBox cbTpl;
        NumericUpDown nuTol;
        DataGridView grid;
        List<string> devices, styles;
        bool loading;
        public string ActiveName;

        public TemplatesForm(Settings settings, string active)
        {
            st = settings;
            foreach (PrintTemplate t in st.Templates) work.Add(t.Clone());
            ActiveName = active;
            Text = "Шаблоны печати";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(1150, 620);
            StartPosition = FormStartPosition.CenterParent;
            try { devices = Cad.Devices(); } catch { devices = new List<string>(); }
            try { styles = Cad.Styles(); } catch { styles = new List<string>(); }
            devices.Insert(0, ""); styles.Insert(0, "");
            Build();
            ReloadCombo(active);
        }

        void Build()
        {
            FlowLayoutPanel top = new FlowLayoutPanel(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(6);
            top.Controls.Add(L("Шаблон:"));
            cbTpl = new ComboBox(); cbTpl.DropDownStyle = ComboBoxStyle.DropDownList; cbTpl.Width = 200;
            cbTpl.SelectedIndexChanged += delegate { LoadGrid(); };
            top.Controls.Add(cbTpl);
            top.Controls.Add(B("Создать копию", delegate { NewTemplate(); }));
            top.Controls.Add(B("Переименовать", delegate { RenameTemplate(); }));
            top.Controls.Add(B("Удалить", delegate { DeleteTemplate(); }));
            top.Controls.Add(L("    Допуск размеров, мм:"));
            nuTol = new NumericUpDown(); nuTol.DecimalPlaces = 1; nuTol.Increment = 0.5m; nuTol.Minimum = 0; nuTol.Maximum = 50; nuTol.Width = 60;
            nuTol.Value = (decimal)Math.Min(50, Math.Max(0, st.Tolerance));
            top.Controls.Add(nuTol);
            top.Controls.Add(B("Обновить принтеры", delegate { Cad.ClearCache(); try { devices = Cad.Devices(); devices.Insert(0, ""); } catch { } LoadGrid(); }));

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersWidth = 24;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.DataError += delegate (object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            Txt("Name", "Формат", 90);
            Txt("W", "Ширина", 65);
            Txt("H", "Высота", 65);
            Cmb("Device", "Принтер", 230, devices);
            Cmb("Media", "Лист", 270, new List<string>());
            Cmb("Style", "Стиль", 120, styles);
            Cmb("Fit", "Масштаб", 80, new List<string>(new string[] { "Вписать", "1:1" }));
            Cmb("Flip", "Переворот", 75, new List<string>(new string[] { "0", "90", "180", "270" }));
            grid.CellValueChanged += OnCellChanged;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };

            FlowLayoutPanel left = new FlowLayoutPanel(); left.Dock = DockStyle.Left; left.FlowDirection = FlowDirection.TopDown; left.Width = 100; left.Padding = new Padding(4);
            left.Controls.Add(B("+ Строка", delegate { AddRow(); }, 90));
            left.Controls.Add(B("− Строка", delegate { DelRow(); }, 90));
            left.Controls.Add(B("▲ Вверх", delegate { MoveRow(-1); }, 90));
            left.Controls.Add(B("▼ Вниз", delegate { MoveRow(1); }, 90));

            FlowLayoutPanel bottom = new FlowLayoutPanel(); bottom.Dock = DockStyle.Bottom; bottom.AutoSize = true; bottom.Padding = new Padding(6);
            bottom.Controls.Add(B("По первой строке…", delegate { ByFirstRow(); }, 150));
            bottom.Controls.Add(B("Сохранить", delegate { SaveAll(); }, 110));
            bottom.Controls.Add(B("Закрыть", delegate { Close(); }, 90));
            Label hint = L("   Переворот добавляется к автоповороту. Лист бумаги — из списка выбранного принтера.");
            hint.ForeColor = SystemColors.GrayText;
            bottom.Controls.Add(hint);

            Controls.Add(grid); Controls.Add(left); Controls.Add(top); Controls.Add(bottom);
        }

        static Label L(string t) { Label l = new Label(); l.Text = t; l.AutoSize = true; l.Padding = new Padding(0, 6, 0, 0); return l; }
        static Button B(string t, EventHandler h, int w = 0)
        {
            Button b = new Button(); b.Text = t; b.Click += h; b.Height = 28;
            if (w > 0) b.Width = w; else b.AutoSize = true;
            return b;
        }
        void Txt(string n, string h, int w)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn(); c.Name = n; c.HeaderText = h; c.Width = w;
            c.SortMode = DataGridViewColumnSortMode.NotSortable; grid.Columns.Add(c);
        }
        void Cmb(string n, string h, int w, List<string> items)
        {
            DataGridViewComboBoxColumn c = new DataGridViewComboBoxColumn(); c.Name = n; c.HeaderText = h; c.Width = w;
            c.FlatStyle = FlatStyle.Flat; c.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
            foreach (string s in items) c.Items.Add(s);
            grid.Columns.Add(c);
        }

        PrintTemplate Cur
        {
            get
            {
                string n = cbTpl.SelectedItem as string;
                foreach (PrintTemplate t in work) if (t.Name == n) return t;
                return null;
            }
        }

        void ReloadCombo(string sel)
        {
            cbTpl.Items.Clear();
            foreach (PrintTemplate t in work) cbTpl.Items.Add(t.Name);
            int i = cbTpl.Items.IndexOf(sel);
            cbTpl.SelectedIndex = i >= 0 ? i : (cbTpl.Items.Count > 0 ? 0 : -1);
        }

        static void Ensure(DataGridViewComboBoxCell c, string v)
        {
            if (v == null) v = "";
            if (!c.Items.Contains(v)) c.Items.Add(v);
            c.Value = v;
        }

        void FillMedia(DataGridViewRow r, string device, string media)
        {
            DataGridViewComboBoxCell c = (DataGridViewComboBoxCell)r.Cells["Media"];
            c.Items.Clear();
            c.Items.Add("");
            foreach (string[] m in Cad.Media(device)) if (!c.Items.Contains(m[1])) c.Items.Add(m[1]);
            Ensure(c, media);
        }

        void LoadGrid()
        {
            PrintTemplate t = Cur;
            loading = true;
            grid.Rows.Clear();
            if (t != null)
            {
                Cursor = Cursors.WaitCursor;
                foreach (TemplateRow r in t.Rows)
                {
                    int k = grid.Rows.Add();
                    DataGridViewRow gr = grid.Rows[k];
                    gr.Cells["Name"].Value = r.Name;
                    gr.Cells["W"].Value = r.Width.ToString(CultureInfo.InvariantCulture);
                    gr.Cells["H"].Value = r.Height.ToString(CultureInfo.InvariantCulture);
                    Ensure((DataGridViewComboBoxCell)gr.Cells["Device"], r.Device);
                    FillMedia(gr, r.Device, r.Media);
                    Ensure((DataGridViewComboBoxCell)gr.Cells["Style"], r.Style);
                    gr.Cells["Fit"].Value = r.Fit ? "Вписать" : "1:1";
                    Ensure((DataGridViewComboBoxCell)gr.Cells["Flip"], r.Flip.ToString(CultureInfo.InvariantCulture));
                }
                Cursor = Cursors.Default;
            }
            loading = false;
        }

        void OnCellChanged(object s, DataGridViewCellEventArgs e)
        {
            if (loading || e.RowIndex < 0) return;
            if (grid.Columns[e.ColumnIndex].Name == "Device")
            {
                DataGridViewRow r = grid.Rows[e.RowIndex];
                Cursor = Cursors.WaitCursor;
                FillMedia(r, Convert.ToString(r.Cells["Device"].Value), "");
                Cursor = Cursors.Default;
            }
        }

        /// <summary>Перенести значения из таблицы в рабочую копию шаблона.</summary>
        void Collect()
        {
            PrintTemplate t = Cur;
            if (t == null) return;
            grid.EndEdit();
            t.Rows.Clear();
            foreach (DataGridViewRow gr in grid.Rows)
            {
                TemplateRow r = new TemplateRow();
                r.Name = Convert.ToString(gr.Cells["Name"].Value).Trim();
                if (r.Name.Length == 0) continue;
                double v;
                double.TryParse(Convert.ToString(gr.Cells["W"].Value).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v); r.Width = v;
                double.TryParse(Convert.ToString(gr.Cells["H"].Value).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v); r.Height = v;
                r.Device = Convert.ToString(gr.Cells["Device"].Value);
                r.Media = Convert.ToString(gr.Cells["Media"].Value);
                r.Style = Convert.ToString(gr.Cells["Style"].Value);
                r.Fit = Convert.ToString(gr.Cells["Fit"].Value) != "1:1";
                int f; int.TryParse(Convert.ToString(gr.Cells["Flip"].Value), out f); r.Flip = f;
                t.Rows.Add(r);
            }
        }

        void AddRow()
        {
            int at = grid.CurrentRow != null ? grid.CurrentRow.Index + 1 : grid.Rows.Count;
            loading = true;
            grid.Rows.Insert(at, 1);
            DataGridViewRow gr = grid.Rows[at];
            gr.Cells["Name"].Value = "Новый"; gr.Cells["W"].Value = "297"; gr.Cells["H"].Value = "210";
            Ensure((DataGridViewComboBoxCell)gr.Cells["Device"], "");
            FillMedia(gr, "", "");
            Ensure((DataGridViewComboBoxCell)gr.Cells["Style"], "");
            gr.Cells["Fit"].Value = "Вписать";
            Ensure((DataGridViewComboBoxCell)gr.Cells["Flip"], "0");
            loading = false;
            grid.CurrentCell = gr.Cells["Name"];
        }

        void DelRow()
        {
            if (grid.CurrentRow != null) grid.Rows.RemoveAt(grid.CurrentRow.Index);
        }

        void MoveRow(int d)
        {
            if (grid.CurrentRow == null) return;
            Collect();
            int i = grid.CurrentRow.Index, j = i + d;
            PrintTemplate t = Cur;
            if (t == null || j < 0 || j >= t.Rows.Count) return;
            TemplateRow x = t.Rows[i]; t.Rows[i] = t.Rows[j]; t.Rows[j] = x;
            LoadGrid();
            grid.CurrentCell = grid.Rows[j].Cells[0];
        }

        void ByFirstRow()
        {
            if (grid.Rows.Count < 2) return;
            using (Form f = new Form())
            {
                f.Text = "Заполнить все строки по первой"; f.Font = Font; f.Size = new Size(560, 110);
                f.StartPosition = FormStartPosition.CenterParent; f.FormBorderStyle = FormBorderStyle.FixedDialog;
                FlowLayoutPanel p = new FlowLayoutPanel(); p.Dock = DockStyle.Fill; p.Padding = new Padding(8);
                string[] cols = { "Device", "Style", "Fit", "Flip" };
                string[] names = { "Принтер", "Стиль", "Масштаб", "Переворот" };
                for (int i = 0; i < cols.Length; i++)
                {
                    string c = cols[i];
                    p.Controls.Add(B(names[i], delegate { CopyFirst(c); f.Close(); }, 95));
                }
                p.Controls.Add(B("Все", delegate { foreach (string c in cols) CopyFirst(c); f.Close(); }, 80));
                f.Controls.Add(p);
                f.ShowDialog(this);
            }
        }

        void CopyFirst(string col)
        {
            string v = Convert.ToString(grid.Rows[0].Cells[col].Value);
            for (int i = 1; i < grid.Rows.Count; i++)
            {
                DataGridViewCell c = grid.Rows[i].Cells[col];
                DataGridViewComboBoxCell cc = c as DataGridViewComboBoxCell;
                if (cc != null) Ensure(cc, v); else c.Value = v;
            }
            if (col == "Device")
            {
                // подобрать лист той же ориентации/размера у нового принтера
                for (int i = 1; i < grid.Rows.Count; i++)
                    FillMedia(grid.Rows[i], v, Convert.ToString(grid.Rows[i].Cells["Media"].Value));
            }
        }

        string Ask(string title, string def)
        {
            using (Form f = new Form())
            {
                f.Text = title; f.Font = Font; f.Size = new Size(360, 130); f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
                TextBox tb = new TextBox(); tb.Text = def; tb.SetBounds(12, 12, 320, 24);
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(160, 48, 80, 28);
                Button cn = new Button(); cn.Text = "Отмена"; cn.DialogResult = DialogResult.Cancel; cn.SetBounds(250, 48, 80, 28);
                f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cn); f.AcceptButton = ok; f.CancelButton = cn;
                return f.ShowDialog(this) == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }

        void NewTemplate()
        {
            Collect();
            PrintTemplate src = Cur;
            string n = Ask("Имя нового шаблона", src == null ? "Новый" : src.Name + " копия");
            if (string.IsNullOrEmpty(n)) return;
            foreach (PrintTemplate t in work) if (t.Name == n) { MessageBox.Show(this, "Такой шаблон уже есть."); return; }
            PrintTemplate c = src == null ? new PrintTemplate() : src.Clone();
            c.Name = n;
            work.Add(c);
            ReloadCombo(n);
        }

        void RenameTemplate()
        {
            PrintTemplate t = Cur;
            if (t == null) return;
            Collect();
            string n = Ask("Новое имя шаблона", t.Name);
            if (string.IsNullOrEmpty(n) || n == t.Name) return;
            foreach (PrintTemplate x in work) if (x.Name == n) { MessageBox.Show(this, "Такой шаблон уже есть."); return; }
            t.Name = n;
            ReloadCombo(n);
        }

        void DeleteTemplate()
        {
            PrintTemplate t = Cur;
            if (t == null || work.Count < 2) return;
            if (MessageBox.Show(this, "Удалить шаблон «" + t.Name + "»?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            work.Remove(t);
            ReloadCombo(null);
        }

        void SaveAll()
        {
            Collect();
            st.Templates = new List<PrintTemplate>();
            foreach (PrintTemplate t in work) st.Templates.Add(t.Clone());
            st.Tolerance = (double)nuTol.Value;
            ActiveName = cbTpl.SelectedItem as string;
            st.ActiveTemplate = ActiveName;
            try { st.Save(); MessageBox.Show(this, "Шаблоны сохранены.", Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (ActiveName == null) ActiveName = cbTpl.SelectedItem as string;
            base.OnFormClosing(e);
        }
    }
}
