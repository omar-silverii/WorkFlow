using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.WebForms.Manifestos
{
    // Manifesto del proceso publicado
    public class ProcesoManifest
    {
        public string Clave { get; set; }
        public string Nombre { get; set; }
        public List<Etapa> Etapas { get; set; }
        public List<Bandeja> Bandejas { get; set; }
        public Notificaciones Notificaciones { get; set; }
    }

    public class Etapa
    {
        public string Id { get; set; }
        public string Nombre { get; set; }
        public Formulario Form { get; set; }
    }

    public class Formulario
    {
        public List<Campo> Campos { get; set; }
    }

    public class Campo
    {
        // Id lógico (ej: documento.tieneFirma). Para campos simples puede ser "cliente.nombre"
        public string Id { get; set; }

        // Etiqueta visible
        public string Etiqueta { get; set; }

        // Tipos simples: texto, numero, booleano, archivo, lista
        public string Tipo { get; set; }

        // Requerido a nivel UI
        public bool Obligatorio { get; set; }

        // Fuente para listas (opcional)
        public List<string> Opciones { get; set; }
    }

    public class Bandeja
    {
        public string Rol { get; set; }
        public string Nombre { get; set; }
        public List<string> Columnas { get; set; }
    }

    public class Notificaciones
    {
        public string CorreoSinFirma { get; set; }
    }
}
