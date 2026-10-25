using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FramePrint
{
    /// <summary>
    /// Склейка PDF, созданных плоттером AutoCAD (pdfplot): классическая таблица xref,
    /// без потоков объектов. Порядок страниц сохраняется, двоичные потоки копируются без изменений.
    /// </summary>
    public static class PdfMerge
    {
        static readonly Encoding L1 = Encoding.GetEncoding(28591);   // байт = символ
        static readonly Regex ObjHead = new Regex(@"(\d+)\s+(\d+)\s+obj\b", RegexOptions.Compiled);
        static readonly Regex Ref = new Regex(@"(\d+)\s+(\d+)\s+R\b", RegexOptions.Compiled);
        static readonly Regex LengthRx = new Regex(@"/Length\s+(\d+)(?:\s+(\d+)\s+R)?", RegexOptions.Compiled);

        class Obj
        {
            public int Num;
            public string Dict;     // часть до слова stream (или весь объект)
            public string Stream;   // сырые байты потока (latin1) или null
        }

        class Doc
        {
            public Dictionary<int, Obj> Objs = new Dictionary<int, Obj>();
            public List<int> Pages = new List<int>();
            public HashSet<int> Skip = new HashSet<int>();   // каталог, узлы Pages, Info
        }

        public static void Merge(IList<string> inputs, string output)
        {
            Merge(inputs, null, output);
        }

        /// <summary>
        /// titles[i] — название страницы i для закладок и подписей страниц («1- Общие данные»);
        /// null — без закладки (страница наследует подпись предыдущей, как допуск СРО после титула).
        /// </summary>
        public static void Merge(IList<string> inputs, IList<string> titles, string output)
        {
            List<Doc> docs = new List<Doc>();
            foreach (string f in inputs) docs.Add(Parse(L1.GetString(File.ReadAllBytes(f)), f));

            StringBuilder sb = new StringBuilder();
            List<long> offsets = new List<long>();   // индекс = новый номер
            offsets.Add(0);
            MemoryStream ms = new MemoryStream();
            Write(ms, "%PDF-1.7\n%Þ­¾ï\n");

            int next = 1;
            int pagesRoot = -1; // номер назначим в конце
            List<int> newPages = new List<int>();
            List<KeyValuePair<int, Obj>> outObjs = new List<KeyValuePair<int, Obj>>();
            List<Dictionary<int, int>> maps = new List<Dictionary<int, int>>();
            List<string> itemTitles = new List<string>();

            foreach (Doc d in docs)
            {
                Dictionary<int, int> map = new Dictionary<int, int>();
                List<int> nums = new List<int>(d.Objs.Keys);
                nums.Sort();
                foreach (int n in nums) if (!d.Skip.Contains(n)) map[n] = next++;
                maps.Add(map);
            }
            pagesRoot = next++;
            int catalog = next++;
            bool marks = titles != null && titles.Count == inputs.Count;
            int outlines = 0, group = 0, labels = 0;
            List<int> itemNums = new List<int>();
            List<int> itemPage = new List<int>();   // индекс страницы в newPages
            if (marks)
            {
                outlines = next++; group = next++; labels = next++;
            }

            for (int k = 0; k < docs.Count; k++)
            {
                Doc d = docs[k];
                Dictionary<int, int> map = maps[k];
                List<int> nums = new List<int>(map.Keys);
                nums.Sort();
                foreach (int n in nums)
                {
                    Obj o = d.Objs[n];
                    string dict = Ref.Replace(o.Dict, delegate (Match m)
                    {
                        int r = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        int nr;
                        if (map.TryGetValue(r, out nr)) return nr + " 0 R";
                        if (d.Skip.Contains(r)) return pagesRoot + " 0 R";   // /Parent на старый узел Pages
                        return "null";
                    });
                    Obj no = new Obj(); no.Num = map[n]; no.Dict = dict; no.Stream = o.Stream;
                    outObjs.Add(new KeyValuePair<int, Obj>(map[n], no));
                }
                if (marks && titles[k] != null && d.Pages.Count > 0) itemPage.Add(newPages.Count);
                if (marks && titles[k] != null) itemTitles.Add(titles[k]);
                foreach (int p in d.Pages) newPages.Add(map[p]);
            }
            if (marks) foreach (int ipg in itemPage) itemNums.Add(next++);

            long[] off = new long[next];
            foreach (KeyValuePair<int, Obj> kv in outObjs)
            {
                off[kv.Key] = ms.Position;
                Write(ms, kv.Key + " 0 obj\n");
                Write(ms, kv.Value.Dict.TrimEnd());
                if (kv.Value.Stream != null)
                {
                    Write(ms, "\nstream\n");
                    Write(ms, kv.Value.Stream);
                    Write(ms, "\nendstream");
                }
                Write(ms, "\nendobj\n");
            }
            StringBuilder kids = new StringBuilder();
            foreach (int p in newPages) kids.Append(p).Append(" 0 R ");
            off[pagesRoot] = ms.Position;
            Write(ms, pagesRoot + " 0 obj\n<< /Type /Pages /Kids [" + kids.ToString().TrimEnd() + "] /Count " + newPages.Count + " >>\nendobj\n");
            if (marks && itemNums.Count > 0)
            {
                // закладки: «Листы и виды» -> листы
                off[outlines] = ms.Position;
                Write(ms, outlines + " 0 obj\n<< /Type /Outlines /First " + group + " 0 R /Last " + group + " 0 R /Count " + (itemNums.Count + 1) + " >>\nendobj\n");
                off[group] = ms.Position;
                Write(ms, group + " 0 obj\n<< /Title " + PdfText("Листы и виды") + " /Parent " + outlines + " 0 R /First " + itemNums[0] +
                    " 0 R /Last " + itemNums[itemNums.Count - 1] + " 0 R /Count " + itemNums.Count + " >>\nendobj\n");
                for (int i = 0; i < itemNums.Count; i++)
                {
                    StringBuilder it = new StringBuilder();
                    it.Append("<< /Title ").Append(PdfText(itemTitles[i])).Append(" /Parent ").Append(group).Append(" 0 R");
                    if (i > 0) it.Append(" /Prev ").Append(itemNums[i - 1]).Append(" 0 R");
                    if (i < itemNums.Count - 1) it.Append(" /Next ").Append(itemNums[i + 1]).Append(" 0 R");
                    it.Append(" /Dest [").Append(newPages[itemPage[i]]).Append(" 0 R /Fit] >>");
                    off[itemNums[i]] = ms.Position;
                    Write(ms, itemNums[i] + " 0 obj\n" + it + "\nendobj\n");
                }
                // подписи страниц: «[2] 1- Общие данные»
                StringBuilder nums = new StringBuilder();
                for (int i = 0; i < itemNums.Count; i++)
                {
                    int pg = i == 0 ? 0 : itemPage[i];
                    nums.Append(pg).Append(" << /P ").Append(PdfText("[" + (i + 1) + "] " + itemTitles[i])).Append(" /St 1 >> ");
                }
                off[labels] = ms.Position;
                Write(ms, labels + " 0 obj\n<< /Nums [" + nums.ToString().TrimEnd() + "] >>\nendobj\n");
                off[catalog] = ms.Position;
                Write(ms, catalog + " 0 obj\n<< /Type /Catalog /Pages " + pagesRoot + " 0 R /Outlines " + outlines +
                    " 0 R /PageMode /UseOutlines /PageLabels " + labels + " 0 R >>\nendobj\n");
            }
            else
            {
                off[catalog] = ms.Position;
                Write(ms, catalog + " 0 obj\n<< /Type /Catalog /Pages " + pagesRoot + " 0 R >>\nendobj\n");
            }

            long xref = ms.Position;
            StringBuilder x = new StringBuilder();
            x.Append("xref\n0 ").Append(next).Append("\n0000000000 65535 f \n");
            for (int i = 1; i < next; i++)
            {
                if (off[i] > 0) x.Append(off[i].ToString("0000000000")).Append(" 00000 n \n");
                else x.Append("0000000000 65535 f \n");
            }
            x.Append("trailer\n<< /Size ").Append(next).Append(" /Root ").Append(catalog).Append(" 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            Write(ms, x.ToString());

            string tmp = output + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            if (File.Exists(output)) File.Delete(output);
            File.Move(tmp, output);
        }

        /// <summary>Текстовая строка PDF в UTF-16BE: &lt;FEFF....&gt;</summary>
        static string PdfText(string t)
        {
            StringBuilder sb = new StringBuilder("<FEFF");
            foreach (byte b in Encoding.BigEndianUnicode.GetBytes(t ?? "")) sb.Append(b.ToString("X2"));
            return sb.Append('>').ToString();
        }

        static void Write(Stream s, string text)
        {
            byte[] b = L1.GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        /// <summary>Можно ли вставить этот PDF «как есть» (не зашифрован, читается).</summary>
        public static bool CanMerge(string path, out string reason)
        {
            reason = null;
            try
            {
                Doc d = Parse(L1.GetString(File.ReadAllBytes(path)), path);
                if (d.Pages.Count == 0) { reason = "в файле не найдено страниц"; return false; }
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        public static int PageCount(string path)
        {
            try { return Parse(L1.GetString(File.ReadAllBytes(path)), path).Pages.Count; }
            catch { return 0; }
        }

        static Doc Parse(string t, string name)
        {
            string fn = Path.GetFileName(name);
            if (Regex.IsMatch(t, @"/Encrypt\s+\d+\s+\d+\s+R|/Encrypt\s*<<"))
                throw new InvalidOperationException("PDF защищён (зашифрован): " + fn);
            Doc d = new Doc();
            int pos = 0;
            while (true)
            {
                Match h = ObjHead.Match(t, pos);
                if (!h.Success) break;
                int num = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
                int bodyStart = h.Index + h.Length;
                int endobj = t.IndexOf("endobj", bodyStart, StringComparison.Ordinal);
                int stream = t.IndexOf("stream", bodyStart, StringComparison.Ordinal);
                Obj o = new Obj(); o.Num = num;
                if (stream >= 0 && (endobj < 0 || stream < endobj) && !IsEndstream(t, stream))
                {
                    o.Dict = t.Substring(bodyStart, stream - bodyStart);
                    int ds = stream + 6;
                    if (ds < t.Length && t[ds] == '\r') ds++;
                    if (ds < t.Length && t[ds] == '\n') ds++;
                    int len = StreamLength(t, o.Dict);
                    if (len < 0 || ds + len > t.Length)
                    {
                        int es = t.IndexOf("endstream", ds, StringComparison.Ordinal);
                        if (es < 0) break;
                        len = es - ds;
                        while (len > 0 && (t[ds + len - 1] == '\n' || t[ds + len - 1] == '\r')) len--;
                    }
                    o.Stream = t.Substring(ds, len);
                    int after = t.IndexOf("endobj", ds + len, StringComparison.Ordinal);
                    pos = after < 0 ? t.Length : after + 6;
                }
                else
                {
                    if (endobj < 0) break;
                    o.Dict = t.Substring(bodyStart, endobj - bodyStart);
                    pos = endobj + 6;
                }
                d.Objs[num] = o;   // при дополнениях файла более поздняя версия перекрывает раннюю
            }

            // сжатые наборы объектов (/ObjStm) распаковываем, служебные потоки /XRef выбрасываем
            List<int> service = new List<int>();
            Dictionary<int, Obj> fromStreams = new Dictionary<int, Obj>();
            foreach (KeyValuePair<int, Obj> kv in d.Objs)
            {
                Obj o = kv.Value;
                if (o.Stream == null) continue;
                if (Regex.IsMatch(o.Dict, @"/Type\s*/XRef\b")) { service.Add(kv.Key); continue; }
                if (!Regex.IsMatch(o.Dict, @"/Type\s*/ObjStm\b")) continue;
                service.Add(kv.Key);
                string data = Inflate(o, fn);
                int n = IntKey(o.Dict, "N"), first = IntKey(o.Dict, "First");
                string[] head = data.Substring(0, Math.Min(first, data.Length))
                    .Split(new char[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < head.Length && i / 2 < n; i += 2)
                {
                    int on = int.Parse(head[i], CultureInfo.InvariantCulture);
                    int st = first + int.Parse(head[i + 1], CultureInfo.InvariantCulture);
                    int en = (i + 3 < head.Length) ? first + int.Parse(head[i + 3], CultureInfo.InvariantCulture) : data.Length;
                    if (st < 0 || st > data.Length || en > data.Length || en < st) continue;
                    Obj so = new Obj(); so.Num = on; so.Dict = " " + data.Substring(st, en - st).Trim() + " ";
                    fromStreams[on] = so;
                }
            }
            foreach (int k in service) d.Objs.Remove(k);
            foreach (KeyValuePair<int, Obj> kv in fromStreams)
                if (!d.Objs.ContainsKey(kv.Key)) d.Objs[kv.Key] = kv.Value;

            // каталог -> дерево страниц (последний /Root в файле — самый новый)
            MatchCollection roots = Regex.Matches(t, @"/Root\s+(\d+)\s+\d+\s+R");
            if (roots.Count == 0) throw new InvalidOperationException("Не найден каталог PDF: " + fn);
            int cat = int.Parse(roots[roots.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
            MatchCollection infos = Regex.Matches(t, @"/Info\s+(\d+)\s+\d+\s+R");
            d.Skip.Add(cat);
            if (infos.Count > 0) d.Skip.Add(int.Parse(infos[infos.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture));
            Obj catObj;
            if (!d.Objs.TryGetValue(cat, out catObj)) throw new InvalidOperationException("Каталог PDF повреждён: " + fn);
            Match pr = Regex.Match(catObj.Dict, @"/Pages\s+(\d+)\s+\d+\s+R");
            if (!pr.Success) throw new InvalidOperationException("Не найдено дерево страниц: " + fn);
            Walk(d, int.Parse(pr.Groups[1].Value, CultureInfo.InvariantCulture), 0, new Dictionary<string, string>());
            return d;
        }

        static int IntKey(string dict, string key)
        {
            Match m = Regex.Match(dict, "/" + key + @"\s+(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        static string Inflate(Obj o, string fn)
        {
            if (!Regex.IsMatch(o.Dict, @"/Filter\s*/FlateDecode\b|/Filter\s*\[\s*/FlateDecode\s*\]"))
                throw new InvalidOperationException("Неизвестное сжатие объектов в " + fn);
            byte[] raw = L1.GetBytes(o.Stream);
            if (raw.Length < 2) return "";
            using (MemoryStream src = new MemoryStream(raw, 2, raw.Length - 2))   // пропуск заголовка zlib
            using (System.IO.Compression.DeflateStream z = new System.IO.Compression.DeflateStream(src, System.IO.Compression.CompressionMode.Decompress))
            using (MemoryStream dst = new MemoryStream())
            {
                z.CopyTo(dst);
                return L1.GetString(dst.ToArray());
            }
        }

        static bool IsEndstream(string t, int i)
        {
            return i >= 3 && string.CompareOrdinal(t, i - 3, "end", 0, 3) == 0;
        }

        static int StreamLength(string t, string dict)
        {
            Match m = LengthRx.Match(dict);
            if (!m.Success) return -1;
            int v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!m.Groups[2].Success) return v;
            Match lo = Regex.Match(t, @"(?<!\d)" + v + @"\s+0\s+obj\s*(\d+)\s*endobj");
            return lo.Success ? int.Parse(lo.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
        }

        static readonly string[] Inheritable = { "MediaBox", "CropBox", "Resources", "Rotate" };

        static void Walk(Doc d, int node, int depth, Dictionary<string, string> inh)
        {
            Obj o;
            if (depth > 30 || !d.Objs.TryGetValue(node, out o)) return;
            if (Regex.IsMatch(o.Dict, @"/Type\s*/Pages\b"))
            {
                d.Skip.Add(node);
                Dictionary<string, string> mine = new Dictionary<string, string>(inh);
                foreach (string k in Inheritable)
                {
                    string v = ValueOf(o.Dict, k);
                    if (v != null) mine[k] = v;
                }
                string kids = ValueOf(o.Dict, "Kids");
                if (kids == null) return;
                Match km = Regex.Match(kids, @"^(\d+)\s+\d+\s+R$");
                if (km.Success)   // массив Kids вынесен в отдельный объект
                {
                    Obj ko;
                    if (!d.Objs.TryGetValue(int.Parse(km.Groups[1].Value, CultureInfo.InvariantCulture), out ko)) return;
                    kids = ko.Dict;
                }
                foreach (Match r in Ref.Matches(kids))
                    Walk(d, int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), depth + 1, mine);
            }
            else
            {
                // унаследованные от узла Pages параметры переносим в саму страницу
                StringBuilder add = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in inh)
                    if (ValueOf(o.Dict, kv.Key) == null) add.Append(" /").Append(kv.Key).Append(' ').Append(kv.Value);
                if (add.Length > 0)
                {
                    int i = o.Dict.IndexOf("<<", StringComparison.Ordinal);
                    if (i >= 0) o.Dict = o.Dict.Insert(i + 2, add.ToString() + " ");
                }
                d.Pages.Add(node);
            }
        }

        /// <summary>Текст значения ключа словаря верхнего уровня (массив, словарь, ссылка, имя, число) или null.</summary>
        static string ValueOf(string dict, string key)
        {
            int depth = 0, i = 0, n = dict.Length;
            while (i < n)
            {
                char c = dict[i];
                if (c == '(') { i = SkipString(dict, i); continue; }
                if (c == '<' && i + 1 < n && dict[i + 1] == '<') { depth++; i += 2; continue; }
                if (c == '>' && i + 1 < n && dict[i + 1] == '>') { depth--; i += 2; continue; }
                if (c == '<') { int e = dict.IndexOf('>', i); i = e < 0 ? n : e + 1; continue; }
                if (c == '[') { depth++; i++; continue; }
                if (c == ']') { depth--; i++; continue; }
                if (c == '/' && depth == 1)
                {
                    int j = i + 1;
                    while (j < n && !IsDelim(dict[j])) j++;
                    string name = dict.Substring(i + 1, j - i - 1);
                    int vs = j;
                    while (vs < n && char.IsWhiteSpace(dict[vs])) vs++;
                    int ve = ValueEnd(dict, vs);
                    if (name == key) return dict.Substring(vs, ve - vs).Trim();
                    i = ve;
                    continue;
                }
                i++;
            }
            return null;
        }

        static bool IsDelim(char c)
        {
            return char.IsWhiteSpace(c) || c == '/' || c == '[' || c == ']' || c == '<' || c == '>' || c == '(' || c == ')' || c == '{' || c == '}' || c == '%';
        }

        static int SkipString(string s, int i)
        {
            int depth = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\') { i++; continue; }
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) return i + 1; }
            }
            return s.Length;
        }

        static int ValueEnd(string s, int i)
        {
            int n = s.Length;
            if (i >= n) return n;
            char c = s[i];
            if (c == '(') return SkipString(s, i);
            if (c == '[' || (c == '<' && i + 1 < n && s[i + 1] == '<'))
            {
                int depth = 0;
                while (i < n)
                {
                    char x = s[i];
                    if (x == '(') { i = SkipString(s, i); continue; }
                    if (x == '<' && i + 1 < n && s[i + 1] == '<') { depth++; i += 2; continue; }
                    if (x == '>' && i + 1 < n && s[i + 1] == '>') { depth--; i += 2; if (depth == 0) return i; continue; }
                    if (x == '<') { int e = s.IndexOf('>', i); i = e < 0 ? n : e + 1; if (depth == 0) return i; continue; }
                    if (x == '[') depth++;
                    if (x == ']') { depth--; if (depth == 0) return i + 1; }
                    i++;
                }
                return n;
            }
            if (c == '<') { int e = s.IndexOf('>', i); return e < 0 ? n : e + 1; }
            if (c == '/') { int j = i + 1; while (j < n && !IsDelim(s[j])) j++; return j; }
            Match m = Regex.Match(s.Substring(i), @"^(\d+\s+\d+\s+R\b|[+\-]?[\d.]+|true|false|null)");
            if (m.Success) return i + m.Length;
            int k = i; while (k < n && !IsDelim(s[k])) k++;
            return Math.Max(k, i + 1);
        }
    }
}
