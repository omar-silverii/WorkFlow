using System;
using System.Collections.Generic;
using System.Linq;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// fix51: léxico inicial de frases humanas para el Constructor IA v2.
    ///
    /// Este archivo NO reemplaza todavía a WfAiMlnetProvider.cs. Es la base para mover
    /// sinónimos y variantes de lenguaje humano a una estructura separada, mantenible
    /// y alineada con WfAiNodeCapabilityMap.
    /// </summary>
    public static class WfAiPhraseLexicon
    {
        public const string ConceptDocLoadNotaCredito = "doc.load.nota_credito";
        public const string ConceptConditionIf = "condition.if";
        public const string ConceptConditionAll = "condition.all";
        public const string ConceptConditionAny = "condition.any";
        public const string ConceptConditionEmpty = "condition.empty";
        public const string ConceptConditionNotEmpty = "condition.not_empty";
        public const string ConceptConditionGreaterThan = "condition.greater_than";
        public const string ConceptConditionNoSupera = "condition.no_supera";
        public const string ConceptHumanTask = "human.task";
        public const string ConceptHumanOutcomeApto = "human.outcome.apto";
        public const string ConceptHumanOutcomeNoApto = "human.outcome.no_apto";
        public const string ConceptNotify = "util.notify";
        public const string ConceptLoggerInfo = "util.logger.info";
        public const string ConceptLoggerWarn = "util.logger.warn";
        public const string ConceptEnd = "util.end";
        public const string ConceptElse = "branch.else";

        public static List<WfAiPhrasePattern> Build()
        {
            var list = new List<WfAiPhrasePattern>();

            Add(list, ConceptDocLoadNotaCredito, "doc.load", 1,
                "cargar nota de credito", "leer nota de credito", "procesar nota de credito", "recibir nota de credito", "tomar una nc", "cargar nc");

            Add(list, ConceptConditionIf, "control.if", 1,
                "si", "cuando", "en caso de", "validar", "controlar", "comprobar", "verificar");

            Add(list, ConceptConditionAll, "control.if", 1,
                "todas las condiciones", "todas las reglas", "deben cumplirse todas", "y tiene", "y el total", "y debe");

            Add(list, ConceptConditionAny, "control.if", 1,
                "cualquiera", "alguna", "una de las", "o falta", "o que", "or", "any");

            Add(list, ConceptConditionEmpty, "control.if", 1,
                "falta", "no tiene", "esta vacio", "vacio", "sin dato", "no informado", "no esta informado");

            Add(list, ConceptConditionNotEmpty, "control.if", 1,
                "tiene", "esta informado", "informado", "no esta vacio", "con dato");

            Add(list, ConceptConditionGreaterThan, "control.if", 1,
                "mayor a", "mayor que", "supera", "excede", "por encima de", "mas de");

            Add(list, ConceptConditionNoSupera, "control.if", 1,
                "no supera", "no excede", "no pasa de", "menor o igual", "hasta");

            Add(list, ConceptHumanTask, "human.task", 1,
                "enviar a", "mandar tarea", "asignar a", "que revise", "que apruebe", "para aprobar", "para corregir", "para revisar", "derivar a");

            Add(list, ConceptHumanOutcomeApto, "control.if", 1,
                "si aprueba", "si la aprueba", "si lo aprueba", "si es apto", "si queda apto", "si resulta apto", "apto");

            Add(list, ConceptHumanOutcomeNoApto, "control.if", 1,
                "si rechaza", "si la rechaza", "si lo rechaza", "si no aprueba", "si es no apto", "no apto", "rechazado", "rechazada");

            Add(list, ConceptNotify, "util.notify", 1,
                "notificar", "avisar", "informar", "mandar aviso", "enviar notificacion", "dar aviso");

            Add(list, ConceptLoggerInfo, "util.logger", 1,
                "registrar evento informativo", "registrar un evento informativo", "dejar registro informativo", "logger info", "informativo");

            Add(list, ConceptLoggerWarn, "util.logger", 1,
                "registrar advertencia", "registrar una advertencia", "dejar advertencia", "logger warn", "advertencia", "warning");

            Add(list, ConceptEnd, "util.end", 1,
                "finalizar", "terminar", "fin", "cerrar workflow", "despues finalizar", "y finalizar");

            Add(list, ConceptElse, "control.if", 1,
                "caso contrario", "de lo contrario", "si no", "en caso contrario", "sino");

            return list;
        }

        public static List<WfAiPhrasePattern> FindPatterns(string text)
        {
            var normalized = WfAiPhraseNormalizer.Normalize(text);
            var found = new List<WfAiPhrasePattern>();

            foreach (var pattern in Build())
            {
                if (WfAiPhraseNormalizer.ContainsAny(normalized, pattern.Phrases))
                {
                    found.Add(pattern);
                }
            }

            return found;
        }

        public static List<WfAiPhrasePattern> ForNodeType(string nodeType)
        {
            return Build().Where(p => string.Equals(p.NodeType, nodeType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static void Add(List<WfAiPhrasePattern> list, string concept, string nodeType, int priority, params string[] phrases)
        {
            list.Add(new WfAiPhrasePattern
            {
                Concept = concept,
                NodeType = nodeType,
                Priority = priority,
                Phrases = new List<string>(phrases ?? new string[0])
            });
        }
    }

    public class WfAiPhrasePattern
    {
        public string Concept { get; set; }
        public string NodeType { get; set; }
        public int Priority { get; set; }
        public List<string> Phrases { get; set; }
    }
}
