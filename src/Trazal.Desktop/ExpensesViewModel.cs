using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Trazal.Core;

namespace Trazal.Desktop
{
    public sealed class ExpenseGroup
    {
        public string Category { get; set; }
        public string Subtotal { get; set; }
    }
    public sealed class ExpensesViewModel : Observable
    {
        private readonly List<Expense> all;
        private string category="Todas las categorías";
        public string Title { get; }
        public string[] Categories => new[]{"Todas las categorías"}.Concat(Expense.Categories).ToArray();
        public string Category { get=>category; set { if(Set(ref category,value)){Notify(nameof(Items));Notify(nameof(Subtotal));Notify(nameof(Tax));Notify(nameof(Total));Notify(nameof(Empty));Notify(nameof(Groups));} } }
        public List<Expense> Items => all.Where(e=>Category=="Todas las categorías"||e.Category==Category).ToList();
        public bool Empty => Items.Count==0;
        public string Subtotal => ShellViewModel.Money(Items.Sum(e=>e.Amount));
        public string Tax => ShellViewModel.Money(Items.Sum(e=>e.Tax));
        public string Total => ShellViewModel.Money(Items.Sum(e=>e.Total));
        public List<ExpenseGroup> Groups => Items.GroupBy(e=>e.Category).Select(g=>new ExpenseGroup {Category=g.Key,Subtotal=ShellViewModel.Money(g.Sum(e=>e.Amount))}).ToList();
        public ICommand Budget {get;}
        public ICommand Add {get;}
        public ICommand Edit {get;}
        public ExpensesViewModel(ShellViewModel shell,Work work,List<Expense> entries)
        {all=entries;Title=work.Title;Budget=new ActionCommand(_=>shell.OpenWork(work));Add=new ActionCommand(_=>shell.EditExpense(work,null));Edit=new ActionCommand(e=>shell.EditExpense(work,(Expense)e));}
    }
}
