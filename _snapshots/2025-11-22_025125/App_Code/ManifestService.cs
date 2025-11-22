using System;
using System.IO;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.WebForms.Manifestos
{

    public static class ManifestService
    {
        public static ProcesoManifest Cargar(string clave)
        {
            var basePath = System.Web.HttpContext.Current.Server.MapPath("~/App_Data/manifests");
            var path = Path.Combine(basePath, clave + ".json");
            if (!File.Exists(path))
                throw new FileNotFoundException("Manifiesto no encontrado: " + path);

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ProcesoManifest>(json);
        }
    }
}
