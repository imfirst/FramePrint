using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;

namespace FramePrint
{
    public class TemplateRow
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Device { get; set; }
        public string Media { get; set; }
        public string Style { get; set; }
        public bool Fit { get; set; }
        public int Flip { get; set; }

        public TemplateRow()
        {
            Name = ""; Device = ""; Media = ""; Style = ""; Fit = true;
        }

        public TemplateRow Clone()
        {
            return (TemplateRow)MemberwiseClone();
        }
    }

    public class PrintTemplate
    {
        public string Name { get; set; }
        public List<TemplateRow> Rows { get; set; }

        public PrintTemplate()
        {
            Name = "";
            Rows = new List<TemplateRow>();
        }

        public PrintTemplate Clone()
        {
            PrintTemplate t = new PrintTemplate();
            t.Name = Name;
            foreach (TemplateRow r in Rows) t.Rows.Add(r.Clone());
            return t;
        }

        public TemplateRow Find(string name)
        {
            foreach (TemplateRow r in Rows)
                if (string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }

    public enum SaveMode
    {
        [Description("Рядом с чертежом")] NextToDwg,
        [Description("В указанную папку")] Folder,
        [Description("В подпапку рядом с чертежом")] SubFolder
    }

    public enum OutputMethod
    {
        [Description("Движок печати (из открытого чертежа)")] Engine,
        [Description("Публикация DSD")] Publish
    }

    public enum SortMode
    {
        [Description("Y-X: строками сверху вниз, слева направо")] YX,
        [Description("X-Y: столбцами слева направо, сверху вниз")] XY,
        [Description("По номеру листа")] SheetNumber,
        [Description("Без сортировки")] None
    }

    /// <summary>Точка поиска текста в штампе: смещение от ПРАВОГО НИЖНЕГО угла рамки (мм на листе) и радиус.</summary>
    public struct StampPoint
    {
        public double X, Y, R;
        public bool IsEmpty { get { return R <= 0; } }

        public static StampPoint Parse(string s)
        {
            StampPoint p = new StampPoint();
            if (string.IsNullOrEmpty(s)) return p;
            string[] a = s.Replace(',', '.').Split(new char[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length >= 3)
            {
                double.TryParse(a[0], NumberStyles.Float, CultureInfo.InvariantCulture, out p.X);
                double.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p.Y);
                double.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out p.R);
            }
            return p;
        }
    }

    public class Settings
    {
        // ---------- Поиск ----------
        [Category("1. Поиск рамок"), DisplayName("Слои рамок"), Description("Через ; например: FORMAT;РАМКА")]
        public string Layers { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Масштабы"), Description("Масштаб(ы) рамок в Модели через ; например: 50 или 25;50;100. В Листах масштаб всегда 1.")]
        public string Scales { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Искать в Модели")]
        public bool SearchModel { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Искать в Листах")]
        public bool SearchLayouts { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Только замкнутые полилинии")]
        public bool ClosedOnly { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Допуск размеров, мм")]
        public double Tolerance { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Игнорировать дубликаты")]
        public bool IgnoreDuplicates { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Сортировка")]
        public SortMode Sort { get; set; }
        [Category("1. Поиск рамок"), DisplayName("Допуск сортировки, мм"), Description("Насколько могут отличаться низы рамок одной строки (мм на листе)")]
        public double SortTolerance { get; set; }

        // ---------- Штамп ----------
        [Category("2. Штамп"), DisplayName("Номер листа: X;Y;R"), Description("Смещение от правого нижнего угла рамки, мм на листе, и радиус поиска")]
        public string PtNumber { get; set; }
        [Category("2. Штамп"), DisplayName("Листов: X;Y;R")]
        public string PtTotal { get; set; }
        [Category("2. Штамп"), DisplayName("Наименование: X;Y;R")]
        public string PtName { get; set; }
        [Category("2. Штамп"), DisplayName("Шифр: X;Y;R")]
        public string PtCipher { get; set; }
        [Category("2. Штамп"), DisplayName("Искать атрибуты блоков")]
        public bool UseAttributes { get; set; }
        [Category("2. Штамп"), DisplayName("Атрибут: номер листа")]
        public string AttrNumber { get; set; }
        [Category("2. Штамп"), DisplayName("Атрибут: листов")]
        public string AttrTotal { get; set; }
        [Category("2. Штамп"), DisplayName("Атрибут: наименование")]
        public string AttrName { get; set; }
        [Category("2. Штамп"), DisplayName("Атрибут: шифр")]
        public string AttrCipher { get; set; }

        // ---------- Нумерация ----------
        [Category("3. Нумерация"), DisplayName("Начальный номер")]
        public int StartNumber { get; set; }
        [Category("3. Нумерация"), DisplayName("Пропускать титул (лист без штампа)")]
        public bool SkipTitle { get; set; }
        [Category("3. Нумерация"), DisplayName("Писать «Листов» на первом листе")]
        public bool WriteTotalOnFirst { get; set; }
        [Category("3. Нумерация"), DisplayName("Создавать текст, если ячейка пустая")]
        public bool CreateMissingText { get; set; }
        [Category("3. Нумерация"), DisplayName("Высота создаваемого текста, мм")]
        public double NumberTextHeight { get; set; }

        // ---------- Ведомость ----------
        [Category("4. Ведомость"), DisplayName("Заголовок")]
        public string VedTitle { get; set; }
        [Category("4. Ведомость"), DisplayName("Столбец 1")]
        public string VedCol1 { get; set; }
        [Category("4. Ведомость"), DisplayName("Столбец 2")]
        public string VedCol2 { get; set; }
        [Category("4. Ведомость"), DisplayName("Столбец 3")]
        public string VedCol3 { get; set; }
        [Category("4. Ведомость"), DisplayName("Ширины столбцов, мм"), Description("Через ;")]
        public string VedWidths { get; set; }
        [Category("4. Ведомость"), DisplayName("Высоты: заголовок;шапка;строка, мм")]
        public string VedHeights { get; set; }
        [Category("4. Ведомость"), DisplayName("Высоты текста: заголовок;шапка;строка")]
        public string VedTextHeights { get; set; }
        [Category("4. Ведомость"), DisplayName("Масштаб таблицы"), Description("0 = как у рамок")]
        public double VedScale { get; set; }
        [Category("4. Ведомость"), DisplayName("Пропускать листы без штампа")]
        public bool VedSkipNoStamp { get; set; }

        // ---------- PDF ----------
        [Category("5. PDF"), DisplayName("PDF-плоттер для публикации")]
        public string PdfDevice { get; set; }
        [Category("5. PDF"), DisplayName("Куда сохранять")]
        public SaveMode SaveTo { get; set; }
        [Category("5. PDF"), DisplayName("Папка"), Editor(typeof(System.Windows.Forms.Design.FolderNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string Folder { get; set; }
        [Category("5. PDF"), DisplayName("Имя подпапки")]
        public string SubFolder { get; set; }
        [Category("5. PDF"), DisplayName("Имя файла"), Description("Поля: {Объект} — начало имени чертежа до первого «_»; {Раздел} — шифр из штампа печатаемых листов (О, О.СО, ВК…); {ДатаФайла} — дата в конце имени чертежа; {Файл} — имя чертежа целиком; {Дата} — сегодня; {Шаблон}. Если шифр не прочитан — имя как у чертежа.")]
        public string FileNamePattern { get; set; }
        [Category("5. PDF"), DisplayName("Разделы для имени файла"), Description("Только эти обозначения попадают в имя PDF. Берётся последнее подходящее в строке шифра.")]
        public string Sections { get; set; }
        [Category("5. PDF"), DisplayName("Формат даты")]
        public string DateFormat { get; set; }
        [Category("5. PDF"), DisplayName("Заменять существующие файлы")]
        public bool Overwrite { get; set; }
        [Category("5. PDF"), DisplayName("Один многолистовой PDF")]
        public bool MultiPage { get; set; }
        [Category("5. PDF"), DisplayName("Закладки и подписи страниц"), Description("Закладки «Листы и виды» -> «1- Общие данные» и подписи страниц «[2] 1- Общие данные», как в Реверсе")]
        public bool Bookmarks { get; set; }
        [Category("5. PDF"), DisplayName("Открыть PDF после создания")]
        public bool OpenAfter { get; set; }
        [Category("5. PDF"), DisplayName("Качество вектора, dpi")]
        public int VectorDpi { get; set; }
        [Category("5. PDF"), DisplayName("Качество растра, dpi")]
        public int RasterDpi { get; set; }
        [Category("5. PDF"), DisplayName("Включать шрифты")]
        public bool EmbedFonts { get; set; }
        [Category("5. PDF"), DisplayName("Текст в геометрию")]
        public bool TextToGeometry { get; set; }

        // ---------- СРО ----------
        [Category("6. Допуск СРО"), DisplayName("Вставлять допуск СРО в PDF")]
        public bool SroEnabled { get; set; }
        [Category("6. Допуск СРО"), DisplayName("Файл PDF допуска"), Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string SroPath { get; set; }
        [Category("6. Допуск СРО"), DisplayName("Вставлять PDF как есть"), Description("Страницы файла допуска копируются в итоговый PDF без изменений (маленький размер). Если файл защищён или не читается — вставляется картинкой.")]
        public bool SroAsIs { get; set; }
        [Category("6. Допуск СРО"), DisplayName("Вставить после листа №"), Description("1 = сразу после титульного листа")]
        public int SroAfterPage { get; set; }
        [Category("6. Допуск СРО"), DisplayName("Страниц в файле"), Description("0 = определить автоматически")]
        public int SroPageCount { get; set; }
        [Category("6. Допуск СРО"), DisplayName("Напоминать, если файлу больше, дней")]
        public int SroMaxAgeDays { get; set; }

        // ---------- Прочее ----------
        [Category("7. Печать"), DisplayName("Копий")]
        public int Copies { get; set; }
        [Category("7. Печать"), DisplayName("Инвертировать автоориентацию"), Description("Включите, если листы выходят повёрнутыми на 90°")]
        public bool InvertAutoOrientation { get; set; }
        [Category("7. Печать"), DisplayName("Печатать веса линий")]
        public bool PlotLineweights { get; set; }

        [Category("7. Печать"), DisplayName("Оставлять наборы FP-… в чертеже"), Description("Для отладки: после публикации наборы параметров листов не удаляются, их можно открыть в Диспетчере наборов параметров листов и посмотреть предварительный просмотр")]
        public bool KeepSetups { get; set; }

        [Browsable(false), Category("7. Печать"), DisplayName("Способ вывода"), Description("Publish — публикация DSD, как в Реверсе (рекомендуется). Engine — экспериментальная печать движком.")]
        public OutputMethod Output { get; set; }

        [Browsable(false)] public int SchemaVersion { get; set; }
        [Browsable(false)] public string ActiveTemplate { get; set; }
        [Browsable(false)] public List<PrintTemplate> Templates { get; set; }

        public Settings()
        {
            Layers = "FORMAT"; Scales = "50"; SearchModel = true; SearchLayouts = false; ClosedOnly = false;
            Tolerance = 1; IgnoreDuplicates = true; Sort = SortMode.YX; SortTolerance = 0;
            PtNumber = "32;25;2"; PtTotal = "15;25;2"; PtName = "90;12;6"; PtCipher = "65;55;2";
            UseAttributes = true; AttrNumber = "Лист"; AttrTotal = "Листов"; AttrName = "Наименование"; AttrCipher = "Шифр";
            StartNumber = 1; SkipTitle = true; WriteTotalOnFirst = true; CreateMissingText = true; NumberTextHeight = 2.5;
            VedTitle = "ВЕДОМОСТЬ РАБОЧИХ ЧЕРТЕЖЕЙ ОСНОВНОГО КОМПЛЕКТА"; VedCol1 = "Лист"; VedCol2 = "Наименование"; VedCol3 = "Примечание";
            VedWidths = "15;140;30"; VedHeights = "12;15;8"; VedTextHeights = "5;3.5;3.5"; VedScale = 0; VedSkipNoStamp = true;
            PdfDevice = "DWG To PDF.pc3"; SaveTo = SaveMode.Folder;
            Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "");
            SubFolder = "PDF"; FileNamePattern = "{Объект}_{Раздел}_{ДатаФайла}"; DateFormat = "yyyy-MM-dd"; Sections = "О;ВК;ЭОМ;ВиК;СС;АР;ОВ;АПС;СОУЭ"; Overwrite = false; MultiPage = true; Bookmarks = true; OpenAfter = true;
            VectorDpi = 1200; RasterDpi = 400; EmbedFonts = true; TextToGeometry = false;
            SroEnabled = true; SroAsIs = true; SroPath = ""; SroAfterPage = 1; SroPageCount = 0; SroMaxAgeDays = 35;
            Copies = 1; Output = OutputMethod.Publish; InvertAutoOrientation = false; PlotLineweights = true;
            ActiveTemplate = "PDF_поворот"; SchemaVersion = 0;
            Templates = new List<PrintTemplate>();
        }

        public PrintTemplate GetTemplate(string name)
        {
            foreach (PrintTemplate t in Templates)
                if (t.Name == name) return t;
            return Templates.Count > 0 ? Templates[0] : null;
        }

        public List<double> ScaleList()
        {
            List<double> res = new List<double>();
            foreach (string s in (Scales ?? "").Replace(',', '.').Split(new char[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) res.Add(v);
            }
            if (res.Count == 0) res.Add(1);
            return res;
        }

        public static double[] Nums(string s, params double[] def)
        {
            List<double> res = new List<double>();
            foreach (string x in (s ?? "").Replace(',', '.').Split(new char[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) res.Add(v);
            }
            for (int i = res.Count; i < def.Length; i++) res.Add(def[i]);
            return res.ToArray();
        }

        // ---------- хранение ----------
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FramePrint"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.xml"); } }

        static Settings _current;
        public static Settings Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
            set { _current = value; }
        }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    XmlSerializer xs = new XmlSerializer(typeof(Settings));
                    using (FileStream fs = File.OpenRead(FilePath))
                    {
                        Settings s = (Settings)xs.Deserialize(fs);
                        if (s.Templates == null || s.Templates.Count == 0) s.Templates = DefaultTemplates();
                        if (s.SchemaVersion < 3)
                        {
                            if (string.IsNullOrEmpty(s.FileNamePattern) || s.FileNamePattern == "{Файл}")
                                s.FileNamePattern = "{Объект}_{Раздел}_{ДатаФайла}";
                            s.SchemaVersion = 3;
                        }
                        return s;
                    }
                }
            }
            catch { }
            Settings d = new Settings();
            d.Templates = DefaultTemplates();
            d.SchemaVersion = 3;
            return d;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            XmlSerializer xs = new XmlSerializer(typeof(Settings));
            string tmp = FilePath + ".tmp";
            using (FileStream fs = File.Create(tmp)) xs.Serialize(fs, this);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }

        public Settings Clone()
        {
            XmlSerializer xs = new XmlSerializer(typeof(Settings));
            using (MemoryStream ms = new MemoryStream())
            {
                xs.Serialize(ms, this);
                ms.Position = 0;
                return (Settings)xs.Deserialize(ms);
            }
        }

        static void R(List<PrintTemplate> t, string tpl, string name, double w, double h, string dev, string media, string style, bool fit, int flip)
        {
            PrintTemplate pt = null;
            foreach (PrintTemplate x in t) if (x.Name == tpl) pt = x;
            if (pt == null) { pt = new PrintTemplate(); pt.Name = tpl; t.Add(pt); }
            TemplateRow r = new TemplateRow();
            r.Name = name; r.Width = w; r.Height = h; r.Device = dev; r.Media = media; r.Style = style; r.Fit = fit; r.Flip = flip;
            pt.Rows.Add(r);
        }

        /// <summary>Шаблоны, перенесённые из настроек пользователя.</summary>
        public static List<PrintTemplate> DefaultTemplates()
        {
            List<PrintTemplate> t = new List<PrintTemplate>();
            R(t, "Standart", "A0", 1189, 841, "", "", "", true, 0);
            R(t, "Standart", "A0x2", 1682, 1189, "", "", "", true, 0);
            R(t, "Standart", "A0x3", 2523, 1189, "", "", "", true, 0);
            R(t, "Standart", "A1", 841, 594, "", "", "", true, 0);
            R(t, "Standart", "A1x3", 1783, 841, "", "", "", true, 0);
            R(t, "Standart", "A1x4", 2378, 841, "", "", "", true, 0);
            R(t, "Standart", "A2", 594, 420, "", "", "", true, 0);
            R(t, "Standart", "A2x3", 1261, 594, "", "", "", true, 0);
            R(t, "Standart", "A2x4", 1682, 594, "", "", "", true, 0);
            R(t, "Standart", "A2x5", 2102, 594, "", "", "", true, 0);
            R(t, "Standart", "A3", 420, 297, "", "", "", true, 0);
            R(t, "Standart", "A3x3", 891, 420, "", "", "", true, 0);
            R(t, "Standart", "A3x4", 1189, 420, "", "", "", true, 0);
            R(t, "Standart", "A3x5", 1486, 420, "", "", "", true, 0);
            R(t, "Standart", "A3x6", 1783, 420, "", "", "", true, 0);
            R(t, "Standart", "A3x7", 2080, 420, "", "", "", true, 0);
            R(t, "Standart", "A4", 297, 210, "", "", "", true, 0);
            R(t, "Standart", "A4x3", 630, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x4", 841, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x5", 1051, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x6", 1261, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x7", 1471, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x8", 1682, 297, "", "", "", true, 0);
            R(t, "Standart", "A4x9", 1892, 297, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A0-A", 1189, 841, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A0-K", 841, 1189, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A1-A", 841, 594, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A1-K", 594, 841, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A2-A", 594, 420, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A2-K", 420, 594, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A3-A", 420, 297, "DWG To PDF_less.pc3", "ISO full bleed A3 (420.00 x 297.00 MM)", "", true, 0);
            R(t, "PDF_a3_povorot", "A3-K", 297, 420, "DWG To PDF_less.pc3", "ISO full bleed A3 (297.00 x 420.00 MM)", "", true, 0);
            R(t, "PDF_a3_povorot", "A4-A", 297, 210, "", "", "", true, 0);
            R(t, "PDF_a3_povorot", "A4-K", 210, 297, "", "", "", true, 0);
            R(t, "Принтер", "A0-A", 1189, 841, "", "", "", true, 0);
            R(t, "Принтер", "A0-K", 841, 1189, "", "", "", true, 0);
            R(t, "Принтер", "A1-A", 841, 594, "", "", "", true, 0);
            R(t, "Принтер", "A1-K", 594, 841, "", "", "", true, 0);
            R(t, "Принтер", "A2-A", 594, 420, "", "", "", true, 0);
            R(t, "Принтер", "A2-K", 420, 594, "", "", "", true, 0);
            R(t, "Принтер", "A3-A", 420, 297, "!HP Deskjet 1280 series_less.pc3", "A3", "acad.ctb", true, 180);
            R(t, "Принтер", "A3-K", 297, 420, "!HP Deskjet 1280 series_less.pc3", "A3", "acad.ctb", true, 180);
            R(t, "Принтер", "A4-A", 297, 210, "", "", "", true, 0);
            R(t, "Принтер", "A4-K", 210, 297, "", "", "", true, 0);
            R(t, "PDF_поворот", "A0-A", 1189, 841, "DWG To PDF.pc3", "ISO full bleed A0 (841.00 x 1189.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A1", 841, 594, "DWG To PDF.pc3", "ISO full bleed A1 (841.00 x 594.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A0-K", 841, 1189, "", "", "", true, 0);
            R(t, "PDF_поворот", "A1-A", 841, 594, "DWG To PDF.pc3", "ISO full bleed A1 (841.00 x 594.00 MM)", "acad.ctb", true, 180);
            R(t, "PDF_поворот", "A1-K", 594, 841, "DWG To PDF.pc3", "ISO full bleed A1 (594.00 x 841.00 MM)", "acad.ctb", true, 180);
            R(t, "PDF_поворот", "A2-A", 594, 420, "DWG To PDF.pc3", "ISO full bleed A2 (594.00 x 420.00 MM)", "acad.ctb", true, 180);
            R(t, "PDF_поворот", "A2-K", 420, 594, "DWG To PDF.pc3", "ISO full bleed A2 (420.00 x 594.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A3-A", 420, 297, "DWG To PDF.pc3", "ISO full bleed A3 (420.00 x 297.00 MM)", "acad.ctb", true, 180);
            R(t, "PDF_поворот", "A3-K", 297, 420, "DWG To PDF.pc3", "ISO full bleed A3 (297.00 x 420.00 MM)", "acad.ctb", true, 180);
            R(t, "PDF_поворот", "A3_1-5", 630, 297, "DWG To PDF.pc3", "ISO full bleed B5 (250.00 x 176.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A4-A", 297, 210, "DWG To PDF.pc3", "ISO full bleed A4 (297.00 x 210.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A4-K", 210, 297, "DWG To PDF.pc3", "ISO full bleed A4 (210.00 x 297.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "A2_LONG", 420, 891, "DWG To PDF_a2long.pc3", "A2_LONG (420.00 x 891.00 MM)", "acad.ctb", true, 0);
            R(t, "PDF_поворот", "F3_long2", 297, 841, "DWG To PDF.pc3", "User 1 (840.00 x 297.00 MM)", "acad.ctb", true, 0);
            return t;
        }
    }
}
