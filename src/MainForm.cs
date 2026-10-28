using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Font = System.Drawing.Font;
using FlowDirection = System.Windows.Forms.FlowDirection;

namespace FramePrint
{
    public class MainForm : Form
    {
        Settings st;
        List<FrameRow> rows = new List<FrameRow>();

        ComboBox cbTemplate, cbSort;
        TextBox tbScale, tbLayers, tbSro;
        CheckBox chModel, chLayouts, chSro, chReverse;
        NumericUpDown nuSroAfter, nuCopies;
        Label lbSroInfo, lbStatus;
        DataGridView grid;

        public MainForm()
        {
            st = Settings.Current;
            Text = "Пакетная печать рамок  v" + Cad.Version;
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1240, 720);
            MinimumSize = new Size(900, 500);
            KeyPreview = true;
            Build();
            LoadFromSettings();
        }

        // ------------------------------------------------------------------ UI
        void Build()
        {
            // верхняя панель
            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(6); top.WrapContents = true;

            top.Controls.Add(Lbl("Шаблон:"));
            cbTemplate = new ComboBox(); cbTemplate.DropDownStyle = ComboBoxStyle.DropDownList; cbTemplate.Width = 170;
            cbTemplate.SelectedIndexChanged += delegate { OnTemplateChanged(); };
            top.Controls.Add(cbTemplate);
            top.Controls.Add(Btn("Шаблоны…", delegate { EditTemplates(); }));

            top.Controls.Add(Lbl("   Масштаб:"));
            tbScale = new TextBox(); tbScale.Width = 70; top.Controls.Add(tbScale);
            top.Controls.Add(Lbl("Слои:"));
            tbLayers = new TextBox(); tbLayers.Width = 110; top.Controls.Add(tbLayers);
            chModel = new CheckBox(); chModel.Text = "Модель"; chModel.AutoSize = true; top.Controls.Add(chModel);
            chLayouts = new CheckBox(); chLayouts.Text = "Листы"; chLayouts.AutoSize = true; top.Controls.Add(chLayouts);
            top.Controls.Add(Lbl("Сортировка:"));
            cbSort = new ComboBox(); cbSort.DropDownStyle = ComboBoxStyle.DropDownList; cbSort.Width = 150;
            cbSort.Items.AddRange(new object[] { "Y-X (строками)", "X-Y (столбцами)", "По № листа", "Без сортировки" });
            top.Controls.Add(cbSort);
            top.Controls.Add(Btn("Настройки…", delegate { EditSettings(); }));

            // таблица
            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.RowHeadersWidth = 24;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            grid.BackgroundColor = SystemColors.Window;
            grid.DataError += delegate (object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            AddCol("No", "№", 36, true);
            DataGridViewCheckBoxColumn chk = new DataGridViewCheckBoxColumn(); chk.Name = "Print"; chk.HeaderText = "Печать"; chk.Width = 55; grid.Columns.Add(chk);
            AddCol("Tab", "Вкладка", 70, true);
            DataGridViewComboBoxColumn fmt = new DataGridViewComboBoxColumn(); fmt.Name = "Fmt"; fmt.HeaderText = "Формат"; fmt.Width = 95; fmt.FlatStyle = FlatStyle.Flat; grid.Columns.Add(fmt);
            AddCol("Num", "Лист №", 55, true);
            AddCol("Total", "Листов", 50, true);
            AddCol("Name", "Наименование", 260, true);
            AddCol("Kind", "Штамп", 65, true);
            AddCol("Cipher", "Шифр", 90, true);
            AddCol("Section", "Раздел", 55, true);
            AddCol("Device", "Принтер", 150, true);
            AddCol("Media", "Лист бумаги", 190, true);
            AddCol("Style", "Стиль", 70, true);
            AddCol("Copies", "Кол-во", 50, false);
            grid.CellValueChanged += OnCellChanged;
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellDoubleClick += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].ReadOnly) ShowRow(e.RowIndex);
            };

            // левая панель кнопок строк
            FlowLayoutPanel left = new FlowLayoutPanel();
            left.Dock = DockStyle.Left; left.FlowDirection = FlowDirection.TopDown; left.Width = 92; left.Padding = new Padding(4);
            left.Controls.Add(Btn("Удалить", delegate { DeleteSelected(); }, 82));
            left.Controls.Add(Btn("▲ Вверх", delegate { MoveRow(-1); }, 82));
            left.Controls.Add(Btn("▼ Вниз", delegate { MoveRow(1); }, 82));
            left.Controls.Add(Btn("Показать", delegate { if (grid.CurrentRow != null) ShowRow(grid.CurrentRow.Index); }, 82));
            left.Controls.Add(Btn("Все ☑", delegate { SetAllPrint(true); }, 82));
            left.Controls.Add(Btn("Все ☐", delegate { SetAllPrint(false); }, 82));

            // нижняя панель: СРО
            Panel sro = new Panel(); sro.Dock = DockStyle.Bottom; sro.Height = 36; sro.Padding = new Padding(6, 6, 6, 0);
            FlowLayoutPanel sf = new FlowLayoutPanel(); sf.Dock = DockStyle.Fill; sf.WrapContents = false;
            chSro = new CheckBox(); chSro.Text = "Вставить допуск СРО после листа"; chSro.AutoSize = true; sf.Controls.Add(chSro);
            nuSroAfter = new NumericUpDown(); nuSroAfter.Minimum = 0; nuSroAfter.Maximum = 999; nuSroAfter.Width = 50; sf.Controls.Add(nuSroAfter);
            tbSro = new TextBox(); tbSro.Width = 420; tbSro.ReadOnly = true; sf.Controls.Add(tbSro);
            sf.Controls.Add(Btn("Выбрать PDF…", delegate { PickSro(); }));
            lbSroInfo = new Label(); lbSroInfo.AutoSize = true; lbSroInfo.Padding = new Padding(6, 6, 0, 0); sf.Controls.Add(lbSroInfo);
            sro.Controls.Add(sf);

            // нижняя панель: действия
            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom; bottom.AutoSize = true; bottom.Padding = new Padding(6);
            bottom.Controls.Add(Btn("Найти все форматы", delegate { Find(false); }, 140));
            bottom.Controls.Add(Btn("Выбрать рамкой", delegate { Find(true); }, 120));
            bottom.Controls.Add(Btn("Нумерация", delegate { Numerate(); }, 100));
            bottom.Controls.Add(Btn("Ведомость", delegate { SheetList(); }, 100));
            bottom.Controls.Add(Btn("В CSV", delegate { ExportCsv(); }, 70));
            bottom.Controls.Add(Lbl("     Копий:"));
            nuCopies = new NumericUpDown(); nuCopies.Minimum = 1; nuCopies.Maximum = 99; nuCopies.Width = 45; nuCopies.Value = 1; bottom.Controls.Add(nuCopies);
            chReverse = new CheckBox(); chReverse.Text = "С последнего"; chReverse.AutoSize = true; chReverse.Padding = new Padding(0, 4, 0, 0);
            bottom.Controls.Add(chReverse);
            Button bPrint = Btn("Печать", delegate { DoPublish(false); }, 100);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(nuCopies, "Сколько комплектов печатать на принтер (каждый комплект целиком). В PDF всегда один.");
            tip.SetToolTip(chReverse, "Печать на принтер от последнего листа к первому — пачка выходит в правильном порядке.\nСписок и PDF не меняются.");
            Button bPdf = Btn("Публикация в PDF", delegate { DoPublish(true); }, 140);
            bPdf.Font = new Font(Font, FontStyle.Bold);
            bottom.Controls.Add(bPrint); bottom.Controls.Add(bPdf);
            bottom.Controls.Add(Btn("Закрыть", delegate { Close(); }, 80));
            lbStatus = new Label(); lbStatus.AutoSize = true; lbStatus.Padding = new Padding(10, 7, 0, 0); bottom.Controls.Add(lbStatus);

            Controls.Add(grid);
            Controls.Add(left);
            Controls.Add(top);
            Controls.Add(sro);
            Controls.Add(bottom);
        }

        static Label Lbl(string t)
        {
            Label l = new Label(); l.Text = t; l.AutoSize = true; l.Padding = new Padding(0, 6, 0, 0); return l;
        }

        static Button Btn(string t, EventHandler h, int w = 0)
        {
            Button b = new Button(); b.Text = t; b.Click += h;
            if (w > 0) { b.Width = w; b.Height = 28; } else { b.AutoSize = true; b.Height = 28; }
            return b;
        }

        void AddCol(string name, string header, int w, bool ro)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name; c.HeaderText = header; c.Width = w; c.ReadOnly = ro;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(c);
        }

        // ------------------------------------------------------------ настройки
        void LoadFromSettings()
        {
            cbTemplate.Items.Clear();
            foreach (PrintTemplate t in st.Templates) cbTemplate.Items.Add(t.Name);
            int i = cbTemplate.Items.IndexOf(st.ActiveTemplate);
            cbTemplate.SelectedIndex = i >= 0 ? i : (cbTemplate.Items.Count > 0 ? 0 : -1);
            tbScale.Text = st.Scales;
            tbLayers.Text = st.Layers;
            chModel.Checked = st.SearchModel;
            chLayouts.Checked = st.SearchLayouts;
            cbSort.SelectedIndex = (int)st.Sort;
            chSro.Checked = st.SroEnabled;
            nuSroAfter.Value = Math.Max(0, Math.Min(999, st.SroAfterPage));
            nuCopies.Value = 1;                       // копий по умолчанию — всегда одна
            chReverse.Checked = st.PrintReverse;
            tbSro.Text = st.SroPath;
            UpdateSroInfo();
        }

        void SaveToSettings()
        {
            if (cbTemplate.SelectedItem != null) st.ActiveTemplate = (string)cbTemplate.SelectedItem;
            st.Scales = tbScale.Text.Trim();
            st.Layers = tbLayers.Text.Trim();
            st.SearchModel = chModel.Checked;
            st.SearchLayouts = chLayouts.Checked;
            st.Sort = (SortMode)Math.Max(0, cbSort.SelectedIndex);
            st.SroEnabled = chSro.Checked;
            st.SroAfterPage = (int)nuSroAfter.Value;
            st.Copies = (int)nuCopies.Value;
            st.PrintReverse = chReverse.Checked;
            st.SroPath = tbSro.Text;
            try { st.Save(); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveToSettings();
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && grid.Focused && !grid.IsCurrentCellInEditMode) { DeleteSelected(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        PrintTemplate Tpl { get { return st.GetTemplate(cbTemplate.SelectedItem as string); } }

        void OnTemplateChanged()
        {
            PrintTemplate t = Tpl;
            if (t == null) return;
            DataGridViewComboBoxColumn c = (DataGridViewComboBoxColumn)grid.Columns["Fmt"];
            c.Items.Clear();
            foreach (TemplateRow r in t.Rows) if (!c.Items.Contains(r.Name)) c.Items.Add(r.Name);
            c.Items.Add("СРО");
            // перепривязать найденные рамки к строкам нового шаблона по размеру
            foreach (FrameRow f in rows)
            {
                if (f.IsSro || f.Fmt == null) continue;
                TemplateRow best = null; double be = double.MaxValue;
                foreach (TemplateRow r in t.Rows)
                {
                    double e = Math.Max(Math.Abs(f.W / f.Scale - r.Width), Math.Abs(f.H / f.Scale - r.Height));
                    if (e < be) { be = e; best = r; }
                }
                if (best != null && be <= st.Tolerance) f.Fmt = best;
            }
            Fill();
        }

        void UpdateSroInfo()
        {
            string p = tbSro.Text;
            if (string.IsNullOrEmpty(p)) { lbSroInfo.Text = "файл не выбран"; lbSroInfo.ForeColor = SystemColors.GrayText; return; }
            if (!File.Exists(p)) { lbSroInfo.Text = "файл не найден!"; lbSroInfo.ForeColor = Color.Firebrick; return; }
            DateTime d = File.GetLastWriteTime(p);
            int pages = st.SroPageCount > 0 ? st.SroPageCount : PdfMerge.PageCount(p);
            if (pages <= 0) pages = Cad.PdfPageCount(p);
            int age = (int)(DateTime.Now - d).TotalDays;
            lbSroInfo.Text = "стр.: " + pages + ", файл от " + d.ToString("dd.MM.yyyy") + (age > st.SroMaxAgeDays ? " — пора обновить!" : "");
            lbSroInfo.ForeColor = age > st.SroMaxAgeDays ? Color.DarkOrange : SystemColors.ControlText;
        }

        void PickSro()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "PDF (*.pdf)|*.pdf";
                d.Title = "Файл допуска СРО";
                if (File.Exists(tbSro.Text)) d.InitialDirectory = Path.GetDirectoryName(tbSro.Text);
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    tbSro.Text = d.FileName;
                    chSro.Checked = true;
                    SaveToSettings();
                    UpdateSroInfo();
                }
            }
        }

        // ---------------------------------------------------------------- таблица
        void Fill()
        {
            grid.SuspendLayout();
            int cur = grid.CurrentRow != null ? grid.CurrentRow.Index : -1;
            grid.Rows.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                FrameRow f = rows[i];
                TemplateRow t = f.Fmt;
                int k = grid.Rows.Add(i + 1, f.Print, f.LayoutName, t == null ? "" : t.Name, f.Number, f.Total, f.Name, f.StampKind, Cad.SectionOf(f.Cipher), Cad.MainSection(f.Cipher),
                    t == null ? "" : t.Device, t == null ? "" : t.Media, t == null ? "" : t.Style, f.Copies);
                if (t == null || string.IsNullOrEmpty(t.Device)) grid.Rows[k].DefaultCellStyle.BackColor = Color.MistyRose;
                else if (!f.HasStamp) grid.Rows[k].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 225);
            }
            if (cur >= 0 && cur < grid.Rows.Count) grid.CurrentCell = grid.Rows[cur].Cells[0];
            grid.ResumeLayout();
            lbStatus.Text = "Найдено рамок: " + rows.Count;
        }

        void OnCellChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count) return;
            FrameRow f = rows[e.RowIndex];
            string col = grid.Columns[e.ColumnIndex].Name;
            object v = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            if (col == "Print") f.Print = v is bool && (bool)v;
            else if (col == "Copies") { int n; if (int.TryParse(Convert.ToString(v), out n) && n > 0) f.Copies = n; }
            else if (col == "Fmt")
            {
                TemplateRow r = Tpl == null ? null : Tpl.Find(Convert.ToString(v));
                if (r != null)
                {
                    f.Fmt = r;
                    DataGridViewRow gr = grid.Rows[e.RowIndex];
                    gr.Cells["Device"].Value = r.Device; gr.Cells["Media"].Value = r.Media; gr.Cells["Style"].Value = r.Style;
                }
            }
        }

        List<int> SelectedIdx()
        {
            List<int> res = new List<int>();
            foreach (DataGridViewRow r in grid.SelectedRows) res.Add(r.Index);
            if (res.Count == 0 && grid.CurrentRow != null) res.Add(grid.CurrentRow.Index);
            res.Sort();
            return res;
        }

        void DeleteSelected()
        {
            List<int> idx = SelectedIdx();
            for (int i = idx.Count - 1; i >= 0; i--) rows.RemoveAt(idx[i]);
            Fill();
        }

        void MoveRow(int d)
        {
            if (grid.CurrentRow == null) return;
            int i = grid.CurrentRow.Index, j = i + d;
            if (j < 0 || j >= rows.Count) return;
            FrameRow t = rows[i]; rows[i] = rows[j]; rows[j] = t;
            Fill();
            grid.CurrentCell = grid.Rows[j].Cells[0];
        }

        void SetAllPrint(bool v)
        {
            foreach (FrameRow f in rows) f.Print = v;
            Fill();
        }

        void ShowRow(int i)
        {
            if (i < 0 || i >= rows.Count) return;
            try { Cad.ZoomTo(rows[i]); } catch (Exception ex) { Err(ex); }
        }

        // ---------------------------------------------------------------- действия
        void Find(bool byWindow)
        {
            SaveToSettings();
            PrintTemplate t = Tpl;
            if (t == null) { MessageBox.Show(this, "Нет шаблона."); return; }
            try
            {
                List<ObjectId> only = null;
                if (byWindow)
                {
                    Editor ed = Cad.Doc.Editor;
                    using (EditorUserInteraction ui = ed.StartUserInteraction(this))
                    {
                        PromptSelectionOptions po = new PromptSelectionOptions();
                        po.MessageForAdding = "\nВыберите рамки: ";
                        SelectionFilter flt = new SelectionFilter(new TypedValue[] { new TypedValue(0, "LWPOLYLINE,POLYLINE") });
                        PromptSelectionResult r = ed.GetSelection(po, flt);
                        ui.End();
                        if (r.Status != PromptStatus.OK) return;
                        only = new List<ObjectId>(r.Value.GetObjectIds());
                    }
                }
                Cursor = Cursors.WaitCursor;
                rows = Cad.FindFrames(st, t, only);
                foreach (FrameRow f in rows) f.Copies = 1;
                Fill();
                if (rows.Count == 0)
                    MessageBox.Show(this, "Рамки не найдены.\nПроверьте слой (" + st.Layers + "), масштаб (" + st.Scales + ") и размеры в шаблоне «" + t.Name + "».",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Err(ex); }
            finally { Cursor = Cursors.Default; }
        }

        void Numerate()
        {
            if (rows.Count == 0) { MessageBox.Show(this, "Сначала найдите рамки."); return; }
            List<FrameRow> target = rows;
            List<int> idx = SelectedIdx();
            if (idx.Count > 1)
            {
                target = new List<FrameRow>();
                foreach (int i in idx) target.Add(rows[i]);
            }
            string ask = "Пронумеровать " + (idx.Count > 1 ? "выделенные листы (" + idx.Count + ")" : "все листы") +
                         " начиная с " + st.StartNumber + "?" + (st.SkipTitle ? "\nЛисты без штампа (титул) пропускаются." : "");
            if (MessageBox.Show(this, ask, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                string msg = Cad.Numerate(st, target);
                Fill();
                MessageBox.Show(this, msg, Text);
            }
            catch (Exception ex) { Err(ex); }
        }

        void SheetList()
        {
            if (rows.Count == 0) { MessageBox.Show(this, "Сначала найдите рамки."); return; }
            try
            {
                double scale = st.VedScale > 0 ? st.VedScale : st.ScaleList()[0];
                Editor ed = Cad.Doc.Editor;
                using (EditorUserInteraction ui = ed.StartUserInteraction(this))
                {
                    PromptPointResult r = ed.GetPoint("\nТочка вставки ведомости (левый верхний угол): ");
                    ui.End();
                    if (r.Status != PromptStatus.OK) return;
                    Point3d p = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    Cad.InsertSheetList(st, rows, p, scale);
                }
            }
            catch (Exception ex) { Err(ex); }
        }

        void ExportCsv()
        {
            if (rows.Count == 0) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CSV для Excel (*.csv)|*.csv";
                d.FileName = Path.GetFileNameWithoutExtension(Cad.Doc.Name) + " - ведомость.csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { Cad.ExportCsv(rows, d.FileName); } catch (Exception ex) { Err(ex); }
            }
        }

        void DoPublish(bool pdf)
        {
            SaveToSettings();
            if (rows.Count == 0) { MessageBox.Show(this, "Сначала найдите рамки."); return; }
            if (pdf && st.SroEnabled && File.Exists(st.SroPath))
            {
                int age = (int)(DateTime.Now - File.GetLastWriteTime(st.SroPath)).TotalDays;
                if (age > st.SroMaxAgeDays &&
                    MessageBox.Show(this, "Файлу допуска СРО " + age + " дн. Продолжить с ним?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                string msg;
                using (EditorUserInteraction ui = Cad.Doc.Editor.StartUserInteraction(this))
                {
                    msg = Cad.Publish(st, rows, pdf, cbTemplate.SelectedItem as string);
                    ui.End();
                }
                Cursor = Cursors.Default;
                // без замечаний — только строка в командной строке AutoCAD; с замечаниями — окно
                try { Cad.Doc.Editor.WriteMessage("\nFramePrint: " + msg.Replace("\n", "\nFramePrint: ") + "\n"); } catch { }
                if (!Cad.LastOk) MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { Cursor = Cursors.Default; Err(ex); }
        }

        void EditTemplates()
        {
            SaveToSettings();
            using (TemplatesForm f = new TemplatesForm(st, cbTemplate.SelectedItem as string))
            {
                f.ShowDialog(this);
                string sel = f.ActiveName;
                cbTemplate.Items.Clear();
                foreach (PrintTemplate t in st.Templates) cbTemplate.Items.Add(t.Name);
                int i = cbTemplate.Items.IndexOf(sel);
                cbTemplate.SelectedIndex = i >= 0 ? i : (cbTemplate.Items.Count > 0 ? 0 : -1);
            }
        }

        void EditSettings()
        {
            SaveToSettings();
            using (SettingsForm f = new SettingsForm(st))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    List<PrintTemplate> tpls = st.Templates;
                    st = f.Result;
                    st.Templates = tpls;
                    Settings.Current = st;
                    try { st.Save(); } catch (Exception ex) { Err(ex); }
                    LoadFromSettings();
                }
            }
        }

        void Err(Exception ex)
        {
            MessageBox.Show(this, ex.Message + "\n\n" + ex.GetType().Name, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =========================================================================
    public class SettingsForm : Form
    {
        public Settings Result;

        public SettingsForm(Settings st)
        {
            Result = st.Clone();
            Text = "Настройки";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(620, 640);
            StartPosition = FormStartPosition.CenterParent;
            PropertyGrid pg = new PropertyGrid();
            pg.Dock = DockStyle.Fill;
            pg.PropertySort = PropertySort.Categorized;
            pg.ToolbarVisible = false;
            pg.SelectedObject = Result;
            FlowLayoutPanel b = new FlowLayoutPanel(); b.Dock = DockStyle.Bottom; b.FlowDirection = FlowDirection.RightToLeft; b.Height = 40; b.Padding = new Padding(6);
            Button ok = new Button(); ok.Text = "Сохранить"; ok.DialogResult = DialogResult.OK; ok.Width = 100;
            Button cancel = new Button(); cancel.Text = "Отмена"; cancel.DialogResult = DialogResult.Cancel; cancel.Width = 90;
            Button def = new Button(); def.Text = "По умолчанию"; def.Width = 110;
            def.Click += delegate
            {
                if (MessageBox.Show(this, "Сбросить настройки (кроме шаблонов)?", Text, MessageBoxButtons.OKCancel) == DialogResult.OK)
                { Result = new Settings(); pg.SelectedObject = Result; }
            };
            b.Controls.Add(ok); b.Controls.Add(cancel); b.Controls.Add(def);
            Controls.Add(pg); Controls.Add(b);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
