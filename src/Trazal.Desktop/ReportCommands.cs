using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed partial class ShellViewModel
    {
        private PreparedRestore pendingRestore;
        public ICommand RestoreBackup { get; private set; }
        public ICommand ManageBackups { get; private set; }
        private void OpenBackupOptions()
        {
            var e=new Editor{Title="Respaldos locales",SaveLabel="Continuar",Help="El respaldo incluye todas las obras, avances, gastos y el perfil. Guárdalo fuera de este equipo para protegerlo ante una falla del disco."};
            e.Add("operation","¿Qué deseas hacer? *",choices:new[]{"Guardar una copia","Restaurar un respaldo"},placeholder:"Selecciona una opción");
            e.Configure(()=>{if(e.Value("operation")=="")throw new ArgumentException("Selecciona una opción.");},()=>{Dialog=null;if(e.Value("operation")=="Guardar una copia")Backup.Execute(null);else RestoreBackup.Execute(null);},()=>Dialog=null,false);Dialog=e;
        }
        public async Task ExportReport(Work work,DateTime cut,bool pdf)
        {
            if(busy)return;
            var picker=new SaveFileDialog{Title="Exportar reporte de obra",Filter=pdf?"Documento PDF (*.pdf)|*.pdf":"Libro Excel (*.xlsx)|*.xlsx",FileName="Trazal-"+work.Id.Substring(0,8)+"-"+cut.ToString("yyyyMMdd")+(pdf?".pdf":".xlsx"),OverwritePrompt=true};
            if(picker.ShowDialog()!=true)return;
            busy=true;CommandManager.InvalidateRequerySuggested();Status="Preparando reporte…";
            try
            {
                // Snapshot captured before yielding to the UI; exports cannot mix edits made while writing.
                var report=WorkReport.Read(store,work.Id,cut);
                await Task.Run(()=>{if(pdf)PdfReport.Write(report,picker.FileName);else ExcelReport.Write(report,picker.FileName);});
                Status="Reporte guardado: "+picker.FileName;
            }
            catch(Exception ex){Status="No se pudo exportar el reporte: "+ex.Message;}
            finally{busy=false;CommandManager.InvalidateRequerySuggested();}
        }
        private async Task ChooseRestore()
        {
            var picker=new OpenFileDialog{Title="Seleccionar respaldo de Trazal",Filter="Respaldo Trazal (*.trazal)|*.trazal",CheckFileExists=true};
            if(picker.ShowDialog()!=true)return;
            busy=true;CommandManager.InvalidateRequerySuggested();Status="Verificando respaldo…";
            try
            {
                pendingRestore?.Dispose();pendingRestore=await Task.Run(()=>store.PrepareRestore(picker.FileName));
                var e=new Editor{Title="Restaurar respaldo",SaveLabel="Restaurar",Help="Reemplaza TODAS las obras, avances, gastos y el perfil local. No combina datos. Se guardará una copia automática del estado actual en la carpeta de datos de Trazal."};
                e.Add("confirm",pendingRestore.SourceName+"\n"+pendingRestore.Summary+"\nPerfil: "+pendingRestore.Owner+"\n\nReemplazará TODAS las obras y el perfil. Se guardará una copia automática de tus datos actuales.\n\nConfirmación *",choices:new[]{"Reemplazar los datos actuales con este respaldo"},placeholder:"Revisa el contenido y confirma");
                e.Configure(()=>{if(string.IsNullOrEmpty(e.Value("confirm")))throw new ArgumentException("Confirma el reemplazo.");},()=>{
                    var saved=store.Restore(pendingRestore);pendingRestore.Dispose();pendingRestore=null;
                    profile=store.GetProfile();Notify(nameof(Welcome));Notify(nameof(Initials));theme=store.GetSetting("theme","Sistema");Theme.Apply(theme);Notify(nameof(ThemeName));Dialog=null;OpenHome();Status="Restauración completa. Copia anterior: "+saved;
                },()=>{pendingRestore?.Dispose();pendingRestore=null;Dialog=null;Status="Restauración cancelada. Tus datos siguen intactos.";},false);
                Dialog=e;Status="Respaldo verificado. Revisa los datos antes de restaurar.";
            }
            catch(Exception ex){Status="No se pudo preparar el respaldo: "+ex.Message;}
            finally{busy=false;CommandManager.InvalidateRequerySuggested();}
        }
    }
}
