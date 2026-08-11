using System;
using System.Collections.Generic;

namespace Intranet.WorkflowStudio.WebForms
{
    /// <summary>
    /// FIX84C2Bf: resuelve referencias humanas de usuario contra el catálogo real.
    /// Acepta identidad completa, DisplayName o cuenta corta (parte posterior a \\ o /).
    /// Nunca fabrica dominios ni completa prefijos por intuición.
    /// </summary>
    public static class WfAiUserReferenceResolver
    {
        public static WfAiUserReferenceResolution Resolve(WfAiCatalog catalog, string reference)
        {
            var result = new WfAiUserReferenceResolution
            {
                Input = (reference ?? string.Empty).Trim(),
                Status = WfAiUserReferenceStatus.NotFound
            };

            if (result.Input.Length == 0)
            {
                result.Status = WfAiUserReferenceStatus.Empty;
                return result;
            }

            if (catalog == null || catalog.Users == null || catalog.Users.Count == 0)
                return result;

            // 1) Identidad canónica completa: máxima prioridad.
            List<WfAiUserInfo> matches = Match(catalog.Users, delegate (WfAiUserInfo u)
            {
                return string.Equals((u.UserKey ?? string.Empty).Trim(), result.Input, StringComparison.OrdinalIgnoreCase);
            });
            if (matches.Count > 0) return Complete(result, matches);

            // 2) Nombre visible exacto.
            matches = Match(catalog.Users, delegate (WfAiUserInfo u)
            {
                return string.Equals((u.DisplayName ?? string.Empty).Trim(), result.Input, StringComparison.OrdinalIgnoreCase);
            });
            if (matches.Count > 0) return Complete(result, matches);

            // 3) Cuenta corta exacta: DOMAIN\\USUARIO1 -> USUARIO1.
            matches = Match(catalog.Users, delegate (WfAiUserInfo u)
            {
                return string.Equals(ShortAccountName(u == null ? string.Empty : u.UserKey), result.Input, StringComparison.OrdinalIgnoreCase);
            });
            return Complete(result, matches);
        }

        public static string ShortAccountName(string userKey)
        {
            string value = (userKey ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;

            int slash = Math.Max(value.LastIndexOf('\\'), value.LastIndexOf('/'));
            return slash >= 0 && slash < value.Length - 1 ? value.Substring(slash + 1).Trim() : value;
        }

        private static List<WfAiUserInfo> Match(IEnumerable<WfAiUserInfo> users, Predicate<WfAiUserInfo> predicate)
        {
            var result = new List<WfAiUserInfo>();
            if (users == null || predicate == null) return result;

            foreach (WfAiUserInfo user in users)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.UserKey)) continue;
                if (!predicate(user)) continue;

                bool duplicate = false;
                foreach (WfAiUserInfo existing in result)
                {
                    if (existing != null && string.Equals(existing.UserKey, user.UserKey, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) result.Add(user);
            }
            return result;
        }

        private static WfAiUserReferenceResolution Complete(WfAiUserReferenceResolution result, List<WfAiUserInfo> matches)
        {
            matches = matches ?? new List<WfAiUserInfo>();
            foreach (WfAiUserInfo match in matches)
            {
                if (match == null) continue;
                result.Candidates.Add(new WfAiUserReferenceCandidate
                {
                    UserKey = (match.UserKey ?? string.Empty).Trim(),
                    DisplayName = (match.DisplayName ?? string.Empty).Trim(),
                    ShortName = ShortAccountName(match.UserKey)
                });
            }

            if (result.Candidates.Count == 1)
            {
                result.Status = WfAiUserReferenceStatus.Resolved;
                result.UserKey = result.Candidates[0].UserKey;
                result.DisplayName = result.Candidates[0].DisplayName;
                result.ShortName = result.Candidates[0].ShortName;
            }
            else if (result.Candidates.Count > 1)
            {
                result.Status = WfAiUserReferenceStatus.Ambiguous;
            }
            else
            {
                result.Status = WfAiUserReferenceStatus.NotFound;
            }
            return result;
        }
    }

    public static class WfAiUserReferenceStatus
    {
        public const string Empty = "empty";
        public const string Resolved = "resolved";
        public const string Ambiguous = "ambiguous";
        public const string NotFound = "not_found";
    }

    public class WfAiUserReferenceResolution
    {
        public string Input { get; set; }
        public string Status { get; set; }
        public string UserKey { get; set; }
        public string DisplayName { get; set; }
        public string ShortName { get; set; }
        public List<WfAiUserReferenceCandidate> Candidates { get; set; }

        public bool IsResolved
        {
            get { return string.Equals(Status, WfAiUserReferenceStatus.Resolved, StringComparison.OrdinalIgnoreCase); }
        }

        public WfAiUserReferenceResolution()
        {
            Candidates = new List<WfAiUserReferenceCandidate>();
        }
    }

    public class WfAiUserReferenceCandidate
    {
        public string UserKey { get; set; }
        public string DisplayName { get; set; }
        public string ShortName { get; set; }
    }
}
