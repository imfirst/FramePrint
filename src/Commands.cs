using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(FramePrint.Commands))]
[assembly: CommandClass(typeof(FramePrint.Commands))]

namespace FramePrint
{
    public class Commands : IExtensionApplication
    {
        public void Initialize()
        {
            try
            {
                Document d = AcApp.DocumentManager.MdiActiveDocument;
                if (d != null)
                    d.Editor.WriteMessage("\nFramePrint " + Cad.Version + " загружен. Команды: FPRINT — окно печати, FPPDF — быстрый PDF по текущим настройкам.\n");
            }
            catch { }
        }

        public void Terminate() { }

        /// <summary>Главное окно: поиск рамок, таблица печати, PDF, нумерация, ведомость.</summary>
        static MainForm form;

        /// <summary>Действие окна FramePrint, ожидающее выполнения внутри команды FPRUN.</summary>
        public static Action Pending;

        [CommandMethod("FPRINT", CommandFlags.Modal)]
        public void ShowMain()
        {
            if (form != null && !form.IsDisposed)
            {
                if (form.WindowState == System.Windows.Forms.FormWindowState.Minimized)
                    form.WindowState = System.Windows.Forms.FormWindowState.Normal;
                form.Activate();
                return;
            }
            form = new MainForm();
            form.FormClosed += delegate { form = null; Pending = null; };
            AcApp.ShowModelessDialog(form);
        }

        /// <summary>Служебная: выполняет действие окна (печать, поиск, нумерация…) в контексте команды.</summary>
        [CommandMethod("FPRUN", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void RunPending()
        {
            Action a = Pending;
            Pending = null;
            if (a != null) a();
        }

        /// <summary>Без окна: найти все рамки по текущим настройкам и опубликовать PDF.</summary>
        [CommandMethod("FPPDF", CommandFlags.Modal)]
        public void QuickPdf()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                Settings st = Settings.Current;
                PrintTemplate t = st.GetTemplate(st.ActiveTemplate);
                List<FrameRow> rows = Cad.FindFrames(st, t, null);
                ed.WriteMessage("\nНайдено рамок: " + rows.Count + " (шаблон «" + t.Name + "»)");
                if (rows.Count == 0) return;
                ed.WriteMessage("\n" + Cad.Publish(st, rows, true, t.Name) + "\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка: " + ex.Message + "\n");
            }
        }
            /// <summary>Диагностика координат окна печати.</summary>
        [CommandMethod("FPDIAG", CommandFlags.Modal)]
        public void Diag()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            try
            {
                Settings st = Settings.Current;
                PrintTemplate t = st.GetTemplate(st.ActiveTemplate);
                List<FrameRow> rows = Cad.FindFrames(st, t, null);
                ed.WriteMessage("\nFPDIAG: рамок " + rows.Count + Cad.Diagnose(st, rows) + "\n");
            }
            catch (System.Exception ex) { ed.WriteMessage("\nОшибка: " + ex.Message + "\n"); }
        }
    }
}
