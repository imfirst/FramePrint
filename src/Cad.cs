﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Publishing;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FramePrint
{
    /// <summary>Найденная рамка (строка таблицы печати).</summary>
    public class FrameRow
    {
        public bool Print = true;
        public string LayoutName = "Model";
        public bool IsModel = true;
        public int LayoutOrder;
        public double MinX, MinY, MaxX, MaxY;
        public double Scale = 1;
        public ObjectId FrameId;
        public TemplateRow Fmt;
        public int Copies = 1;
        public bool IsSro;          // служебная страница допуска СРО
        public ObjectId SroRefId;

        public string Number = "", Total = "", Name = "", Cipher = "";
        public TextItem NumberText, TotalText;
        public bool HasStamp { get { return Number.Length > 0 || Name.Length > 0 || NumberText != null; } }
        public string StampKind { get { return IsSro ? "СРО" : (HasStamp ? "Форма 3" : "Титул"); } }
        public double W { get { return MaxX - MinX; } }
        public double H { get { return MaxY - MinY; } }
    }

    /// <summary>Текст на чертеже с его габаритом в МСК.</summary>
    public class TextItem
    {
        public string Text = "";
        public string Tag;            // для атрибутов
        public double MinX, MinY, MaxX, MaxY;
        public ObjectId Id;           // ObjectId.Null, если текст вложен в блок и не редактируется
        public double CX { get { return (MinX + MaxX) / 2; } }
        public double CY { get { return (MinY + MaxY) / 2; } }

        public double DistTo(double px, double py)
        {
            double dx = Math.Max(Math.Max(MinX - px, 0), px - MaxX);
            double dy = Math.Max(Math.Max(MinY - py, 0), py - MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public static class Cad
    {
        public const string Version = "1.4.0";
        public static Document Doc { get { return AcApp.DocumentManager.MdiActiveDocument; } }

        // =====================================================================
        //  ПОИСК РАМОК
        // =====================================================================
        public static List<FrameRow> FindFrames(Settings st, PrintTemplate tpl, ICollection<ObjectId> only)
        {
            List<FrameRow> res = new List<FrameRow>();
            Document doc = Doc;
            Database db = doc.Database;
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string l in (st.Layers ?? "").Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                layers.Add(l.Trim());
            HashSet<ObjectId> onlySet = only == null ? null : new HashSet<ObjectId>(only);
            List<double> scales = st.ScaleList();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary lays = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                List<Layout> layoutList = new List<Layout>();
                foreach (DBDictionaryEntry e in lays)
                    layoutList.Add((Layout)tr.GetObject(e.Value, OpenMode.ForRead));
                layoutList.Sort(delegate (Layout a, Layout b) { return a.TabOrder.CompareTo(b.TabOrder); });

                foreach (Layout lay in layoutList)
                {
                    bool isModel = lay.ModelType;
                    if (isModel && !st.SearchModel) continue;
                    if (!isModel && !st.SearchLayouts) continue;
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                    List<FrameRow> found = new List<FrameRow>();

                    foreach (ObjectId id in btr)
                    {
                        if (onlySet != null && !onlySet.Contains(id)) continue;
                        if (id.ObjectClass.DxfName != "LWPOLYLINE" && id.ObjectClass.DxfName != "POLYLINE") continue;
                        Curve c = tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (c == null) continue;
                        if (layers.Count > 0 && !layers.Contains(c.Layer)) continue;
                        if (st.ClosedOnly && !c.Closed) continue;
                        Extents3d ext;
                        try { ext = c.GeometricExtents; } catch { continue; }
                        double w = ext.MaxPoint.X - ext.MinPoint.X, h = ext.MaxPoint.Y - ext.MinPoint.Y;
                        if (w <= 0 || h <= 0) continue;

                        TemplateRow best = null; double bestErr = double.MaxValue, bestScale = 1;
                        List<double> sc = isModel ? scales : new List<double>(new double[] { 1.0 });
                        foreach (double s in sc)
                        {
                            foreach (TemplateRow r in tpl.Rows)
                            {
                                double err = Math.Max(Math.Abs(w / s - r.Width), Math.Abs(h / s - r.Height));
                                if (err <= st.Tolerance && err < bestErr) { best = r; bestErr = err; bestScale = s; }
                            }
                            if (best != null) break;
                        }
                        if (best == null) continue;

                        FrameRow fr = new FrameRow();
                        fr.FrameId = id; fr.Fmt = best; fr.Scale = bestScale;
                        fr.IsModel = isModel; fr.LayoutName = lay.LayoutName; fr.LayoutOrder = lay.TabOrder;
                        fr.MinX = ext.MinPoint.X; fr.MinY = ext.MinPoint.Y; fr.MaxX = ext.MaxPoint.X; fr.MaxY = ext.MaxPoint.Y;
                        if (st.IgnoreDuplicates && IsDuplicate(found, fr)) continue;
                        found.Add(fr);
                    }

                    if (found.Count > 0)
                    {
                        List<TextItem> texts = new List<TextItem>();
                        CollectTexts(tr, btr, Matrix3d.Identity, true, 0, texts);
                        foreach (FrameRow fr in found) ReadStamp(st, fr, texts);
                        Sort(st, found);
                        res.AddRange(found);
                    }
                }
                tr.Commit();
            }
            return res;
        }

        static bool IsDuplicate(List<FrameRow> list, FrameRow f)
        {
            double eps = 0.5 * f.Scale;
            foreach (FrameRow x in list)
                if (Math.Abs(x.MinX - f.MinX) < eps && Math.Abs(x.MinY - f.MinY) < eps &&
                    Math.Abs(x.MaxX - f.MaxX) < eps && Math.Abs(x.MaxY - f.MaxY) < eps) return true;
            return false;
        }

        public static void Sort(Settings st, List<FrameRow> list)
        {
            if (st.Sort == SortMode.None) return;
            if (st.Sort == SortMode.SheetNumber)
            {
                list.Sort(delegate (FrameRow a, FrameRow b)
                {
                    double na, nb;
                    bool pa = double.TryParse(a.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out na);
                    bool pb = double.TryParse(b.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out nb);
                    if (!pa) na = -1; if (!pb) nb = -1;
                    return na.CompareTo(nb);
                });
                return;
            }
            bool yx = st.Sort == SortMode.YX;
            // 1) первичный ключ: низ рамки (Y, сверху вниз) или левый край (X)
            list.Sort(delegate (FrameRow a, FrameRow b)
            {
                return yx ? b.MinY.CompareTo(a.MinY) : a.MinX.CompareTo(b.MinX);
            });
            // 2) группировка с допуском, внутри группы — вторичный ключ
            List<List<FrameRow>> groups = new List<List<FrameRow>>();
            foreach (FrameRow f in list)
            {
                double tol = Math.Max(st.SortTolerance * f.Scale, 1e-3 * f.Scale + 0.01);
                double key = yx ? f.MinY : f.MinX;
                List<FrameRow> g = groups.Count > 0 ? groups[groups.Count - 1] : null;
                double gk = g == null ? 0 : (yx ? g[0].MinY : g[0].MinX);
                if (g != null && Math.Abs(gk - key) <= tol) g.Add(f);
                else { g = new List<FrameRow>(); g.Add(f); groups.Add(g); }
            }
            list.Clear();
            foreach (List<FrameRow> g in groups)
            {
                g.Sort(delegate (FrameRow a, FrameRow b)
                {
                    return yx ? a.MinX.CompareTo(b.MinX) : b.MaxY.CompareTo(a.MaxY);
                });
                list.AddRange(g);
            }
        }

        // =====================================================================
        //  ТЕКСТЫ И ШТАМП
        // =====================================================================
        static void CollectTexts(Transaction tr, BlockTableRecord btr, Matrix3d xf, bool top, int depth, List<TextItem> list)
        {
            foreach (ObjectId id in btr)
            {
                string dxf = id.ObjectClass.DxfName;
                if (dxf != "TEXT" && dxf != "MTEXT" && dxf != "INSERT") continue;
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;

                DBText t = ent as DBText;
                if (t != null && !(t is AttributeDefinition))
                {
                    Add(list, t.TextString, null, ent, xf, top ? id : ObjectId.Null);
                    continue;
                }
                MText m = ent as MText;
                if (m != null)
                {
                    Add(list, m.Text, null, ent, xf, top ? id : ObjectId.Null);
                    continue;
                }
                BlockReference br = ent as BlockReference;
                if (br != null && depth < 4)
                {
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        AttributeReference ar = tr.GetObject(aid, OpenMode.ForRead) as AttributeReference;
                        if (ar == null || ar.Invisible) continue;
                        string s = ar.IsMTextAttribute ? ar.MTextAttribute.Text : ar.TextString;
                        Add(list, s, ar.Tag, ar, xf, top ? aid : ObjectId.Null);
                    }
                    BlockTableRecord def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (def != null && !def.IsFromExternalReference && !def.IsLayout)
                        CollectTexts(tr, def, xf * br.BlockTransform, false, depth + 1, list);
                }
            }
        }

        static void Add(List<TextItem> list, string text, string tag, Entity ent, Matrix3d xf, ObjectId id)
        {
            if (text == null) return;
            text = text.Trim();
            Extents3d ext;
            try { ext = ent.GeometricExtents; } catch { return; }
            if (!xf.IsEqualTo(Matrix3d.Identity)) ext.TransformBy(xf);
            TextItem ti = new TextItem();
            ti.Text = text; ti.Tag = tag; ti.Id = id;
            ti.MinX = ext.MinPoint.X; ti.MinY = ext.MinPoint.Y; ti.MaxX = ext.MaxPoint.X; ti.MaxY = ext.MaxPoint.Y;
            list.Add(ti);
        }

        static void StampXY(FrameRow f, StampPoint p, out double x, out double y)
        {
            x = f.MaxX - p.X * f.Scale;
            y = f.MinY + p.Y * f.Scale;
        }

        static TextItem Nearest(List<TextItem> texts, FrameRow f, StampPoint p)
        {
            if (p.IsEmpty) return null;
            double x, y; StampXY(f, p, out x, out y);
            double r = p.R * f.Scale;
            TextItem best = null; double bd = double.MaxValue;
            foreach (TextItem t in texts)
            {
                if (t.Text.Length == 0) continue;
                double d = t.DistTo(x, y);
                if (d <= r && d < bd) { bd = d; best = t; }
            }
            return best;
        }

        static string Collect(List<TextItem> texts, FrameRow f, StampPoint p)
        {
            if (p.IsEmpty) return "";
            double x, y; StampXY(f, p, out x, out y);
            double r = p.R * f.Scale;
            List<TextItem> hits = new List<TextItem>();
            foreach (TextItem t in texts)
                if (t.Text.Length > 0 && t.DistTo(x, y) <= r) hits.Add(t);
            hits.Sort(delegate (TextItem a, TextItem b)
            {
                int c = b.CY.CompareTo(a.CY);
                return Math.Abs(a.CY - b.CY) < 1e-6 ? a.MinX.CompareTo(b.MinX) : c;
            });
            StringBuilder sb = new StringBuilder();
            foreach (TextItem t in hits) { if (sb.Length > 0) sb.Append(' '); sb.Append(t.Text.Replace("\r", " ").Replace("\n", " ")); }
            return sb.ToString().Trim();
        }

        static void ReadStamp(Settings st, FrameRow f, List<TextItem> all)
        {
            // только тексты внутри рамки
            List<TextItem> texts = new List<TextItem>();
            foreach (TextItem t in all)
                if (t.CX >= f.MinX && t.CX <= f.MaxX && t.CY >= f.MinY && t.CY <= f.MaxY) texts.Add(t);

            // 1) атрибуты блоков (приоритет)
            if (st.UseAttributes)
            {
                foreach (TextItem t in texts)
                {
                    if (t.Tag == null) continue;
                    if (Eq(t.Tag, st.AttrNumber)) { f.Number = t.Text; f.NumberText = t; }
                    else if (Eq(t.Tag, st.AttrTotal)) { f.Total = t.Text; f.TotalText = t; }
                    else if (Eq(t.Tag, st.AttrName)) f.Name = t.Text;
                    else if (Eq(t.Tag, st.AttrCipher)) f.Cipher = t.Text;
                }
            }
            // 2) тексты по точкам штампа
            if (f.NumberText == null)
            {
                TextItem n = Nearest(texts, f, StampPoint.Parse(st.PtNumber));
                if (n != null) { f.Number = n.Text; f.NumberText = n; }
            }
            if (f.TotalText == null)
            {
                TextItem n = Nearest(texts, f, StampPoint.Parse(st.PtTotal));
                if (n != null) { f.Total = n.Text; f.TotalText = n; }
            }
            if (f.Name.Length == 0) f.Name = Collect(texts, f, StampPoint.Parse(st.PtName));
            if (f.Cipher.Length == 0) f.Cipher = Collect(texts, f, StampPoint.Parse(st.PtCipher));
        }

        static bool Eq(string a, string b)
        {
            return !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        //  НУМЕРАЦИЯ
        // =====================================================================
        public static string Numerate(Settings st, List<FrameRow> rows)
        {
            Document doc = Doc; Database db = doc.Database;
            List<FrameRow> target = new List<FrameRow>();
            foreach (FrameRow f in rows)
            {
                if (f.IsSro) continue;
                if (st.SkipTitle && !f.HasStamp) continue;
                target.Add(f);
            }
            if (target.Count == 0) return "Нет листов со штампом для нумерации.";
            int n = st.StartNumber, written = 0, skipped = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < target.Count; i++)
                {
                    FrameRow f = target[i];
                    string num = n.ToString(CultureInfo.InvariantCulture);
                    if (WriteValue(tr, db, st, f, f.NumberText, StampPoint.Parse(st.PtNumber), num)) { f.Number = num; written++; }
                    else skipped++;
                    if (i == 0 && st.WriteTotalOnFirst)
                    {
                        string tot = target.Count.ToString(CultureInfo.InvariantCulture);
                        if (WriteValue(tr, db, st, f, f.TotalText, StampPoint.Parse(st.PtTotal), tot)) f.Total = tot;
                    }
                    n++;
                }
                tr.Commit();
            }
            doc.Editor.Regen();
            string msg = "Пронумеровано листов: " + written;
            if (skipped > 0) msg += "\nНе удалось записать (текст внутри блока без атрибута): " + skipped;
            return msg;
        }

        static bool WriteValue(Transaction tr, Database db, Settings st, FrameRow f, TextItem ti, StampPoint p, string value)
        {
            if (ti != null)
            {
                if (ti.Id.IsNull) return false;
                DBObject o = tr.GetObject(ti.Id, OpenMode.ForWrite);
                AttributeReference ar = o as AttributeReference;
                if (ar != null) { ar.TextString = value; ti.Text = value; return true; }
                DBText t = o as DBText;
                if (t != null) { t.TextString = value; ti.Text = value; return true; }
                MText m = o as MText;
                if (m != null) { m.Contents = value; ti.Text = value; return true; }
                return false;
            }
            if (!st.CreateMissingText || p.IsEmpty) return false;
            double x, y; StampXY(f, p, out x, out y);
            BlockTableRecord space = SpaceOf(tr, db, f);
            DBText nt = new DBText();
            nt.SetDatabaseDefaults(db);
            nt.Height = st.NumberTextHeight * f.Scale;
            nt.TextString = value;
            nt.HorizontalMode = TextHorizontalMode.TextCenter;
            nt.VerticalMode = TextVerticalMode.TextVerticalMid;
            nt.Position = new Point3d(x, y, 0);
            nt.AlignmentPoint = new Point3d(x, y, 0);
            ObjectId id = space.AppendEntity(nt);
            tr.AddNewlyCreatedDBObject(nt, true);
            nt.AdjustAlignment(db);
            TextItem created = new TextItem();
            created.Id = id; created.Text = value;
            created.MinX = created.MaxX = x; created.MinY = created.MaxY = y;
            if (p.Equals(StampPoint.Parse(st.PtNumber))) f.NumberText = created; else f.TotalText = created;
            return true;
        }

        static BlockTableRecord SpaceOf(Transaction tr, Database db, FrameRow f)
        {
            Entity e = (Entity)tr.GetObject(f.FrameId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(e.OwnerId, OpenMode.ForWrite);
        }

        // =====================================================================
        //  ВЕДОМОСТЬ
        // =====================================================================
        public static void InsertSheetList(Settings st, List<FrameRow> rows, Point3d pt, double scale)
        {
            Document doc = Doc; Database db = doc.Database;
            List<FrameRow> data = new List<FrameRow>();
            foreach (FrameRow f in rows)
            {
                if (f.IsSro) continue;
                if (st.VedSkipNoStamp && !f.HasStamp) continue;
                data.Add(f);
            }
            double[] w = Settings.Nums(st.VedWidths, 15, 140, 30);
            double[] h = Settings.Nums(st.VedHeights, 12, 15, 8);
            double[] th = Settings.Nums(st.VedTextHeights, 5, 3.5, 3.5);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Table t = new Table();
                t.SetDatabaseDefaults(db);
                t.TableStyle = db.Tablestyle;
                t.Position = pt;
                t.SetSize(data.Count + 2, 3);
                for (int c = 0; c < 3; c++) t.Columns[c].Width = w[c] * scale;
                t.Rows[0].Height = h[0] * scale;
                t.Rows[1].Height = h[1] * scale;
                for (int r = 2; r < data.Count + 2; r++) t.Rows[r].Height = h[2] * scale;

                t.MergeCells(CellRange.Create(t, 0, 0, 0, 2));
                SetCell(t, 0, 0, st.VedTitle, th[0] * scale, CellAlignment.MiddleCenter);
                SetCell(t, 1, 0, st.VedCol1, th[1] * scale, CellAlignment.MiddleCenter);
                SetCell(t, 1, 1, st.VedCol2, th[1] * scale, CellAlignment.MiddleCenter);
                SetCell(t, 1, 2, st.VedCol3, th[1] * scale, CellAlignment.MiddleCenter);
                for (int i = 0; i < data.Count; i++)
                {
                    SetCell(t, i + 2, 0, data[i].Number, th[2] * scale, CellAlignment.MiddleCenter);
                    SetCell(t, i + 2, 1, data[i].Name, th[2] * scale, CellAlignment.MiddleLeft);
                    SetCell(t, i + 2, 2, "", th[2] * scale, CellAlignment.MiddleCenter);
                }
                t.GenerateLayout();
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                space.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                tr.Commit();
            }
        }

        static void SetCell(Table t, int r, int c, string text, double height, CellAlignment al)
        {
            Cell cell = t.Cells[r, c];
            cell.TextString = text ?? "";
            cell.TextHeight = height;
            cell.Alignment = al;
        }

        public static void ExportCsv(List<FrameRow> rows, string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("№;Вкладка;Формат;Лист;Листов;Наименование;Шифр;Штамп");
            int i = 1;
            foreach (FrameRow f in rows)
            {
                sb.AppendLine(string.Join(";", new string[] {
                    (i++).ToString(), f.LayoutName, f.Fmt == null ? "" : f.Fmt.Name, f.Number, f.Total,
                    "\"" + f.Name.Replace("\"", "\"\"") + "\"", f.Cipher, f.StampKind }));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // =====================================================================
        //  ПОКАЗАТЬ РАМКУ
        // =====================================================================
        public static void ZoomTo(FrameRow f)
        {
            Document doc = Doc;
            using (doc.LockDocument())
            {
                if (!f.IsModel && LayoutManager.Current.CurrentLayout != f.LayoutName)
                    LayoutManager.Current.CurrentLayout = f.LayoutName;
                else if (f.IsModel && LayoutManager.Current.CurrentLayout != "Model")
                    LayoutManager.Current.CurrentLayout = "Model";
                using (ViewTableRecord v = doc.Editor.GetCurrentView())
                {
                    double m = 1.1;
                    v.CenterPoint = new Point2d((f.MinX + f.MaxX) / 2, (f.MinY + f.MaxY) / 2);
                    v.Height = f.H * m;
                    v.Width = f.W * m;
                    v.ViewDirection = Vector3d.ZAxis;
                    v.Target = Point3d.Origin;
                    v.ViewTwist = 0;
                    doc.Editor.SetCurrentView(v);
                }
            }
        }

        // =====================================================================
        //  ПРИНТЕРЫ, ЛИСТЫ, СТИЛИ
        // =====================================================================
        static Dictionary<string, List<string[]>> _mediaCache = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        public static List<string> Devices()
        {
            List<string> res = new List<string>();
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            using (PlotSettings ps = new PlotSettings(true)) { psv.RefreshLists(ps); }
            foreach (string s in psv.GetPlotDeviceList()) res.Add(s);
            return res;
        }

        public static List<string> Styles()
        {
            List<string> res = new List<string>();
            foreach (string s in PlotSettingsValidator.Current.GetPlotStyleSheetList()) res.Add(s);
            res.Sort(StringComparer.OrdinalIgnoreCase);
            return res;
        }

        /// <summary>Список листов принтера: пары {каноническое имя, отображаемое имя}.</summary>
        public static List<string[]> Media(string device)
        {
            List<string[]> res;
            if (string.IsNullOrEmpty(device)) return new List<string[]>();
            if (_mediaCache.TryGetValue(device, out res)) return res;
            res = new List<string[]>();
            try
            {
                PlotSettingsValidator psv = PlotSettingsValidator.Current;
                using (PlotSettings ps = new PlotSettings(true))
                {
                    psv.SetPlotConfigurationName(ps, device, null);
                    psv.RefreshLists(ps);
                    foreach (string can in psv.GetCanonicalMediaNameList(ps))
                        res.Add(new string[] { can, psv.GetLocaleMediaName(ps, can) });
                }
            }
            catch { }
            _mediaCache[device] = res;
            return res;
        }

        public static void ClearCache() { _mediaCache.Clear(); }

        static string Canonical(string device, string media)
        {
            if (string.IsNullOrEmpty(media)) return null;
            foreach (string[] m in Media(device))
                if (m[1] == media || m[0] == media) return m[0];
            string alt = media.Replace(' ', '_');
            foreach (string[] m in Media(device))
                if (string.Equals(m[0], alt, StringComparison.OrdinalIgnoreCase)) return m[0];
            return null;
        }

        static readonly Regex SizeRx = new Regex(@"\(([\d\.]+)\s*x\s*([\d\.]+)\s*(MM|mm|Inches|inches|мм)?\)", RegexOptions.Compiled);

        /// <summary>Найти лист A4 нужной ориентации у плоттера (для страниц СРО).</summary>
        static string FindMedia(string device, double wmm, double hmm)
        {
            string fallback = null;
            foreach (string[] m in Media(device))
            {
                Match mt = SizeRx.Match(m[1]);
                if (!mt.Success) continue;
                double a = double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture);
                double b = double.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture);
                if (Math.Abs(a - wmm) < 1 && Math.Abs(b - hmm) < 1)
                {
                    if (m[1].IndexOf("full bleed", StringComparison.OrdinalIgnoreCase) >= 0) return m[0];
                    if (fallback == null) fallback = m[0];
                }
            }
            return fallback;
        }

        // =====================================================================
        //  ПЕЧАТЬ / ПУБЛИКАЦИЯ
        // =====================================================================
        const string SetupPrefix = "FP-";

        public static string Publish(Settings st, List<FrameRow> allRows, bool toPdf, string templateName)
        {
            Document doc = Doc;
            Database db = doc.Database;
            if (!File.Exists(doc.Name))
                return "Сначала сохраните чертёж: публикация работает с файлом .dwg.";

            List<FrameRow> rows = new List<FrameRow>();
            List<string> problems = new List<string>();
            foreach (FrameRow f in allRows)
            {
                if (!f.Print) continue;
                if (f.Fmt == null || string.IsNullOrEmpty(f.Fmt.Device) || string.IsNullOrEmpty(f.Fmt.Media))
                {
                    problems.Add((f.Fmt == null ? "?" : f.Fmt.Name) + " — в шаблоне не задан принтер или лист");
                    continue;
                }
                rows.Add(f);
            }
            if (rows.Count == 0)
                return "Нечего печатать." + (problems.Count > 0 ? "\n" + string.Join("\n", problems.ToArray()) : "");

            string pdfPath = null;
            if (toPdf)
            {
                pdfPath = OutputPath(st, doc.Name, rows, templateName);
                if (pdfPath == null) return "Не удалось определить папку для PDF.";
                if (File.Exists(pdfPath) && !CanWrite(pdfPath))
                    return "Файл занят (открыт в просмотрщике?):\n" + pdfPath;
            }

            // --- допуск СРО ---
            List<FrameRow> sro = new List<FrameRow>();
            string sroNote = null;
            string sroDirect = null;      // PDF допуска, вставляемый в итоговый файл как есть
            int sroDirectPages = 0;
            if (toPdf && st.SroEnabled && string.IsNullOrEmpty(st.SroPath))
                sroNote = "Допуск СРО не вставлен: файл не выбран (кнопка «Выбрать PDF…» внизу окна).";
            if (toPdf && !st.SroEnabled)
                sroNote = "Допуск СРО выключен (галочка внизу окна).";
            if (toPdf && st.SroEnabled && !string.IsNullOrEmpty(st.SroPath))
            {
                if (!File.Exists(st.SroPath)) sroNote = "Файл допуска СРО не найден: " + st.SroPath;
                else
                {
                    string why = null;
                    if (st.SroAsIs && st.MultiPage && PdfMerge.CanMerge(st.SroPath, out why))
                    {
                        sroDirect = st.SroPath;
                        sroDirectPages = PdfMerge.PageCount(st.SroPath);
                    }
                    else
                    {
                        if (st.SroAsIs && why != null) sroNote = "Допуск СРО вставлен картинкой (" + why + ").";
                        try { sro = AddSroPages(st, db, rows); }
                        catch (System.Exception ex) { sroNote = "Допуск СРО не вставлен: " + ex.Message; }
                    }
                }
            }
            List<FrameRow> pages = new List<FrameRow>();
            int after = Math.Max(0, Math.Min(st.SroAfterPage, rows.Count));
            for (int i = 0; i < rows.Count; i++)
            {
                if (i == after) pages.AddRange(sro);
                pages.Add(rows[i]);
            }
            if (after >= rows.Count) pages.AddRange(sro);

            try { doc.Editor.UpdateTiledViewportsInDatabase(); } catch { }
            object bgPlot = AcApp.GetSystemVariable("BACKGROUNDPLOT");
            object pdfFrame = null;
            try { pdfFrame = AcApp.GetSystemVariable("PDFFRAME"); } catch { }
            List<string> setupNames = new List<string>();
            try
            {
                AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
                if (pdfFrame != null) AcApp.SetSystemVariable("PDFFRAME", (short)0);

                if (st.Output != OutputMethod.Publish || true)
                {
                    PlotEngineRun(doc, st, pages, toPdf, pdfPath, sroDirect, Math.Max(0, Math.Min(st.SroAfterPage, rows.Count)));
                }
                else
                {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DeleteSetups(tr, db);
                    for (int i = 0; i < pages.Count; i++)
                    {
                        string name = SetupPrefix + (i + 1) + "-" + pages[i].LayoutName;
                        CreateSetup(tr, db, st, pages[i], name);
                        setupNames.Add(name);
                    }
                    tr.Commit();
                }

                string tmpDir = Path.Combine(Path.GetTempPath(), "FramePrint");
                Directory.CreateDirectory(tmpDir);
                string dsdPath = Path.Combine(tmpDir, "publish.dsd");
                string baseName = Path.GetFileNameWithoutExtension(doc.Name);

                using (DsdEntryCollection col = new DsdEntryCollection())
                {
                    for (int i = 0; i < pages.Count; i++)
                    {
                        int copies = toPdf ? 1 : Math.Max(1, pages[i].Copies);
                        for (int c = 0; c < copies; c++)
                        {
                            DsdEntry e = new DsdEntry();
                            e.DwgName = doc.Name;
                            e.Layout = pages[i].LayoutName;
                            e.Title = toPdf && !st.MultiPage
                                ? Path.GetFileNameWithoutExtension(pdfPath) + "-" + (i + 1).ToString("00")
                                : baseName + " " + pages[i].LayoutName + " " + (i + 1) + (c > 0 ? "-" + (c + 1) : "");
                            e.Nps = setupNames[i];
                            col.Add(e);
                        }
                    }

                    using (DsdData d = new DsdData())
                    {
                        d.SetDsdEntryCollection(col);
                        d.IsSheetSet = false;
                        d.IsHomogeneous = false;
                        d.NoOfCopies = toPdf ? 1 : Math.Max(1, st.Copies);
                        d.PromptForDwfName = false;
                        d.SheetSetName = "FramePrint";
                        d.LogFilePath = Path.Combine(tmpDir, "publish.log");
                        if (toPdf)
                        {
                            d.SheetType = st.MultiPage ? SheetType.MultiPdf : SheetType.SinglePdf;
                            d.DestinationName = pdfPath;
                            d.ProjectPath = Path.GetDirectoryName(pdfPath);
                        }
                        else
                        {
                            d.SheetType = SheetType.OriginalDevice;
                            d.ProjectPath = tmpDir;
                        }
                        if (File.Exists(dsdPath)) File.Delete(dsdPath);
                        d.WriteDsd(dsdPath);
                    }
                }
                PatchDsd(dsdPath, st, toPdf);

                using (DsdData d2 = new DsdData())
                {
                    d2.ReadDsd(dsdPath);
                    string dev = toPdf ? st.PdfDevice : rows[0].Fmt.Device;
                    PlotConfig pc = PlotConfigManager.SetCurrentConfig(dev);
                    AcApp.Publisher.PublishExecute(d2, pc);
                }
                }
            }
            finally
            {
                try { AcApp.SetSystemVariable("BACKGROUNDPLOT", bgPlot); } catch { }
                if (pdfFrame != null) { try { AcApp.SetSystemVariable("PDFFRAME", pdfFrame); } catch { } }
                try
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        RemoveSro(tr, db, sro);
                        if (!st.KeepSetups) DeleteSetups(tr, db);
                        tr.Commit();
                    }
                }
                catch { }
            }

            StringBuilder msg = new StringBuilder();
            if (toPdf)
            {
                if (st.MultiPage && File.Exists(pdfPath))
                {
                    int sroN = sro.Count + sroDirectPages;
                    msg.Append("PDF создан: " + pdfPath + "\nСтраниц: " + (pages.Count + sroDirectPages) +
                        (sroN > 0 ? " (из них допуск СРО: " + sroN + (sroDirect != null ? ", вставлен как есть" : ", картинкой") + ")" : ""));
                    if (st.OpenAfter) { try { System.Diagnostics.Process.Start(pdfPath); } catch { } }
                }
                else if (st.MultiPage) msg.Append("Публикация завершена, но файл не найден: " + pdfPath + "\nСм. журнал AutoCAD.");
                else msg.Append("PDF-файлы сохранены в папку: " + Path.GetDirectoryName(pdfPath));
            }
            else msg.Append("Отправлено на печать листов: " + rows.Count);
            if (sroNote != null) msg.Append("\n" + sroNote);
            if (problems.Count > 0) msg.Append("\nПропущено:\n" + string.Join("\n", problems.ToArray()));
            return msg.ToString();
        }

        static bool CanWrite(string path)
        {
            try { using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } return true; }
            catch { return false; }
        }

        /// <summary>«Шифр       О.СО» -> «О.СО».</summary>
        public static string SectionOf(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            string s = Regex.Replace(cipher, @"^\s*шифр\s*[:.]?\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        /// <summary>
        /// Раздел из шифра: берётся ПОСЛЕДНЕЕ в строке обозначение из разрешённого списка.
        /// «Шифр 2026.10.НК.О» -> «О», «О.СО» -> «О», «ВК.СО» -> «ВК». Не из списка — "".
        /// </summary>
        public static string MainSection(string cipher)
        {
            string s = SectionOf(cipher);
            if (s.Length == 0) return "";
            Dictionary<string, string> allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string a in (Settings.Current.Sections ?? "").Split(new char[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                allowed[ToCyr(a.Trim())] = a.Trim();
            MatchCollection words = Regex.Matches(ToCyr(s), @"\p{L}+");
            for (int i = words.Count - 1; i >= 0; i--)
            {
                string canon;
                if (allowed.TryGetValue(words[i].Value, out canon)) return canon;
            }
            return "";
        }

        /// <summary>Латинские буквы-двойники -> кириллица (O -> О, B -> В, ...).</summary>
        static string ToCyr(string s)
        {
            const string lat = "AaBCcEeHKkMOoPpTXxy";
            const string cyr = "АаВСсЕеНКкМОоРрТХху";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                int i = lat.IndexOf(ch);
                sb.Append(i >= 0 ? cyr[i] : ch);
            }
            return sb.ToString();
        }

        /// <summary>Раздел для имени файла: первый по порядку печати лист крупнее A4 с разделом из списка.</summary>
        public static string Sections(List<FrameRow> rows)
        {
            string fallback = "";
            foreach (FrameRow f in rows)
            {
                if (f.IsSro || !f.Print) continue;
                string sec = MainSection(f.Cipher);
                if (sec.Length == 0) continue;
                if (IsBig(f)) return sec;
                if (fallback.Length == 0) fallback = sec;   // только A4 — берём с них
            }
            return fallback;
        }

        /// <summary>Лист больше A4 (по размеру рамки на бумаге, с допуском 5 мм).</summary>
        static bool IsBig(FrameRow f)
        {
            double a = Math.Max(f.W, f.H) / f.Scale, b = Math.Min(f.W, f.H) / f.Scale;
            return a > 297 + 5 || b > 210 + 5;
        }

        /// <summary>«Щукинская 3_ВиК_О_ЭОМ_2026-10-21» -> объект «Щукинская 3», дата «2026-10-21».</summary>
        public static void SplitDwgName(string file, out string obj, out string date)
        {
            string[] parts = file.Split('_');
            obj = parts[0].Trim();
            date = "";
            if (parts.Length > 1 && Regex.IsMatch(parts[parts.Length - 1], @"^\d{2,4}[-.]\d{2}[-.]\d{2,4}$"))
                date = parts[parts.Length - 1];
        }

        public static string OutputPath(Settings st, string dwgPath, List<FrameRow> rows, string templateName)
        {
            string dir;
            switch (st.SaveTo)
            {
                case SaveMode.NextToDwg: dir = Path.GetDirectoryName(dwgPath); break;
                case SaveMode.SubFolder: dir = Path.Combine(Path.GetDirectoryName(dwgPath), string.IsNullOrEmpty(st.SubFolder) ? "PDF" : st.SubFolder); break;
                default: dir = string.IsNullOrEmpty(st.Folder) ? Path.GetDirectoryName(dwgPath) : st.Folder; break;
            }
            Directory.CreateDirectory(dir);
            string cipher = "";
            foreach (FrameRow f in rows) if (f.Cipher.Length > 0) { cipher = f.Cipher; break; }
            string section = Sections(rows);
            string file = Path.GetFileNameWithoutExtension(dwgPath);
            string pattern = string.IsNullOrEmpty(st.FileNamePattern) ? "{Файл}" : st.FileNamePattern;
            if (section.Length == 0 && pattern.Contains("{Раздел}")) pattern = "{Файл}";   // штамп не прочитан — имя как у чертежа
            string obj, fdate;
            SplitDwgName(file, out obj, out fdate);
            string name = pattern
                .Replace("{Файл}", file)
                .Replace("{Объект}", obj)
                .Replace("{ДатаФайла}", fdate)
                .Replace("{Раздел}", section)
                .Replace("{Шифр}", SectionOf(cipher))
                .Replace("{Шаблон}", templateName ?? "")
                .Replace("{Дата}", DateTime.Now.ToString(string.IsNullOrEmpty(st.DateFormat) ? "yyyy-MM-dd" : st.DateFormat));
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = Regex.Replace(name, "_{2,}", "_").Trim('_', ' ', '-');
            name = name.Trim();
            if (name.Length == 0) name = "drawing";
            string path = Path.Combine(dir, name + ".pdf");
            if (!st.Overwrite)
            {
                int k = 2;
                while (File.Exists(path)) path = Path.Combine(dir, name + " (" + (k++) + ").pdf");
            }
            return path;
        }

        static void DeleteSetups(Transaction tr, Database db)
        {
            DBDictionary dict = (DBDictionary)tr.GetObject(db.PlotSettingsDictionaryId, OpenMode.ForRead);
            List<ObjectId> del = new List<ObjectId>();
            foreach (DBDictionaryEntry e in dict)
                if (e.Key.StartsWith(SetupPrefix, StringComparison.Ordinal)) del.Add(e.Value);
            foreach (ObjectId id in del)
                tr.GetObject(id, OpenMode.ForWrite).Erase();
        }

        static PlotRotation Rot(int deg)
        {
            deg = ((deg % 360) + 360) % 360;
            if (deg == 90) return PlotRotation.Degrees090;
            if (deg == 180) return PlotRotation.Degrees180;
            if (deg == 270) return PlotRotation.Degrees270;
            return PlotRotation.Degrees000;
        }

        /// <summary>Матрица МСК -> DCS активного видового экрана Модели.</summary>
        public static Matrix3d WorldToModelDcs(Transaction tr, Database db)
        {
            ViewportTable vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
            ViewportTableRecord vp = null;
            foreach (ObjectId id in vt)
            {
                ViewportTableRecord r = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (r.Name == "*Active") { vp = r; break; }
            }
            if (vp == null) return Matrix3d.Identity;
            Matrix3d dcsToWcs =
                Matrix3d.Rotation(-vp.ViewTwist, vp.ViewDirection, vp.Target) *
                Matrix3d.Displacement(vp.Target - Point3d.Origin) *
                Matrix3d.PlaneToWorld(vp.ViewDirection);
            return dcsToWcs.Inverse();
        }

        /// <summary>Окно печати рамки в DCS (в Листах DCS = координаты листа).</summary>
        public static Extents2d PlotWindow(Transaction tr, Database db, FrameRow f)
        {
            if (!f.IsModel) return new Extents2d(f.MinX, f.MinY, f.MaxX, f.MaxY);
            Matrix3d m = WorldToModelDcs(tr, db);
            Point3d[] pts = {
                new Point3d(f.MinX, f.MinY, 0), new Point3d(f.MaxX, f.MinY, 0),
                new Point3d(f.MaxX, f.MaxY, 0), new Point3d(f.MinX, f.MaxY, 0) };
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (Point3d p in pts)
            {
                Point3d q = p.TransformBy(m);
                x0 = Math.Min(x0, q.X); y0 = Math.Min(y0, q.Y);
                x1 = Math.Max(x1, q.X); y1 = Math.Max(y1, q.Y);
            }
            return new Extents2d(x0, y0, x1, y1);
        }

        /// <summary>Диагностика для команды FPDIAG.</summary>
        public static string Diagnose(Settings st, List<FrameRow> rows)
        {
            Document doc = Doc; Database db = doc.Database;
            StringBuilder sb = new StringBuilder();
            try { doc.Editor.UpdateTiledViewportsInDatabase(); } catch { }
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ViewportTable vt = (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead);
                foreach (ObjectId id in vt)
                {
                    ViewportTableRecord r = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (r.Name != "*Active") continue;
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\n*Active: target={0} dir={1} twist={2:0.###} center={3}",
                        r.Target, r.ViewDirection, r.ViewTwist, r.CenterPoint);
                    break;
                }
                sb.Append("\nWORLDUCS=" + AcApp.GetSystemVariable("WORLDUCS") + " INSUNITS=" + db.Insunits + " файл=" + doc.Name);
                int n = 0;
                foreach (FrameRow f in rows)
                {
                    if (n++ >= 3) break;
                    Extents2d w = PlotWindow(tr, db, f);
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        "\nРамка {0} [{1}] масштаб {2}: МСК ({3:0.##},{4:0.##})-({5:0.##},{6:0.##})  окно DCS ({7:0.##},{8:0.##})-({9:0.##},{10:0.##})",
                        n, f.Fmt == null ? "?" : f.Fmt.Name, f.Scale, f.MinX, f.MinY, f.MaxX, f.MaxY,
                        w.MinPoint.X, w.MinPoint.Y, w.MaxPoint.X, w.MaxPoint.Y);
                }
                tr.Commit();
            }
            return sb.ToString();
        }

        static void CreateSetup(Transaction tr, Database db, Settings st, FrameRow f, string name)
        {
            PlotSettings ps = BuildSettings(tr, db, st, f, name);
            ps.AddToPlotSettingsDictionary(db);
            tr.AddNewlyCreatedDBObject(ps, true);
        }

        /// <summary>Параметры печати одной рамки (объект не добавлен в чертёж).</summary>
        static PlotSettings BuildSettings(Transaction tr, Database db, Settings st, FrameRow f, string name)
        {
            PlotSettingsValidator psv = PlotSettingsValidator.Current;
            ObjectId layId = LayoutManager.Current.GetLayoutId(f.LayoutName);
            Layout lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);

            PlotSettings ps = new PlotSettings(lay.ModelType);
            ps.CopyFrom(lay);
            ps.PlotSettingsName = name;

            string media = Canonical(f.Fmt.Device, f.Fmt.Media);
            if (media == null)
                throw new InvalidOperationException("У принтера «" + f.Fmt.Device + "» нет листа «" + f.Fmt.Media + "»");
            psv.SetPlotConfigurationName(ps, f.Fmt.Device, media);
            psv.RefreshLists(ps);
            psv.SetPlotPaperUnits(ps, PlotPaperUnit.Millimeters);

            Extents2d win = PlotWindow(tr, db, f);
            psv.SetPlotWindowArea(ps, win);
            psv.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);

            if (f.Fmt.Fit)
            {
                psv.SetUseStandardScale(ps, true);
                psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
            }
            else
            {
                psv.SetUseStandardScale(ps, false);
                psv.SetCustomPrintScale(ps, new CustomScale(1, f.Scale));
            }
            psv.SetPlotCentered(ps, true);

            // ориентация: окно и лист должны совпадать, плюс «переворот» из шаблона
            Point2d paper = ps.PlotPaperSize;
            bool paperLand = paper.X > paper.Y;
            bool winLand = f.W > f.H;
            int deg = (paperLand == winLand) ? 0 : 90;
            if (st.InvertAutoOrientation) deg = 90 - deg;
            deg += f.Fmt.Flip;
            psv.SetPlotRotation(ps, Rot(deg));

            if (!string.IsNullOrEmpty(f.Fmt.Style))
            {
                try { psv.SetCurrentStyleSheet(ps, f.Fmt.Style); } catch { }
                ps.PlotPlotStyles = true;
            }
            ps.PrintLineweights = st.PlotLineweights;
            return ps;
        }

        static Layout LayoutOf(Transaction tr, FrameRow f)
        {
            return (Layout)tr.GetObject(LayoutManager.Current.GetLayoutId(f.LayoutName), OpenMode.ForRead);
        }

        /// <summary>
        /// Печать движком AutoCAD прямо из открытого чертежа (без DSD и наборов в файле).
        /// PDF: один многолистовой документ или по файлу на лист. Принтер: документ на лист.
        /// </summary>
        static void PlotEngineRun(Document doc, Settings st, List<FrameRow> pages, bool toPdf, string pdfPath, string sroFile, int sroAfter)
        {
            Database db = doc.Database;
            string tmpDir = Path.Combine(Path.GetTempPath(), "FramePrint", "pages");
            Directory.CreateDirectory(tmpDir);
            foreach (string old in Directory.GetFiles(tmpDir, "*.pdf")) { try { File.Delete(old); } catch { } }

            List<string> parts = new List<string>();
            List<string> titles = new List<string>();
            int total = 0;
            foreach (FrameRow f in pages) total += toPdf ? 1 : Math.Max(1, f.Copies) * Math.Max(1, st.Copies);
            int done = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                FrameRow f = pages[i];
                int copies = toPdf ? 1 : Math.Max(1, f.Copies) * Math.Max(1, st.Copies);
                for (int c = 0; c < copies; c++)
                {
                    string file = toPdf ? Path.Combine(tmpDir, (i + 1).ToString("000") + ".pdf") : null;
                    bool cancelled = PlotOne(doc, db, st, f, file, done + 1, total);
                    done++;
                    if (cancelled) throw new OperationCanceledException("Печать отменена.");
                    if (file != null)
                    {
                        if (!File.Exists(file)) throw new InvalidOperationException("Лист " + (i + 1) + " не напечатался (файл не создан).");
                        parts.Add(file);
                        titles.Add(f.IsSro ? null : (f.Number + "- " + f.Name).Trim());
                    }
                }
            }
            if (!toPdf) return;

            string dir = Path.GetDirectoryName(pdfPath);
            string stem = Path.GetFileNameWithoutExtension(pdfPath);
            if (sroFile != null && st.MultiPage)
            {
                int at = Math.Min(sroAfter, parts.Count);
                parts.Insert(at, sroFile);
                titles.Insert(at, null);
            }
            if (st.MultiPage) PdfMerge.Merge(parts, st.Bookmarks ? titles : null, pdfPath);
            else
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    string dst = Path.Combine(dir, stem + "-" + (i + 1).ToString("00") + ".pdf");
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(parts[i], dst);
                }
            }
        }

        /// <summary>Один лист = одно задание движка печати (в PDF-файл или на принтер).</summary>
        static bool PlotOne(Document doc, Database db, Settings st, FrameRow f, string file, int n, int total)
        {
            for (int w = 0; w < 100 && PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting; w++)
                System.Threading.Thread.Sleep(100);
            if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting)
                throw new InvalidOperationException("AutoCAD занят другой печатью.");

            bool cancelled = false;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Layout lay = LayoutOf(tr, f);
                PlotSettings ps = BuildSettings(tr, db, st, f, "FP-tmp");
                PlotInfo pi = new PlotInfo();
                pi.Layout = lay.ObjectId;
                pi.OverrideSettings = ps;
                PlotInfoValidator piv = new PlotInfoValidator();
                piv.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
                piv.Validate(pi);

                using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                using (PlotProgressDialog ppd = new PlotProgressDialog(false, 1, true))
                {
                    ppd.set_PlotMsgString(PlotMessageIndex.DialogTitle, "Пакетная печать рамок — лист " + n + " из " + total);
                    ppd.set_PlotMsgString(PlotMessageIndex.CancelJobButtonMessage, "Отменить");
                    ppd.set_PlotMsgString(PlotMessageIndex.CancelSheetButtonMessage, "Отменить лист");
                    ppd.set_PlotMsgString(PlotMessageIndex.SheetSetProgressCaption, "Задание");
                    ppd.set_PlotMsgString(PlotMessageIndex.SheetProgressCaption, "Лист");
                    ppd.LowerPlotProgressRange = 0;
                    ppd.UpperPlotProgressRange = 100;
                    ppd.PlotProgressPos = 0;
                    ppd.OnBeginPlot();
                    ppd.IsVisible = true;
                    pe.BeginPlot(ppd, null);
                    if (file != null) pe.BeginDocument(pi, doc.Name, null, 1, true, file);
                    else pe.BeginDocument(pi, doc.Name, null, 1, false, "");
                    ppd.OnBeginSheet();
                    ppd.LowerSheetProgressRange = 0;
                    ppd.UpperSheetProgressRange = 100;
                    ppd.SheetProgressPos = 0;
                    PlotPageInfo ppi = new PlotPageInfo();
                    pe.BeginPage(ppi, pi, true, null);
                    pe.BeginGenerateGraphics(null);
                    pe.EndGenerateGraphics(null);
                    pe.EndPage(null);
                    ppd.SheetProgressPos = 100;
                    ppd.OnEndSheet();
                    pe.EndDocument(null);
                    ppd.PlotProgressPos = 100;
                    ppd.OnEndPlot();
                    pe.EndPlot(null);
                    cancelled = ppd.IsPlotCancelled;
                }
                tr.Commit();
            }
            return cancelled;
        }

        static void PatchDsd(string path, Settings st, bool toPdf)
        {
            Encoding enc;
            string text;
            using (StreamReader r = new StreamReader(path, Encoding.Default, true))
            {
                text = r.ReadToEnd();
                enc = r.CurrentEncoding;
            }
            string opts = "[PdfOptions]\r\n" +
                "IncludeHyperlinks=FALSE\r\n" +
                "CreateBookmarks=FALSE\r\n" +
                "CaptureFontsInDrawing=" + (st.EmbedFonts ? "TRUE" : "FALSE") + "\r\n" +
                "ConvertTextToGeometry=" + (st.TextToGeometry ? "TRUE" : "FALSE") + "\r\n" +
                "VectorResolution=" + st.VectorDpi + "\r\n" +
                "RasterResolution=" + st.RasterDpi + "\r\n";
            text = Regex.Replace(text, @"\[PdfOptions\][^\[]*", "");
            int i = text.IndexOf("[AutoCAD Block Data]", StringComparison.Ordinal);
            if (i < 0) i = text.IndexOf("[SheetSet Properties]", StringComparison.Ordinal);
            text = i >= 0 ? text.Insert(i, opts) : text + "\r\n" + opts;
            text = Regex.Replace(text, @"PromptForDwfName=TRUE", "PromptForDwfName=FALSE");
            File.WriteAllText(path, text, enc);
        }

        // =====================================================================
        //  ДОПУСК СРО: страницы PDF как подложки в Модели
        // =====================================================================
        public static int PdfPageCount(string path)
        {
            try
            {
                string s = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));
                int max = 0;
                foreach (Match m in Regex.Matches(s, @"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)|/Count\s+(\d+)[^>]*?/Type\s*/Pages\b"))
                {
                    int v;
                    string g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (int.TryParse(g, out v) && v > max) max = v;
                }
                if (max == 0) max = Regex.Matches(s, @"/Type\s*/Page(?!s)\b").Count;
                return Math.Max(1, max);
            }
            catch { return 1; }
        }

        static List<FrameRow> AddSroPages(Settings st, Database db, List<FrameRow> rows)
        {
            List<FrameRow> res = new List<FrameRow>();
            int count = st.SroPageCount > 0 ? st.SroPageCount : PdfPageCount(st.SroPath);
            string device = string.IsNullOrEmpty(st.PdfDevice) ? rows[0].Fmt.Device : st.PdfDevice;

            // место: правее всех рамок
            double x0 = double.MinValue, y0 = double.MaxValue;
            foreach (FrameRow f in rows) if (f.IsModel) { x0 = Math.Max(x0, f.MaxX); y0 = Math.Min(y0, f.MinY); }
            if (x0 == double.MinValue) { x0 = 0; y0 = 0; }
            x0 += 100000;

            Document doc = Doc;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                string key = UnderlayDefinition.GetDictionaryKey(typeof(PdfDefinition));
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                DBDictionary pdfDict;
                if (nod.Contains(key)) pdfDict = (DBDictionary)tr.GetObject(nod.GetAt(key), OpenMode.ForWrite);
                else
                {
                    pdfDict = new DBDictionary();
                    nod.SetAt(key, pdfDict);
                    tr.AddNewlyCreatedDBObject(pdfDict, true);
                }
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                double x = x0;
                for (int p = 1; p <= count; p++)
                {
                    PdfDefinition def = new PdfDefinition();
                    def.SourceFileName = st.SroPath;
                    def.ItemName = p.ToString(CultureInfo.InvariantCulture);
                    string defName = "FP_SRO_" + p + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    ObjectId defId = pdfDict.SetAt(defName, def);
                    tr.AddNewlyCreatedDBObject(def, true);
                    try { def.Load(null); }
                    catch (System.Exception ex) { throw new InvalidOperationException("AutoCAD не смог открыть PDF допуска (стр. " + p + "): " + ex.Message); }
                    if (!def.Loaded) throw new InvalidOperationException("AutoCAD не смог загрузить страницу " + p + " PDF допуска (файл защищён паролем или повреждён?)");

                    PdfReference rf = new PdfReference();
                    rf.SetDatabaseDefaults(db);
                    rf.Layer = SroLayer(tr, db);
                    rf.DefinitionId = defId;
                    rf.Position = new Point3d(x, y0, 0);
                    ObjectId rid = ms.AppendEntity(rf);
                    tr.AddNewlyCreatedDBObject(rf, true);
                    try { rf.Fade = 0; rf.Contrast = 100; rf.Monochrome = false; } catch { }

                    Extents3d ext;
                    try { ext = rf.GeometricExtents; }
                    catch { throw new InvalidOperationException("не удалось определить размер страницы " + p + " PDF допуска"); }
                    FrameRow f = new FrameRow();
                    f.IsSro = true; f.SroRefId = rid; f.FrameId = rid;
                    f.IsModel = true; f.LayoutName = "Model"; f.Scale = 1;
                    f.MinX = ext.MinPoint.X; f.MinY = ext.MinPoint.Y; f.MaxX = ext.MaxPoint.X; f.MaxY = ext.MaxPoint.Y;
                    if (f.W <= 0 || f.H <= 0) throw new InvalidOperationException("не удалось прочитать страницу " + p + " файла СРО");
                    bool land = f.W > f.H;
                    string media = FindMedia(device, land ? 297 : 210, land ? 210 : 297);
                    if (media == null) throw new InvalidOperationException("у плоттера «" + device + "» нет листа A4");
                    TemplateRow t = new TemplateRow();
                    t.Name = "СРО стр." + p; t.Device = device; t.Media = media; t.Style = rows[0].Fmt.Style; t.Fit = true; t.Flip = 0;
                    t.Width = land ? 297 : 210; t.Height = land ? 210 : 297;
                    f.Fmt = t;
                    f.Name = "Допуск СРО, стр. " + p;
                    res.Add(f);
                    x += f.W * 1.5 + 10;
                }
                tr.Commit();
            }
            return res;
        }

        const string SroLayerName = "FP_SRO";

        static string SroLayer(Transaction tr, Database db)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord l;
            if (lt.Has(SroLayerName)) l = (LayerTableRecord)tr.GetObject(lt[SroLayerName], OpenMode.ForWrite);
            else
            {
                l = new LayerTableRecord(); l.Name = SroLayerName;
                lt.UpgradeOpen(); lt.Add(l); tr.AddNewlyCreatedDBObject(l, true);
            }
            l.IsOff = false; l.IsFrozen = false; l.IsPlottable = true;
            return SroLayerName;
        }

        static void RemoveSro(Transaction tr, Database db, List<FrameRow> sro)
        {
            foreach (FrameRow f in sro)
            {
                if (f.SroRefId.IsNull || f.SroRefId.IsErased) continue;
                PdfReference rf = tr.GetObject(f.SroRefId, OpenMode.ForWrite) as PdfReference;
                if (rf == null) continue;
                ObjectId defId = rf.DefinitionId;
                rf.Erase();
                if (!defId.IsNull && !defId.IsErased)
                {
                    PdfDefinition def = tr.GetObject(defId, OpenMode.ForWrite) as PdfDefinition;
                    if (def != null)
                    {
                        try { def.Unload(); } catch { }
                        DBDictionary owner = tr.GetObject(def.OwnerId, OpenMode.ForWrite) as DBDictionary;
                        if (owner != null)
                        {
                            foreach (DBDictionaryEntry e in owner)
                                if (e.Value == defId) { owner.Remove(e.Key); break; }
                        }
                        def.Erase();
                    }
                }
            }
        }
    }
}
