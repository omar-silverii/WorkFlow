using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// FIX84B2: contexto natural reutilizable para resolver referencias humanas sin exigir
    /// nombres internos de parámetros. Esta primera implementación cubre el dominio de colas
    /// de la muestra FIX84; no ejecuta nodos ni modifica el runtime.
    /// </summary>
    public class WfAiNaturalPhraseContext
    {
        [JsonProperty("sourceText")]
        public string SourceText { get; set; }

        [JsonProperty("queue")]
        public WfAiNaturalQueueContext Queue { get; set; }

        [JsonProperty("inferences")]
        public List<WfAiNaturalInference> Inferences { get; set; }

        public WfAiNaturalPhraseContext()
        {
            SourceText = string.Empty;
            Queue = new WfAiNaturalQueueContext();
            Inferences = new List<WfAiNaturalInference>();
        }

        public static WfAiNaturalPhraseContext Analyze(string sourceText)
        {
            string text = (sourceText ?? string.Empty).Trim();
            var result = new WfAiNaturalPhraseContext { SourceText = text };
            if (text.Length == 0) return result;

            List<QueueMention> mentions = FindQueueMentions(text);
            Match createMatch = Regex.Match(text, @"\bcrear\s+(?:una\s+)?cola\b", RegexOptions.IgnoreCase);
            Match publishMatch = FindPublishMarker(text);
            Match consumeMatch = FindConsumeMarker(text);

            result.Queue.MentionsCreateQueue = createMatch.Success;
            result.Queue.CreateQueueIndex = createMatch.Success ? createMatch.Index : -1;
            if (createMatch.Success)
            {
                int createEnd = publishMatch.Success ? publishMatch.Index : text.Length;
                QueueMention created = FindFirstMentionAfter(mentions, createMatch.Index, createEnd);
                if (created != null) result.Queue.CreateQueueName = created.Name;
            }

            if (publishMatch.Success)
            {
                result.Queue.WantsPublish = true;
                result.Queue.PublishIndex = publishMatch.Index;

                QueueMention explicitAfter = FindFirstMentionAfter(mentions, publishMatch.Index + publishMatch.Length, NextRelevantBoundary(text, publishMatch.Index + publishMatch.Length));
                QueueMention prior = FindLastMentionBefore(mentions, publishMatch.Index);
                QueueMention chosen = explicitAfter ?? prior;

                if (chosen != null)
                {
                    result.Queue.PublishQueue = chosen.Name;
                    result.Queue.PublishUsesContextQueue = explicitAfter == null && prior != null;
                }

                string publishSegment = SliceToBoundary(text, publishMatch.Index + publishMatch.Length);
                result.Queue.PublishHasTechnicalPayloadWord = Regex.IsMatch(publishSegment, @"\bpayload\b", RegexOptions.IgnoreCase);

                if (!result.Queue.PublishHasTechnicalPayloadWord)
                {
                    int contentStart = publishMatch.Index + publishMatch.Length;
                    if (explicitAfter != null && explicitAfter.Index >= contentStart)
                        contentStart = explicitAfter.Index + explicitAfter.Length;

                    result.Queue.PublishContent = ExtractNaturalPublishContent(text, contentStart);
                    result.Queue.PublishContentInferredByPosition = !string.IsNullOrWhiteSpace(result.Queue.PublishContent);
                }
            }

            if (consumeMatch.Success)
            {
                result.Queue.WantsConsume = true;
                result.Queue.ConsumeIndex = consumeMatch.Index;

                QueueMention explicitAfter = FindFirstMentionAfter(mentions, consumeMatch.Index + consumeMatch.Length, NextRelevantBoundary(text, consumeMatch.Index + consumeMatch.Length));
                QueueMention prior = FindLastMentionBefore(mentions, consumeMatch.Index);
                QueueMention chosen = explicitAfter ?? prior;
                if (chosen != null)
                {
                    result.Queue.ConsumeQueue = chosen.Name;
                    result.Queue.ConsumeUsesContextQueue = explicitAfter == null && prior != null;
                }

                string consumeSegment = SliceToBoundary(text, consumeMatch.Index);
                Match count = Regex.Match(consumeSegment, @"\b(?<n>\d+)\s+mensajes?\b", RegexOptions.IgnoreCase);
                int n;
                result.Queue.ConsumeCount = count.Success && int.TryParse(count.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0 ? n : 1;
            }

            QueueMention first = mentions.Count > 0 ? mentions[0] : null;
            if (first != null) result.Queue.FirstQueueName = first.Name;

            if (result.Queue.MentionsCreateQueue && result.Queue.WantsPublish
                && result.Queue.CreateQueueIndex >= 0 && result.Queue.CreateQueueIndex < result.Queue.PublishIndex
                && !string.IsNullOrWhiteSpace(result.Queue.CreateQueueName)
                && string.Equals(result.Queue.CreateQueueName, result.Queue.PublishQueue, StringComparison.OrdinalIgnoreCase))
            {
                result.Queue.CreateMeaningResolvedByOperationalContext = true;
            }

            if (result.Queue.PublishUsesContextQueue && !string.IsNullOrWhiteSpace(result.Queue.PublishQueue))
            {
                result.Inferences.Add(new WfAiNaturalInference
                {
                    Key = "queue.publish.reference",
                    Label = "Cola usada para publicar",
                    Value = result.Queue.PublishQueue,
                    Explanation = "Se reutilizó la cola mencionada antes; no hace falta repetir su nombre al decir «y publicar»."
                });
            }

            if (result.Queue.PublishContentInferredByPosition && !string.IsNullOrWhiteSpace(result.Queue.PublishContent))
            {
                result.Inferences.Add(new WfAiNaturalInference
                {
                    Key = "queue.publish.content",
                    Label = "Contenido a publicar",
                    Value = result.Queue.PublishContent,
                    Explanation = "El texto indicado después de «publicar» se interpretó como contenido del mensaje; no hace falta conocer el nombre interno de ese dato."
                });
            }

            if (result.Queue.ConsumeUsesContextQueue && !string.IsNullOrWhiteSpace(result.Queue.ConsumeQueue))
            {
                result.Inferences.Add(new WfAiNaturalInference
                {
                    Key = "queue.consume.reference",
                    Label = "Cola usada para leer",
                    Value = result.Queue.ConsumeQueue,
                    Explanation = "La lectura continúa sobre la misma cola mientras no se nombre otra."
                });
            }

            if (result.Queue.CreateMeaningResolvedByOperationalContext)
            {
                result.Inferences.Add(new WfAiNaturalInference
                {
                    Key = "queue.create.operational",
                    Label = "Sentido de «crear una cola»",
                    Value = "Usar la cola dentro del workflow",
                    Explanation = "La acción «y publicar» aclara el contexto operativo; no se interpreta como aprovisionamiento de infraestructura."
                });
            }

            return result;
        }

        public bool ResolvesAmbiguity(string resolverKey, string queueName)
        {
            if (string.IsNullOrWhiteSpace(resolverKey)) return false;
            if (string.Equals(resolverKey, "queue.create.followed_by_publish", StringComparison.OrdinalIgnoreCase))
            {
                if (!Queue.CreateMeaningResolvedByOperationalContext) return false;
                if (string.IsNullOrWhiteSpace(queueName)) return true;
                return string.Equals(queueName, Queue.PublishQueue, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static Match FindPublishMarker(string text)
        {
            Match direct = Regex.Match(text, @"\b(?:publicar|publica|publicá|encolar)\b", RegexOptions.IgnoreCase);
            Match routed = Regex.Match(text, @"\b(?:mandar|enviar)\s+(?:un\s+mensaje\s+)?a\s+(?:la\s+)?cola\b", RegexOptions.IgnoreCase);
            if (!direct.Success) return routed;
            if (!routed.Success) return direct;
            return direct.Index <= routed.Index ? direct : routed;
        }

        private static Match FindConsumeMarker(string text)
        {
            return Regex.Match(text,
                @"\bconsumir\b|\bleer\s+(?:(?:un|el)\s+|\d+\s+)?mensajes?\b|\btomar\s+(?:(?:un|el)\s+|\d+\s+)?mensajes?\b|\b(?:leer|tomar)\s+de\s+(?:la\s+)?cola\b",
                RegexOptions.IgnoreCase);
        }

        private static List<QueueMention> FindQueueMentions(string text)
        {
            var result = new List<QueueMention>();
            MatchCollection matches = Regex.Matches(text,
                @"\b(?:una\s+|la\s+)?(?:cola|queue)\s+(?:llamada\s+)?(?<name>[A-Za-z0-9_\-\.]+)",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (!match.Success) continue;
                string name = (match.Groups["name"].Value ?? string.Empty).Trim().TrimEnd('.', ',', ';', ':');
                if (string.IsNullOrWhiteSpace(name) || ReservedQueueWord(name)) continue;
                result.Add(new QueueMention { Index = match.Index, Length = match.Length, Name = name });
            }
            return result;
        }

        private static bool ReservedQueueWord(string value)
        {
            string v = (value ?? string.Empty).Trim().ToLowerInvariant();
            return v == "llamada" || v == "existente" || v == "nueva" || v == "que" || v == "para" || v == "con";
        }

        private static QueueMention FindLastMentionBefore(List<QueueMention> mentions, int index)
        {
            QueueMention best = null;
            foreach (QueueMention item in mentions)
            {
                if (item.Index >= index) break;
                best = item;
            }
            return best;
        }

        private static QueueMention FindFirstMentionAfter(List<QueueMention> mentions, int start, int end)
        {
            foreach (QueueMention item in mentions)
            {
                if (item.Index < start) continue;
                if (end >= 0 && item.Index >= end) break;
                return item;
            }
            return null;
        }

        private static string ExtractNaturalPublishContent(string text, int start)
        {
            if (string.IsNullOrWhiteSpace(text) || start < 0 || start >= text.Length) return string.Empty;
            int end = NextRelevantBoundary(text, start);
            if (end < 0) end = text.Length;
            string content = text.Substring(start, end - start).Trim();

            content = Regex.Replace(content, @"^[\s,:;\-]+", string.Empty);
            content = Regex.Replace(content, @"^(?:con\s+)?(?:el\s+)?mensaje\s+", string.Empty, RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"^con\s+", string.Empty, RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"^que\s+diga\s+", string.Empty, RegexOptions.IgnoreCase);
            content = content.Trim().TrimEnd('.', ';', ',').Trim();
            return content;
        }

        private static int NextRelevantBoundary(string text, int start)
        {
            if (string.IsNullOrWhiteSpace(text) || start < 0 || start >= text.Length) return -1;
            string tail = text.Substring(start);

            Match connector = Regex.Match(tail,
                @"(?:,\s*|\s+)(?:y\s+)?(?:luego|despues|después|finalmente)\s+(?=(?:consumir|leer|tomar|registrar|notificar|finalizar|terminar|crear\s+una\s+tarea|mandar\s+una\s+tarea|enviar\s+una\s+tarea)\b)",
                RegexOptions.IgnoreCase);
            int best = connector.Success ? start + connector.Index : -1;

            Match sentenceMatch = Regex.Match(text.Substring(start), @"\.(?=\s|$)", RegexOptions.Singleline);
            int sentence = sentenceMatch.Success ? start + sentenceMatch.Index : -1;
            if (sentence >= 0 && (best < 0 || sentence < best)) best = sentence;
            int semicolon = text.IndexOf(';', start);
            if (semicolon >= 0 && (best < 0 || semicolon < best)) best = semicolon;
            int newline = text.IndexOf('\n', start);
            if (newline >= 0 && (best < 0 || newline < best)) best = newline;
            return best;
        }

        private static string SliceToBoundary(string text, int start)
        {
            if (string.IsNullOrWhiteSpace(text) || start < 0 || start >= text.Length) return string.Empty;
            int end = NextRelevantBoundary(text, start);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start);
        }

        private class QueueMention
        {
            public int Index { get; set; }
            public int Length { get; set; }
            public string Name { get; set; }
        }
    }

    public class WfAiNaturalQueueContext
    {
        [JsonProperty("firstQueueName")]
        public string FirstQueueName { get; set; }
        [JsonProperty("mentionsCreateQueue")]
        public bool MentionsCreateQueue { get; set; }
        [JsonProperty("createQueueIndex")]
        public int CreateQueueIndex { get; set; }
        [JsonProperty("createQueueName")]
        public string CreateQueueName { get; set; }
        [JsonProperty("wantsPublish")]
        public bool WantsPublish { get; set; }
        [JsonProperty("publishIndex")]
        public int PublishIndex { get; set; }
        [JsonProperty("publishQueue")]
        public string PublishQueue { get; set; }
        [JsonProperty("publishUsesContextQueue")]
        public bool PublishUsesContextQueue { get; set; }
        [JsonProperty("publishContent")]
        public string PublishContent { get; set; }
        [JsonProperty("publishContentInferredByPosition")]
        public bool PublishContentInferredByPosition { get; set; }
        [JsonProperty("publishHasTechnicalPayloadWord")]
        public bool PublishHasTechnicalPayloadWord { get; set; }
        [JsonProperty("wantsConsume")]
        public bool WantsConsume { get; set; }
        [JsonProperty("consumeIndex")]
        public int ConsumeIndex { get; set; }
        [JsonProperty("consumeQueue")]
        public string ConsumeQueue { get; set; }
        [JsonProperty("consumeUsesContextQueue")]
        public bool ConsumeUsesContextQueue { get; set; }
        [JsonProperty("consumeCount")]
        public int ConsumeCount { get; set; }
        [JsonProperty("createMeaningResolvedByOperationalContext")]
        public bool CreateMeaningResolvedByOperationalContext { get; set; }

        public WfAiNaturalQueueContext()
        {
            FirstQueueName = string.Empty;
            CreateQueueIndex = -1;
            CreateQueueName = string.Empty;
            PublishIndex = -1;
            PublishQueue = string.Empty;
            PublishContent = string.Empty;
            ConsumeIndex = -1;
            ConsumeQueue = string.Empty;
            ConsumeCount = 1;
        }
    }

    public class WfAiNaturalInference
    {
        [JsonProperty("key")]
        public string Key { get; set; }
        [JsonProperty("label")]
        public string Label { get; set; }
        [JsonProperty("value")]
        public string Value { get; set; }
        [JsonProperty("explanation")]
        public string Explanation { get; set; }
    }
}
