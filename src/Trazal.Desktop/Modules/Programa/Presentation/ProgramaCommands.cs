using Trazal.Desktop.Modules.Programa.Presentation;

namespace Trazal.Desktop
{
    /// <summary>
    /// Partial de ShellViewModel para el módulo Programa global.
    /// Agrega ShowPrograma, IsProgramaSection y OpenPrograma al shell,
    /// siguiendo el mismo patrón que BudgetCommands.cs.
    /// </summary>
    public sealed partial class ShellViewModel
    {
        public System.Windows.Input.ICommand ShowPrograma { get; private set; }
        public bool IsProgramaSection => Page is ProgramaViewModel;

        internal void ConfigurePrograma()
        {
            ShowPrograma = new ActionCommand(_ => Safe(OpenPrograma));
        }

        private void OpenPrograma()
        {
            Page = new ProgramaViewModel(this, store);
            Location = "Programa de obra";
        }
    }
}
