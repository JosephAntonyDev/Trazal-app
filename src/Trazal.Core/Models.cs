using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;

namespace Trazal.Core
{
    public sealed class Work
    {
        public static readonly string[] Types = { "Caminos y carreteras", "Agua potable y drenaje", "Escuelas", "Electrificación", "Edificación", "Otra obra civil" };
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public string Scope { get; set; } = "";
        public decimal? Vat { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int ConceptCount { get; set; }
        public decimal Subtotal { get; set; }
        public void Validate()
        {
            Rules.Required(Title, "Título", 180);
            if (!Types.Contains(Type)) throw new ArgumentException("Selecciona un tipo de obra.");
            if ((Scope ?? "").Length > 240) throw new ArgumentException("El alcance admite hasta 240 caracteres.");
            if (Vat.HasValue && (Vat < 0 || Vat > 100)) throw new ArgumentException("El IVA debe estar entre 0 y 100.");
        }
    }
    public sealed class Concept
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string WorkId { get; set; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount => Budget.Round(Quantity * UnitPrice);
        public void Validate()
        {
            Rules.Required(WorkId, "Obra", 32);
            Rules.Required(Code, "Clave", 40);
            Rules.Required(Description, "Descripción", 500);
            Rules.Required(Unit, "Unidad", 30);
            if (Quantity <= 0 || Quantity > 100000000m) throw new ArgumentException("La cantidad debe ser mayor a cero y no exceder 100 millones.");
            if (UnitPrice < 0 || UnitPrice > 100000000m) throw new ArgumentException("El precio debe estar entre cero y 100 millones.");
            if (decimal.Round(Quantity, 4) != Quantity || decimal.Round(UnitPrice, 4) != UnitPrice)
                throw new ArgumentException("Usa como máximo cuatro decimales en cantidad y precio.");
        }
    }
    public sealed class Profile
    {
        public string FirstName { get; set; } = "Joseph";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Company { get; set; } = "";
        public string FullName => (FirstName + " " + LastName).Trim();
        public string Initials => (FirstName.Trim().Substring(0, 1) + (string.IsNullOrWhiteSpace(LastName) ? "" : LastName.Trim().Substring(0, 1))).ToUpperInvariant();
        public void Validate()
        {
            Rules.Required(FirstName, "Nombre", 80);
            if ((LastName ?? "").Length > 100 || (Company ?? "").Length > 140 || (Phone ?? "").Length > 30 || (Email ?? "").Length > 180)
                throw new ArgumentException("Revisa la longitud de los datos de contacto.");
            if (!string.IsNullOrWhiteSpace(Email))
            {
                try { if (new MailAddress(Email).Address != Email) throw new FormatException(); }
                catch (FormatException) { throw new ArgumentException("Escribe un correo electrónico válido."); }
            }
        }
    }
    public static class Rules
    {
        public static void Required(string value, string label, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(label + " es obligatorio.");
            if (value.Trim().Length > max) throw new ArgumentException(label + " supera el límite de caracteres.");
        }
    }
}
