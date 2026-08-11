// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// fix39c: corrige render recursivo de ramas anidadas sobre fix39b.
// fix39b: Constructor IA en 3 columnas, rama activa visual y datos clickeables sin tocar motor.
// fix41: limpieza defensiva de texto residual del Constructor IA antes de armar/interpretar frase.
// fix69b: importa frase enviada desde WF_AiRegression.aspx (?aiPhrase=...) al textarea del Constructor IA.
// fix70: agrega verificación dinámica/transitoria de frase sin escribir el banco de regresión ni tocar canvas.
// fix71: fallback amigable de desambiguación para frases incompletas o ambiguas.
// fix71b: al elegir una respuesta, la tarjeta queda resuelta y no se repite; si aún hay error, se propone frase consolidada.
// fix71c: el fallback no repite frase consolidada ya usada, no permite interpretar con errores y ofrece resolución concreta para rama NO/datos faltantes.
// fix71d: cuando la frase libre queda trabada, el fallback termina en una acción válida: convertir a plan guiado estructurado o abrir paso a paso.
// fix71e: el plan guiado requiere confirmación explícita y muestra un resumen funcional antes de aplicar al canvas.
// fix71f: el fallback no termina en un botón genérico; muestra resolución accionable en el mismo lugar del error.
// fix72: cierra la secuencia Verificar → Resolver → Revisar → Confirmar → Aplicar, evita mezclar planes parciales y ata la confirmación al plan exacto.
// fix73: presenta la interpretación como borrador visual sobre el canvas, concentra una duda por vez y convierte la propuesta en workflow con una sola confirmación.
// fix73b: conserva un resumen simple de las decisiones usadas y lo deja disponible en el Inspector después de crear el workflow.
// fix73c: transforma las decisiones de desambiguación en tipos reales de nodo y bloquea cualquier plan inconsistente.
// fix73d: elimina preguntas redundantes, prioriza decisiones específicas y muestra qué elección fue reemplazada.
// fix74: consolida un contrato determinístico de plan, valida borrador/proyección/canvas y bloquea cualquier divergencia.
// fix74b: genera y valida la explicación textual desde los parámetros reales de cada nodo y rama.
// fix83b: payload guiado para colas, campos navegables después de consumir y soporte de objetos en Logger.
// fix83c: genera el contrato fix74b antes de Crear workflow desde Verificar frase y amplía frases naturales de colas.
// fix84b: diálogo contractual genérico; las respuestas se envían estructuradas sin reescribir la frase original.
// FIX84C2Bg2b: advertencia explícita = Logger/Warn, preserva contexto de rama y valida borrador funcional contra proyección funcional.
// FIX84C2Bg2c: Paso a paso conserva APTO/NO APTO como estructura; no reinterpreta la frase descriptiva y colapsa Fin redundante de rama contra Fin común.
// FIX84C2Bg2d: los valores ya elegidos en Paso a paso son datos declarativos; el fallback no reinterpreta palabras dentro de mensajes/títulos/descripciones como nuevas intenciones.
(function () {
    var lastPlan = null;
    var importedRegressionPhrase = false;
    var aiClarificationOptions = [];
    var aiRewriteOptions = [];
    var aiFallbackResolved = {};
    var aiFallbackResolvedItems = [];
    var aiFallbackBaseKey = "";
    var aiFallbackAcceptedRewriteKey = "";
    var aiGuidedPlanConfirmed = false;
    var aiGuidedConfirmedPlanKey = "";
    var lastAssistantResult = null;
    var aiVisualDraftSelectedIssueKey = '';
    var aiVisualDraftLastQuestions = [];
    var aiVisualDraftActive = false;
    var aiAppliedDecisionSummary = null;
    var aiFix74SelfTestResult = null;
    var aiFix74LastContractCheck = null;
    var aiFix84bAnswers = {};
    var aiFix84bFingerprint = '';
    var aiFix84bBasePhraseKey = '';

    function $(id) { return document.getElementById(id); }

    function htmlEncode(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function currentWorkflowJson() {
        try {
            if (window.WF_getJson) return window.WF_getJson();
            if (window.buildWorkflow) return JSON.stringify(window.buildWorkflow());
        } catch (e) { }
        return '';
    }

    function setStatus(text, css) {
        var el = $('wfAiStatus');
        if (!el) return;
        el.className = 'wf-ai-status ' + (css || '');
        el.textContent = text || '';
    }


    // ------------------------------------------------------------
    // fix72: confirmación vinculada al contenido exacto del plan
    // ------------------------------------------------------------
    function guidedPlanConfirmationKey(plan) {
        if (!plan) return '';
        try {
            return JSON.stringify({
                actions: plan.actions || [],
                proposedConnections: plan.proposedConnections || [],
                branchPlan: plan.branchPlan || null,
                missingData: plan.missingData || [],
                contractFingerprint: plan.contract && plan.contract.fingerprint || ''
            });
        } catch (e) {
            return '';
        }
    }

    function resetGuidedPlanConfirmation() {
        aiGuidedPlanConfirmed = false;
        aiGuidedConfirmedPlanKey = '';
    }

    function confirmGuidedPlan(plan) {
        var key = guidedPlanConfirmationKey(plan);
        aiGuidedPlanConfirmed = !!key;
        aiGuidedConfirmedPlanKey = key;
        return aiGuidedPlanConfirmed;
    }

    function guidedPlanIsConfirmed(plan) {
        var key = guidedPlanConfirmationKey(plan);
        return !!(aiGuidedPlanConfirmed && key && aiGuidedConfirmedPlanKey === key);
    }

    // ------------------------------------------------------------
    // Constructor guiado incremental
    // ------------------------------------------------------------
    var guideCatalog = {
        roles: [],
        users: [],
        docTipos: [],
        workflowDefs: [],
        fields: [],
        loaded: false
    };

    var guideSteps = [];
    var guideSeq = 1;
    var editingStepIndex = -1;
    var queuePayloadLastValueInput = null;

    // fix39b: destino visual activo del Constructor IA.
    // Permite hacer clic en SI/NO o APTO/NO APTO para que los próximos pasos
    // se agreguen en esa rama, sin depender de combos largos.
    var guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };

    function normalizeKey(v) {
        return String(v == null ? '' : v).trim().toUpperCase();
    }

    function selectedText(id) {
        var el = $(id);
        if (!el) return '';
        return String(el.value || '').trim();
    }

    function roleKey(item) {
        return String((item && (item.RolKey || item.rolKey || item.key)) || '').trim();
    }

    function roleLabel(item) {
        if (!item) return '';
        var key = roleKey(item);
        var name = String(item.Nombre || item.nombre || '').trim();
        if (key && name) return key + ' - ' + name;
        return key || name;
    }

    function userKey(item) {
        return String((item && (item.userKey || item.UserKey || item.key || item.Usuario || item.usuario)) || '').trim();
    }

    function userLabel(item) {
        if (!item) return '';
        var key = userKey(item);
        var name = String(item.displayName || item.DisplayName || item.nombre || item.Nombre || '').trim();
        if (key && name && normalizeKey(key) !== normalizeKey(name)) return key + ' - ' + name;
        return key || name;
    }

    function docCode(item) {
        return String((item && (item.codigo || item.Codigo || item.DocTipoCodigo || item.docTipoCodigo)) || '').trim();
    }

    function docLabel(item) {
        if (!item) return '';
        var codigo = docCode(item);
        var nombre = String(item.nombre || item.Nombre || '').trim();
        if (codigo && nombre) return codigo + ' - ' + nombre;
        return codigo || nombre;
    }

    function workflowDefKey(item) {
        return String((item && (item.key || item.Key || item.ref || item.Ref)) || '').trim();
    }

    function workflowDefLabel(item) {
        if (!item) return '';
        var key = workflowDefKey(item);
        var nombre = String((item && (item.nombre || item.Nombre || item.name || item.Name)) || '').trim();
        var version = String((item && (item.version || item.Version)) || '').trim();
        var label = key;
        if (nombre) label += ' — ' + nombre;
        if (version && version !== '0') label += ' (v' + version + ')';
        return label || nombre;
    }

    function workflowDefOptions(selectedValue) {
        var selected = normalizeKey(selectedValue);
        var html = '<option value="">— elegir workflow —</option>';
        (guideCatalog.workflowDefs || []).forEach(function (item) {
            var key = workflowDefKey(item);
            if (!key) return;
            html += '<option value="' + htmlEncode(key) + '"' + (selected && normalizeKey(key) === selected ? ' selected' : '') + '>' + htmlEncode(workflowDefLabel(item) || key) + '</option>';
        });
        return html;
    }

    function firstWorkflowDef(preferred) {
        var prefs = preferred || [];
        for (var p = 0; p < prefs.length; p++) {
            var pref = normalizeKey(prefs[p]);
            for (var i = 0; i < guideCatalog.workflowDefs.length; i++) {
                if (normalizeKey(workflowDefKey(guideCatalog.workflowDefs[i])) === pref) return workflowDefKey(guideCatalog.workflowDefs[i]);
            }
        }
        return guideCatalog.workflowDefs.length ? workflowDefKey(guideCatalog.workflowDefs[0]) : '';
    }

    function subflowAliasIsValid(alias) {
        var a = String(alias || '').trim();
        if (!a) return true;
        return /^[A-Za-z_][A-Za-z0-9_]*$/.test(a);
    }

    function subflowDisplayName(step) {
        step = step || {};
        return step.ref || step.workflowRef || '(sin workflow)';
    }


    function optionsHtml(items, valueGetter, textGetter, selectedValue) {
        var html = '';
        var selected = normalizeKey(selectedValue);
        (items || []).forEach(function (item) {
            var value = String(valueGetter(item) || '').trim();
            if (!value) return;
            var sel = selected && normalizeKey(value) === selected ? ' selected' : '';
            html += '<option value="' + htmlEncode(value) + '"' + sel + '>' + htmlEncode(textGetter(item) || value) + '</option>';
        });
        return html;
    }

    function roleOptions(selectedValue) {
        return optionsHtml(guideCatalog.roles, roleKey, roleLabel, selectedValue);
    }

    function userDatalistOptions() {
        return optionsHtml(guideCatalog.users, userKey, userLabel, null);
    }

    function userInputHtml(id, selectedValue, useDefault) {
        var fallback = useDefault === false ? '' : (firstUser(['OMARD\\OMARD']) || '');
        var value = String(selectedValue || fallback || '').trim();
        return '<input id="' + htmlEncode(id) + '" class="wf-ai-input wf-ai-user-picker" list="wfAiUsersList" value="' + htmlEncode(value) + '" placeholder="Escribí parte del usuario y seleccioná" />' +
            '<datalist id="wfAiUsersList">' + userDatalistOptions() + '</datalist>';
    }

    function docOptions(selectedValue) {
        return optionsHtml(guideCatalog.docTipos, docCode, docLabel, selectedValue);
    }

    function firstRole(preferred) {
        var prefs = preferred || [];
        for (var p = 0; p < prefs.length; p++) {
            var pref = normalizeKey(prefs[p]);
            for (var i = 0; i < guideCatalog.roles.length; i++) {
                if (normalizeKey(roleKey(guideCatalog.roles[i])) === pref) return roleKey(guideCatalog.roles[i]);
            }
        }
        return guideCatalog.roles.length ? roleKey(guideCatalog.roles[0]) : '';
    }

    function firstUser(preferred) {
        var prefs = preferred || [];
        for (var p = 0; p < prefs.length; p++) {
            var pref = normalizeKey(prefs[p]);
            for (var i = 0; i < guideCatalog.users.length; i++) {
                if (normalizeKey(userKey(guideCatalog.users[i])) === pref) return userKey(guideCatalog.users[i]);
            }
        }
        return guideCatalog.users.length ? userKey(guideCatalog.users[0]) : '';
    }

    function resolveUserSelection(value) {
        var raw = String(value || '').trim();
        if (!raw) return '';
        var rawKey = normalizeKey(raw);
        var matches = [];

        for (var i = 0; i < guideCatalog.users.length; i++) {
            var item = guideCatalog.users[i];
            var key = userKey(item);
            var name = String(item && (item.displayName || item.DisplayName || item.nombre || item.Nombre || '') || '').trim();
            var label = userLabel(item);
            if (!key) continue;

            if (normalizeKey(key) === rawKey || normalizeKey(name) === rawKey || normalizeKey(label) === rawKey) return key;

            if (normalizeKey(key).indexOf(rawKey) >= 0 || normalizeKey(name).indexOf(rawKey) >= 0 || normalizeKey(label).indexOf(rawKey) >= 0)
                matches.push(key);
        }

        return matches.length === 1 ? matches[0] : '';
    }

    function firstDoc(preferred) {
        var prefs = preferred || [];
        for (var p = 0; p < prefs.length; p++) {
            var pref = normalizeKey(prefs[p]);
            for (var i = 0; i < guideCatalog.docTipos.length; i++) {
                if (normalizeKey(docCode(guideCatalog.docTipos[i])) === pref) return docCode(guideCatalog.docTipos[i]);
            }
        }
        return guideCatalog.docTipos.length ? docCode(guideCatalog.docTipos[0]) : '';
    }

    function loadGuideCatalogs() {
        var fallbackRoles = [
            { RolKey: 'COMPRAS', Nombre: 'Compras' },
            { RolKey: 'FINANZAS', Nombre: 'Finanzas' },
            { RolKey: 'GERENCIA', Nombre: 'Gerencia' },
            { RolKey: 'DIR_GENERAL', Nombre: 'Dirección General' },
            { RolKey: 'ADM_FIN', Nombre: 'Administración y Finanzas' }
        ];
        var fallbackDocs = [
            { codigo: 'NOTA_CREDITO_ELECTRONICA_AR', nombre: 'Nota de crédito electrónica AFIP', contextPrefix: 'notaCredito' },
            { codigo: 'FACTURA_ELECTRONICA_AR', nombre: 'Factura electrónica AFIP', contextPrefix: 'factura' }
        ];
        var fallbackUsers = [
            { userKey: 'OMARD\\OMARD', displayName: 'OMARD' },
            { userKey: 'OMARD\\USUARIO1', displayName: 'USUARIO1' },
            { userKey: 'OMARD\\USUARIO2', displayName: 'USUARIO2' },
            { userKey: 'OMARD\\USUARIO3', displayName: 'USUARIO3' },
            { userKey: 'OMARD\\USUARIO4', displayName: 'USUARIO4' },
            { userKey: 'OMARD\\USUARIO5', displayName: 'USUARIO5' }
        ];
        var fallbackFields = [
            { path: 'wf.instanceId', label: 'ID de instancia' },
            { path: 'input.filePath', label: 'Ruta de archivo' },
            { path: 'input.text', label: 'Texto extraído' },
            { path: 'input.hasText', label: 'Tiene texto' }
        ];

        guideCatalog.roles = fallbackRoles;
        guideCatalog.users = fallbackUsers;
        guideCatalog.docTipos = fallbackDocs;
        guideCatalog.workflowDefs = [];
        guideCatalog.fields = fallbackFields;
        renderStepFields();
        renderAvailableFields();

        var pRoles = fetch('Api/WF_Roles_List.ashx', { method: 'GET' })
            .then(function (r) { return r.json(); })
            .then(function (items) {
                if (items && items.length) guideCatalog.roles = items;
            })
            .catch(function () { });

        var pDocs = fetch('Api/Generico.ashx?action=doctipo.list', { method: 'GET' })
            .then(function (r) { return r.json(); })
            .then(function (items) {
                if (items && items.length) guideCatalog.docTipos = items;
            })
            .catch(function () { });

        var pCatalog = fetch('Api/WF_AiCatalog.ashx', { method: 'GET' })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                if (!res || !res.ok) return;
                if (res.docTypes && res.docTypes.length) guideCatalog.docTipos = res.docTypes;
                if (res.users && res.users.length) guideCatalog.users = res.users;
                if (res.fields && res.fields.length) guideCatalog.fields = res.fields;
            })
            .catch(function () { });

        var pDefs = fetch('Api/WfDefiniciones.ashx?activo=1', { method: 'GET' })
            .then(function (r) { return r.json(); })
            .then(function (items) {
                if (items && items.length) guideCatalog.workflowDefs = items;
            })
            .catch(function () { });

        Promise.all([pRoles, pDocs, pCatalog, pDefs]).then(function () {
            guideCatalog.loaded = true;
            renderStepFields();
            renderAvailableFields();
        });
    }

    function docPhrase(docTipo) {
        var doc = normalizeKey(docTipo);
        if (doc === 'NOTA_CREDITO_ELECTRONICA_AR') return 'una NC';
        if (doc === 'FACTURA_ELECTRONICA_AR') return 'una factura electrónica';
        if (doc) return 'el documento ' + doc;
        return 'un documento';
    }


    // ------------------------------------------------------------
    // fix23: catálogo de salidas/campos disponibles
    // ------------------------------------------------------------
    function docContextPrefix(docTipo) {
        var code = normalizeKey(docTipo);
        for (var i = 0; i < guideCatalog.docTipos.length; i++) {
            var item = guideCatalog.docTipos[i];
            if (normalizeKey(docCode(item)) === code) {
                var prefix = String(item.ContextPrefix || item.contextPrefix || item.contextPrefix || '').trim();
                if (prefix) return prefix;
            }
        }
        if (code === 'NOTA_CREDITO_ELECTRONICA_AR') return 'notaCredito';
        if (code === 'FACTURA_ELECTRONICA_AR') return 'factura';
        return '';
    }

    function inferFieldType(path, label) {
        var t = normalizeKey((path || '') + ' ' + (label || ''));
        if (t.indexOf('COUNT') >= 0 || t.indexOf('TOTAL') >= 0 || t.indexOf('IMPORTE') >= 0 || t.indexOf('MONTO') >= 0) return 'número';
        if (t.indexOf('OK') >= 0 || t.indexOf('HAS') >= 0 || t.indexOf('SENT') >= 0 || t.indexOf('PERSISTED') >= 0) return 'sí/no';
        if (t.indexOf('FECHA') >= 0 || t.indexOf('DATE') >= 0 || t.indexOf('VENCIMIENTO') >= 0) return 'fecha';
        return 'texto';
    }

    function addAvailableField(list, source, path, label, type, docTipo) {
        path = String(path || '').trim();
        if (!path) return;
        for (var i = 0; i < list.length; i++) {
            if (list[i].path === path) return;
        }
        list.push({
            source: source || 'General',
            path: path,
            label: label || path,
            type: type || inferFieldType(path, label),
            docTipo: docTipo || ''
        });
    }

    function addDocFallbackFields(list, source, docTipo, prefix) {
        if (!prefix) return;
        var base = 'biz.' + prefix + '.';
        addAvailableField(list, source, base + 'numero', 'Número de comprobante', 'texto', docTipo);
        addAvailableField(list, source, base + 'fecha', 'Fecha del comprobante', 'fecha', docTipo);
        addAvailableField(list, source, base + 'cae', 'CAE', 'texto', docTipo);
        addAvailableField(list, source, base + 'caeVencimiento', 'Vencimiento CAE', 'fecha', docTipo);
        addAvailableField(list, source, base + 'total', 'Total del comprobante', 'número', docTipo);
        addAvailableField(list, source, base + 'itemsCount', 'Cantidad de ítems', 'número', docTipo);
        addAvailableField(list, source, base + 'validacionBasicaOk', 'Validación básica OK', 'sí/no', docTipo);
        addAvailableField(list, source, base + 'comprobanteAsociado.numero', 'Comprobante asociado', 'texto', docTipo);
        addAvailableField(list, source, base + 'emisor.cuit', 'CUIT emisor', 'texto', docTipo);
        addAvailableField(list, source, base + 'emisor.razonSocial', 'Razón social emisor', 'texto', docTipo);
        addAvailableField(list, source, base + 'receptor.cuit', 'CUIT receptor', 'texto', docTipo);
        addAvailableField(list, source, base + 'receptor.razonSocial', 'Razón social receptor', 'texto', docTipo);
    }

    function createStepTitleForAvailableFields(step) {
        // Título seguro para agrupar campos disponibles.
        // No debe llamar a conditionInfo(), findAvailableField() ni buildAvailableFields().
        if (!step) return 'Paso';
        if (step.type === 'doc_load') return 'Cargar documento: ' + docPhrase(step.docTipo || '');
        if (step.type === 'condition') return 'Condición';
        if (step.type === 'human_task') return 'Tarea humana: ' + ((step.destType === 'usuario' || step.user) ? (step.user || '(usuario)') : (step.role || '(rol)')) + ' / ' + (step.title || step.purpose || 'tarea');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
        if (step.type === 'http_request') return 'Solicitud HTTP: ' + ((step.method || 'GET') + ' ' + (step.url || '(sin URL)'));
        if (step.type === 'sql_query') return 'Consulta SQL: ' + sqlShortText(step.query || '');
        if (step.type === 'file_read') return 'Archivo: Leer ' + (step.path || '(sin ruta)');
        if (step.type === 'file_write') return 'Archivo: Escribir ' + (step.path || '(sin ruta)');
        if (step.type === 'queue_publish') return 'Cola: Publicar en ' + (step.queue || '(sin cola)');
        if (step.type === 'queue_consume') return 'Cola: Consumir de ' + (step.queue || '(sin cola)');
        if (step.type === 'subflow') return 'Ejecutar workflow: ' + subflowDisplayName(step);
        if (step.type === 'state_set') return stateSetTitle(step);
        if (step.type === 'state_remove') return 'Quitar variable: ' + (step.key || '');
        if (step.type === 'delay') return 'Demora: ' + (step.ms || '1000') + ' ms';
        if (step.type === 'retry') return 'Reintentar: ' + (step.reintentos || '3') + ' reintento(s)';
        if (step.type === 'error_handler') return 'Manejador de Error';
        if (step.type === 'logger') return 'Registrar log: ' + (step.level || 'Info');
        if (step.type === 'end') return 'Finalizar flujo';
        return step.type || 'Paso';
    }

    function buildAvailableFields() {
        var fields = [];
        addAvailableField(fields, 'Entrada del workflow', 'wf.instanceId', 'ID de instancia', 'número');
        addAvailableField(fields, 'Entrada del workflow', 'wf.estado', 'Estado del workflow', 'texto');
        addAvailableField(fields, 'Entrada del workflow', 'input.filePath', 'Ruta del archivo', 'texto');
        addAvailableField(fields, 'Entrada del workflow', 'input.text', 'Texto extraído', 'texto');
        addAvailableField(fields, 'Entrada del workflow', 'input.hasText', 'Tiene texto extraído', 'sí/no');

        guideSteps.forEach(function (step, idx) {
            // fix24c: NO usar createStepTitle(step) acá.
            // createStepTitle(condition) llama a conditionInfo(), conditionInfo() llama a findAvailableField(),
            // y findAvailableField() vuelve a buildAvailableFields(): eso genera recursión infinita al agregar un IF.
            var source = (idx + 1) + '. ' + createStepTitleForAvailableFields(step);
            if (step.type === 'doc_load') {
                var docTipo = step.docTipo || '';
                var prefix = docContextPrefix(docTipo);
                (guideCatalog.fields || []).forEach(function (f) {
                    var path = String(f.Path || f.path || '').trim();
                    var label = String(f.Label || f.label || path).trim();
                    var fd = String(f.DocTipo || f.docTipo || '').trim();
                    if (!path) return;
                    if (fd && normalizeKey(fd) !== normalizeKey(docTipo)) return;
                    if (!fd && prefix && path.indexOf('biz.' + prefix + '.') !== 0) return;
                    addAvailableField(fields, source, path, label, inferFieldType(path, label), docTipo);
                });
                addDocFallbackFields(fields, source, docTipo, prefix);
            } else if (step.type === 'human_task') {
                addAvailableField(fields, source, 'wf.tarea.<nodeId>.resultado', 'Resultado de la tarea humana', 'texto');
                addAvailableField(fields, source, 'wf.tarea.<nodeId>.observaciones', 'Observaciones de la tarea humana', 'texto');
            } else if (step.type === 'email_send') {
                addAvailableField(fields, source, 'email.sent', 'Correo enviado', 'sí/no');
                addAvailableField(fields, source, 'email.lastError', 'Último error de correo', 'texto');
                addAvailableField(fields, source, 'email.lastTo', 'Último destinatario', 'texto');
                addAvailableField(fields, source, 'email.lastSubject', 'Último asunto', 'texto');
            } else if (step.type === 'notify') {
                addAvailableField(fields, source, 'notify.last.id', 'ID de notificación', 'número');
                addAvailableField(fields, source, 'notify.last.persisted', 'Notificación guardada', 'sí/no');
                addAvailableField(fields, source, 'notify.last.destino', 'Destino de notificación', 'texto');
                addAvailableField(fields, source, 'notify.last.error', 'Último error de notificación', 'texto');
            } else if (step.type === 'http_request') {
                addAvailableField(fields, source, 'payload.status', 'Status HTTP', 'número');
                addAvailableField(fields, source, 'payload.body', 'Respuesta HTTP texto', 'texto');
                addAvailableField(fields, source, 'payload.json', 'Respuesta HTTP JSON', 'texto');
            } else if (step.type === 'sql_query') {
                addAvailableField(fields, source, 'sql.rows', 'Filas devueltas SQL', 'texto');
                addAvailableField(fields, source, 'sql.rowCount', 'Cantidad de filas SQL', 'número');
                addAvailableField(fields, source, 'sql.first', 'Primera fila SQL', 'texto');
                addAvailableField(fields, source, 'sql.scalar', 'Primer valor SQL', 'texto');
                addAvailableField(fields, source, 'sql.rowsAffected', 'Filas afectadas SQL', 'número');
                addAvailableField(fields, source, 'sql.error', 'Último error SQL', 'texto');
            } else if (step.type === 'file_read') {
                addAvailableField(fields, source, step.output || 'archivo', 'Contenido leído del archivo', 'texto');
                addAvailableField(fields, source, 'file.read.lastPath', 'Última ruta leída', 'texto');
                addAvailableField(fields, source, 'file.read.lastLength', 'Longitud del archivo leído', 'número');
                addAvailableField(fields, source, 'file.read.lastEncoding', 'Encoding usado al leer', 'texto');
                addAvailableField(fields, source, 'file.read.lastZipMode', 'Compresión detectada al leer', 'texto');
                addAvailableField(fields, source, 'file.read.lastAsJson', 'Archivo leído como JSON', 'sí/no');
                addAvailableField(fields, source, 'file.read.exists', 'Archivo existe', 'sí/no');
                addAvailableField(fields, source, 'file.read.lastError', 'Último error de lectura', 'texto');
            } else if (step.type === 'file_write') {
                addAvailableField(fields, source, 'file.write.lastPath', 'Última ruta escrita', 'texto');
                addAvailableField(fields, source, 'file.write.lastLength', 'Longitud escrita', 'número');
                addAvailableField(fields, source, 'file.write.lastEncoding', 'Encoding usado al escribir', 'texto');
                addAvailableField(fields, source, 'file.write.lastZipMode', 'Modo ZIP al escribir', 'texto');
                addAvailableField(fields, source, 'file.write.lastEntryName', 'Entrada ZIP escrita', 'texto');
                addAvailableField(fields, source, 'file.write.skipped', 'Escritura omitida', 'sí/no');
                addAvailableField(fields, source, 'file.write.lastError', 'Último error de escritura', 'texto');
            } else if (step.type === 'queue_publish') {
                addAvailableField(fields, source, 'queue.last.id', 'ID del mensaje publicado', 'número');
                addAvailableField(fields, source, 'queue.last.queue', 'Cola utilizada', 'texto');
                addAvailableField(fields, source, 'queue.last.correlationId', 'Correlation ID', 'texto');
                addAvailableField(fields, source, 'queue.last.payload', 'Contenido publicado', 'texto');
                queuePayloadAvailablePaths(step).forEach(function (field) {
                    addAvailableField(fields, source, 'queue.last.payload.' + field.path, 'Campo publicado: ' + field.label, field.type);
                });
                addAvailableField(fields, source, 'queue.error', 'Último error de cola', 'texto');
            } else if (step.type === 'queue_consume') {
                addAvailableField(fields, source, 'queue.hasMessage', 'La cola devolvió mensajes', 'sí/no');
                addAvailableField(fields, source, 'queue.messageId', 'ID del primer mensaje consumido', 'número');
                addAvailableField(fields, source, 'queue.message', 'Primer mensaje consumido', 'texto');
                addAvailableField(fields, source, 'queue.messages', 'Mensajes consumidos', 'texto');
                addAvailableField(fields, source, 'payload', 'Contenido del primer mensaje', 'texto');
                var matchingPublish = null;
                for (var qp = idx - 1; qp >= 0; qp--) {
                    if (guideSteps[qp] && guideSteps[qp].type === 'queue_publish' &&
                        normalizeKey(guideSteps[qp].queue) === normalizeKey(step.queue)) {
                        matchingPublish = guideSteps[qp];
                        break;
                    }
                }
                if (matchingPublish) {
                    queuePayloadAvailablePaths(matchingPublish).forEach(function (field) {
                        addAvailableField(fields, source, 'payload.' + field.path, 'Campo del payload: ' + field.label, field.type);
                    });
                }
                addAvailableField(fields, source, 'queue.error', 'Último error de cola', 'texto');
            } else if (step.type === 'subflow') {
                addAvailableField(fields, source, 'subflow.instanceId', 'Instancia hija creada', 'número');
                addAvailableField(fields, source, 'subflow.childState', 'Estado de la instancia hija', 'texto');
                addAvailableField(fields, source, 'subflow.ref', 'Workflow hijo ejecutado', 'texto');
                addAvailableField(fields, source, 'subflow.estado', 'Datos de estado del subflujo', 'texto');
                addAvailableField(fields, source, 'subflow.logs', 'Logs del subflujo', 'texto');
                var alias = String(step.alias || '').trim();
                if (alias && subflowAliasIsValid(alias)) {
                    var baseAlias = 'subflows.' + alias;
                    addAvailableField(fields, source, baseAlias + '.instanceId', 'Instancia hija (' + alias + ')', 'número');
                    addAvailableField(fields, source, baseAlias + '.childState', 'Estado hijo (' + alias + ')', 'texto');
                    addAvailableField(fields, source, baseAlias + '.ref', 'Workflow hijo (' + alias + ')', 'texto');
                    addAvailableField(fields, source, baseAlias + '.estado', 'Datos del subflujo (' + alias + ')', 'texto');
                    addAvailableField(fields, source, baseAlias + '.logs', 'Logs del subflujo (' + alias + ')', 'texto');
                }
            } else if (step.type === 'state_set') {
                var stateKeys = stateSetKeysForStep(step);
                stateKeys.forEach(function (k) {
                    addAvailableField(fields, source, k, 'Variable guardada', inferFieldType(k, k));
                });
                addAvailableField(fields, source, 'state.last.setCount', 'Cantidad de variables guardadas', 'número');
                addAvailableField(fields, source, 'state.last.setKeys', 'Variables guardadas', 'texto');
                addAvailableField(fields, source, 'state.last.nodeId', 'Último nodo de variables', 'texto');
            } else if (step.type === 'state_remove') {
                addAvailableField(fields, source, 'state.last.removeCount', 'Cantidad de variables solicitadas para quitar', 'número');
                addAvailableField(fields, source, 'state.last.removedCount', 'Cantidad de variables quitadas', 'número');
                addAvailableField(fields, source, 'state.last.removeKeys', 'Variables solicitadas para quitar', 'texto');
                addAvailableField(fields, source, 'state.last.nodeId', 'Último nodo de variables', 'texto');
            } else if (step.type === 'error_handler') {
                addAvailableField(fields, source, 'wf.error', 'Hay error marcado', 'sí/no');
                addAvailableField(fields, source, 'wf.error.message', 'Mensaje de error', 'texto');
                addAvailableField(fields, source, 'wf.error.nodeId', 'Nodo que marcó error', 'texto');
                addAvailableField(fields, source, 'wf.error.nodeType', 'Tipo de nodo de error', 'texto');
                addAvailableField(fields, source, 'wf.error.timestamp', 'Fecha/hora del error', 'fecha');
                addAvailableField(fields, source, 'util.error.lastNotify.mensaje', 'Último aviso de error', 'texto');
            } else if (step.type === 'logger') {
                addAvailableField(fields, source, 'logger.last.level', 'Último nivel de log', 'texto');
                addAvailableField(fields, source, 'logger.last.message', 'Último mensaje de log', 'texto');
                addAvailableField(fields, source, 'logger.last.nodeId', 'Último nodo de log', 'texto');
            }
        });

        return fields;
    }


    function fieldsNewestGroupsFirst(fields) {
        // fix24d: para la UX del constructor, mostrar primero los datos producidos
        // por el último paso agregado. Así el usuario no tiene que bajar para encontrar
        // las salidas del nodo que acaba de agregar.
        fields = fields || [];
        var groups = [];
        var current = null;
        fields.forEach(function (f) {
            var source = String((f && f.source) || '').trim() || 'Datos';
            if (!current || current.source !== source) {
                current = { source: source, items: [] };
                groups.push(current);
            }
            current.items.push(f);
        });

        var entrada = [];
        var pasos = [];
        groups.forEach(function (g) {
            if (normalizeKey(g.source) === normalizeKey('Entrada del workflow')) entrada.push(g);
            else pasos.push(g);
        });

        pasos.reverse();
        var ordered = [];
        pasos.concat(entrada).forEach(function (g) {
            g.items.forEach(function (f) { ordered.push(f); });
        });
        return ordered;
    }

    function renderAvailableFields() {
        var box = $('wfAiAvailableFields');
        if (!box) return;
        var fields = fieldsNewestGroupsFirst(buildAvailableFields());
        if (!fields.length) {
            box.innerHTML = '<div class="wf-ai-guide-empty">Todavía no hay datos disponibles.</div>';
            return;
        }

        var html = '<div class="wf-ai-fields-title">Datos disponibles para próximos pasos</div>';
        html += '<div class="wf-ai-fields-help">Se alimenta con la entrada del workflow y con las salidas de los pasos agregados. Se usa para elegir campos en el IF guiado, mensajes, logs y variables.</div>';
        html += '<div class="wf-ai-fields-list">';
        var lastSource = null;
        fields.forEach(function (f) {
            if (f.source !== lastSource) {
                if (lastSource !== null) html += '</div>';
                lastSource = f.source;
                html += '<div class="wf-ai-field-group"><div class="wf-ai-field-source">' + htmlEncode(lastSource) + '</div>';
            }
            html += '<button type="button" class="wf-ai-field-row" title="Cargar ' + htmlEncode(f.path) + ' en el formulario" data-guide-field-path="' + htmlEncode(f.path) + '">';
            html += '<span><strong>' + htmlEncode(f.label || f.path) + '</strong><br><code>' + htmlEncode(f.path) + '</code></span>';
            html += '<span class="wf-ai-field-type">' + htmlEncode(f.type || '') + '</span>';
            html += '</button>';
        });
        if (lastSource !== null) html += '</div>';
        html += '</div>';
        box.innerHTML = html;

        Array.prototype.forEach.call(box.querySelectorAll('[data-guide-field-path]'), function (btn) {
            btn.addEventListener('click', function (ev) {
                ev.preventDefault();
                applyAvailableFieldToCurrentForm(btn.getAttribute('data-guide-field-path'));
            });
        });
    }


    // ------------------------------------------------------------
    // fix24: IF guiado con campos disponibles y operadores por tipo
    // ------------------------------------------------------------
    function findAvailableField(path) {
        path = String(path || '').trim();
        if (!path) return null;
        var fields = buildAvailableFields();
        for (var i = 0; i < fields.length; i++) {
            if (fields[i].path === path) return fields[i];
        }
        return null;
    }

    function defaultConditionField() {
        var fields = fieldsNewestGroupsFirst(buildAvailableFields());
        if (!fields.length) return '';
        for (var i = 0; i < fields.length; i++) {
            var p = String(fields[i].path || '').toLowerCase();
            if (p.indexOf('.total') >= 0) return fields[i].path;
        }
        for (var j = 0; j < fields.length; j++) {
            var p2 = String(fields[j].path || '').toLowerCase();
            if (p2.indexOf('biz.') === 0) return fields[j].path;
        }
        return fields[0].path;
    }

    function availableFieldOptions(selectedValue) {
        var fields = fieldsNewestGroupsFirst(buildAvailableFields());
        var selected = String(selectedValue || defaultConditionField() || '').trim();
        var html = '';
        var lastSource = null;
        fields.forEach(function (f) {
            if (f.source !== lastSource) {
                if (lastSource !== null) html += '</optgroup>';
                lastSource = f.source;
                html += '<optgroup label="' + htmlEncode(lastSource || 'Datos') + '">';
            }
            var label = (f.label || f.path) + ' — ' + f.path;
            html += '<option value="' + htmlEncode(f.path) + '"' + (f.path === selected ? ' selected' : '') + '>' + htmlEncode(label) + '</option>';
        });
        if (lastSource !== null) html += '</optgroup>';
        return html;
    }

    function availableFieldOptionsWithBlank(selectedValue, blankText) {
        var fields = fieldsNewestGroupsFirst(buildAvailableFields());
        var selected = String(selectedValue || '').trim();
        var html = '<option value="">' + htmlEncode(blankText || '— escribir valor manual —') + '</option>';
        var lastSource = null;
        fields.forEach(function (f) {
            if (f.source !== lastSource) {
                if (lastSource !== null) html += '</optgroup>';
                lastSource = f.source;
                html += '<optgroup label="' + htmlEncode(lastSource || 'Datos') + '">';
            }
            var label = (f.label || f.path) + ' — ' + f.path;
            html += '<option value="' + htmlEncode(f.path) + '"' + (f.path === selected ? ' selected' : '') + '>' + htmlEncode(label) + '</option>';
        });
        if (lastSource !== null) html += '</optgroup>';
        return html;
    }

    function defaultQueuePayloadFields() {
        // FIX84C2A: no inventar campos de negocio. El usuario decide el contenido.
        return [];
    }

    function normalizeQueuePayloadFields(fields) {
        var result = [];
        (fields || []).forEach(function (field) {
            if (!field) return;
            var name = String(field.name || '').trim();
            var value = field.value == null ? '' : String(field.value);
            if (!name && !value) return;
            result.push({ name: name, value: value });
        });
        return result;
    }

    function queuePayloadFieldNameIsValid(name) {
        return /^[A-Za-z_][A-Za-z0-9_]*$/.test(String(name || '').trim());
    }

    function queuePayloadFieldsFromObject(obj) {
        if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return [];
        var fields = [];
        var keys = Object.keys(obj);
        for (var i = 0; i < keys.length; i++) {
            var value = obj[keys[i]];
            if (value !== null && typeof value === 'object') return [];
            fields.push({ name: keys[i], value: value == null ? '' : String(value) });
        }
        return fields;
    }

    function queuePayloadFieldsForStep(step) {
        if (!step) return [];
        var direct = normalizeQueuePayloadFields(step.payloadFields || []);
        if (direct.length) return direct;
        var raw = String(step.payloadText || '').trim();
        if (!raw) return [];
        try {
            return queuePayloadFieldsFromObject(JSON.parse(raw));
        } catch (e) {
            return [];
        }
    }

    function queuePayloadObjectFromFields(fields) {
        var payload = {};
        normalizeQueuePayloadFields(fields).forEach(function (field) {
            if (field.name) payload[field.name] = field.value;
        });
        return payload;
    }

    function queuePayloadValueFromStep(step) {
        if (!step) return '';
        var mode = String(step.payloadMode || '').toLowerCase();
        var guidedFields = normalizeQueuePayloadFields(step.payloadFields || []);
        if (mode === 'fields' || guidedFields.length) return queuePayloadObjectFromFields(guidedFields);

        var raw = String(step.payloadText || '').trim();
        if (!raw) return '';
        if (mode === 'simple') return raw;
        try { return JSON.parse(raw); }
        catch (e) { return raw; }
    }

    function queuePayloadTextForStep(step) {
        var value = queuePayloadValueFromStep(step);
        if (typeof value === 'string') return value;
        try { return JSON.stringify(value); }
        catch (e) { return String(value || ''); }
    }

    function queuePayloadTypeForValue(path, value) {
        if (typeof value === 'number') return 'número';
        if (typeof value === 'boolean') return 'sí/no';
        return inferFieldType(path, path);
    }

    function queuePayloadAvailablePaths(step) {
        var value = queuePayloadValueFromStep(step);
        if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
        var result = [];

        function walk(current, prefix, depth) {
            if (!current || typeof current !== 'object' || Array.isArray(current) || depth > 4) return;
            Object.keys(current).forEach(function (key) {
                var child = current[key];
                var path = prefix ? prefix + '.' + key : key;
                if (child && typeof child === 'object' && !Array.isArray(child)) {
                    walk(child, path, depth + 1);
                } else if (!Array.isArray(child)) {
                    result.push({ path: path, label: key, type: queuePayloadTypeForValue(path, child) });
                }
            });
        }

        walk(value, '', 0);
        return result;
    }

    function collectQueuePayloadFieldsFromForm() {
        var fields = [];
        var box = $('wfAiQueuePayloadFields');
        if (!box) return fields;
        Array.prototype.forEach.call(box.querySelectorAll('[data-queue-payload-row]'), function (row) {
            var name = row.querySelector('[data-queue-payload-name]');
            var value = row.querySelector('[data-queue-payload-value]');
            fields.push({
                name: name ? String(name.value || '').trim() : '',
                value: value ? String(value.value || '') : ''
            });
        });
        return fields;
    }

    function updateQueuePayloadPreview() {
        var preview = $('wfAiQueuePayloadPreview');
        if (!preview) return;
        var payload = queuePayloadObjectFromFields(collectQueuePayloadFieldsFromForm());
        preview.textContent = JSON.stringify(payload, null, 2);
    }

    function bindQueuePayloadRow(row) {
        if (!row) return;
        var name = row.querySelector('[data-queue-payload-name]');
        var value = row.querySelector('[data-queue-payload-value]');
        var source = row.querySelector('[data-queue-payload-source]');
        var remove = row.querySelector('[data-queue-payload-remove]');

        if (name) name.addEventListener('input', updateQueuePayloadPreview);
        if (value) {
            value.addEventListener('input', updateQueuePayloadPreview);
            value.addEventListener('focus', function () { queuePayloadLastValueInput = value; });
        }
        if (source) source.addEventListener('change', function () {
            var path = String(source.value || '').trim();
            if (path && value) {
                value.value = '${' + path + '}';
                queuePayloadLastValueInput = value;
                updateQueuePayloadPreview();
            }
        });
        if (remove) remove.addEventListener('click', function (ev) {
            ev.preventDefault();
            if (queuePayloadLastValueInput && row.contains(queuePayloadLastValueInput)) queuePayloadLastValueInput = null;
            row.parentNode.removeChild(row);
            updateQueuePayloadPreview();
        });
    }

    function addQueuePayloadFieldRow(field) {
        var box = $('wfAiQueuePayloadFields');
        if (!box) return;
        field = field || { name: '', value: '' };
        var row = document.createElement('div');
        row.className = 'wf-ai-queue-field-row';
        row.setAttribute('data-queue-payload-row', '1');
        row.innerHTML = '' +
            '<div class="wf-ai-queue-field-head"><strong>Campo del mensaje</strong><button type="button" class="btn wf-ai-queue-remove" data-queue-payload-remove="1">Quitar</button></div>' +
            '<label class="wf-ai-queue-mini-label">Nombre</label>' +
            '<input class="wf-ai-input" data-queue-payload-name="1" value="' + htmlEncode(field.name || '') + '" placeholder="Ej.: origen, documentoId, importe" />' +
            '<label class="wf-ai-queue-mini-label">Valor</label>' +
            '<input class="wf-ai-input" data-queue-payload-value="1" value="' + htmlEncode(field.value == null ? '' : field.value) + '" placeholder="Texto fijo o ${dato.disponible}" />' +
            '<select class="wf-ai-select" data-queue-payload-source="1">' + availableFieldOptionsWithBlank('', '— elegir un dato disponible —') + '</select>';
        box.appendChild(row);
        bindQueuePayloadRow(row);
        updateQueuePayloadPreview();
    }

    function renderQueuePayloadRows(fields) {
        var box = $('wfAiQueuePayloadFields');
        if (!box) return;
        box.innerHTML = '';
        queuePayloadLastValueInput = null;
        var normalized = normalizeQueuePayloadFields(fields || []);
        normalized.forEach(addQueuePayloadFieldRow);
        updateQueuePayloadPreview();
    }

    function syncQueuePayloadModeFields() {
        var mode = $('wfAiStepQueuePayloadMode');
        var simple = $('wfAiQueuePayloadSimpleBox');
        var guided = $('wfAiQueuePayloadGuidedBox');
        var advanced = $('wfAiQueuePayloadAdvancedBox');
        if (!mode || !simple || !guided || !advanced) return;
        var value = String(mode.value || 'simple');
        simple.style.display = value === 'simple' ? '' : 'none';
        guided.style.display = value === 'fields' ? '' : 'none';
        advanced.style.display = value === 'advanced' ? '' : 'none';
        if (value === 'fields') updateQueuePayloadPreview();
    }

    function isValidStatePath(path) {
        var v = String(path || '').trim();
        if (!v) return false;
        if (v.indexOf('${') >= 0 || /\s/.test(v)) return false;
        return /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/.test(v);
    }

    function isTechnicalOutputPath(path) {
        var v = String(path || '').trim().toLowerCase();
        return /^(payload|sql|logger|notify|email|queue|subflow|subflows)\./.test(v);
    }

    function syncStateValueFromField() {
        var sel = $('wfAiStepValueField');
        var val = $('wfAiStepValue');
        if (!sel || !val) return;
        var path = String(sel.value || '').trim();
        if (path) val.value = '${' + path + '}';
    }

    function syncStateSetModeFields() {
        var mode = $('wfAiStepStateMode');
        var simple = $('wfAiStepStateSimpleBox');
        var json = $('wfAiStepStateJsonBox');
        if (!mode || !simple || !json) return;
        var m = String(mode.value || 'simple');
        simple.style.display = m === 'json' ? 'none' : '';
        json.style.display = m === 'json' ? '' : 'none';
    }

    function normalizeFieldType(type) {
        // fix24b: los tipos visibles pueden venir con acentos: "número", "sí/no".
        // No cambiamos normalizeKey global para no afectar búsquedas de roles/docTipos.
        var raw = String(type || 'texto');
        var t = raw;
        try {
            t = raw.normalize('NFD').replace(/[\u0300-\u036f]/g, '');
        } catch (ex) {
            t = raw;
        }
        t = String(t || '').trim().toUpperCase();

        if (t.indexOf('NUM') >= 0 || t.indexOf('NUMBER') >= 0 || t.indexOf('DECIMAL') >= 0 || t.indexOf('INT') >= 0) return 'numero';
        if (t.indexOf('SI') >= 0 || t.indexOf('NO') >= 0 || t.indexOf('BOOL') >= 0 || t.indexOf('VERD') >= 0 || t.indexOf('TRUE') >= 0 || t.indexOf('FALSE') >= 0) return 'booleano';
        if (t.indexOf('FECHA') >= 0 || t.indexOf('DATE') >= 0) return 'fecha';
        return 'texto';
    }

    function conditionOperatorOptions(fieldType, selectedValue) {
        var type = normalizeFieldType(fieldType);
        var selected = selectedValue || '';
        if (selected === '==') selected = '=';
        var items;
        if (type === 'numero') {
            items = [
                ['>', 'mayor que'],
                ['>=', 'mayor o igual que'],
                ['<', 'menor que'],
                ['<=', 'menor o igual que'],
                ['=', 'igual a'],
                ['!=', 'distinto de'],
                ['not_empty', 'está informado'],
                ['empty', 'está vacío']
            ];
        } else if (type === 'booleano') {
            items = [
                ['true', 'es verdadero / sí'],
                ['false', 'es falso / no'],
                ['not_empty', 'está informado'],
                ['empty', 'está vacío']
            ];
        } else if (type === 'fecha') {
            items = [
                ['not_empty', 'está informada'],
                ['empty', 'está vacía'],
                ['>', 'posterior a'],
                ['<', 'anterior a'],
                ['=', 'igual a'],
                ['!=', 'distinta de']
            ];
        } else {
            items = [
                ['not_empty', 'no está vacío'],
                ['empty', 'está vacío'],
                ['=', 'igual a'],
                ['!=', 'distinto de'],
                ['contains', 'contiene'],
                ['not_contains', 'no contiene']
            ];
        }
        var exists = false;
        for (var i = 0; i < items.length; i++) {
            if (items[i][0] === selected) { exists = true; break; }
        }
        if (!selected || !exists) selected = items[0][0];

        return items.map(function (x) {
            return '<option value="' + htmlEncode(x[0]) + '"' + (x[0] === selected ? ' selected' : '') + '>' + htmlEncode(x[1]) + '</option>';
        }).join('');
    }

    function operatorNeedsValue(op) {
        op = String(op || '');
        return !(op === 'not_empty' || op === 'empty' || op === 'exists' || op === 'not_exists' || op === 'true' || op === 'false');
    }

    function operatorPhrase(op) {
        var map = {
            '==': 'igual a',
            '>': 'mayor que',
            '>=': 'mayor o igual que',
            '<': 'menor que',
            '<=': 'menor o igual que',
            '=': 'igual a',
            '!=': 'distinto de',
            'contains': 'contiene',
            'not_contains': 'no contiene',
            'exists': 'existe',
            'not_exists': 'no existe',
            'not_empty': 'no está vacío',
            'empty': 'está vacío',
            'true': 'es verdadero',
            'false': 'es falso'
        };
        return map[String(op || '')] || String(op || '');
    }

    function conditionRulesModeText(mode) {
        mode = String(mode || 'all').toLowerCase();
        return (mode === 'any' || mode === 'or') ? 'cualquiera de las reglas' : 'todas las reglas';
    }

    function normalizeConditionRule(rule) {
        rule = rule || {};
        var fieldPath = String(rule.fieldPath || rule.field || '').trim();
        if (!fieldPath) fieldPath = defaultConditionField();
        var meta = findAvailableField(fieldPath) || {};
        return {
            fieldPath: fieldPath,
            fieldLabel: rule.fieldLabel || meta.label || fieldPath,
            fieldType: rule.fieldType || meta.type || 'texto',
            operator: rule.operator || rule.op || 'not_empty',
            value: rule.value == null ? '' : String(rule.value),
            transform: rule.transform || ''
        };
    }

    function conditionRuleList(step) {
        var src = step && step.rules;
        if (!src || !src.length) return [];
        var out = [];
        for (var i = 0; i < src.length; i++) {
            var r = normalizeConditionRule(src[i]);
            if (r.fieldPath) out.push(r);
        }
        return out;
    }

    function defaultCompoundRule(path) {
        return normalizeConditionRule({ fieldPath: path || defaultConditionField(), operator: 'not_empty', value: '' });
    }

    function conditionRulePhrase(rule) {
        var r = normalizeConditionRule(rule);
        var text = (r.fieldLabel || r.fieldPath) + ' ' + operatorPhrase(r.operator);
        if (operatorNeedsValue(r.operator) && String(r.value || '').trim()) text += ' ' + String(r.value || '').trim();
        return text;
    }

    function compoundRulesText(step) {
        var rules = conditionRuleList(step);
        if (!rules.length) return '';
        var parts = rules.map(conditionRulePhrase);
        return 'validar que ' + conditionRulesModeText(step.rulesMode) + ' se cumplan: ' + parts.join('; ');
    }

    function conditionRuleRowHtml(rule, index) {
        var r = normalizeConditionRule(rule);
        var meta = findAvailableField(r.fieldPath) || { type: r.fieldType || 'texto', path: r.fieldPath };
        var needs = operatorNeedsValue(r.operator);
        var html = '<div class="wf-ai-compound-rule" data-rule-index="' + index + '">';
        html += '<div class="wf-ai-compound-rule-head">Regla ' + (index + 1) + '<button type="button" class="btn wf-ai-compound-remove" data-rule-remove="' + index + '">Quitar</button></div>';
        html += '<div class="wf-ai-guide-row"><label>Campo</label><select id="wfAiRuleField_' + index + '" class="wf-ai-select wf-ai-rule-field" data-rule-index="' + index + '">' + availableFieldOptions(r.fieldPath) + '</select></div>';
        html += '<div class="wf-ai-guide-row"><label>Operador</label><select id="wfAiRuleOperator_' + index + '" class="wf-ai-select wf-ai-rule-operator" data-rule-index="' + index + '">' + conditionOperatorOptions(meta.type, r.operator) + '</select></div>';
        html += '<div class="wf-ai-guide-row wf-ai-rule-value-row" id="wfAiRuleValueRow_' + index + '"' + (needs ? '' : ' style="display:none"') + '><label>Valor</label><input id="wfAiRuleValue_' + index + '" class="wf-ai-input wf-ai-rule-value" value="' + htmlEncode(r.value || '') + '" placeholder="Ej.: 200000, texto, fecha o ${otro.campo}" /></div>';
        html += '<div class="wf-ai-guide-hint" id="wfAiRuleHint_' + index + '">Tipo: ' + htmlEncode(meta.type || r.fieldType || 'texto') + ' · Campo técnico: ' + htmlEncode(meta.path || r.fieldPath || '') + '</div>';
        html += '</div>';
        return html;
    }

    function collectCompoundRulesFromForm() {
        var box = $('wfAiCompoundRules');
        if (!box) return [];
        var rows = box.querySelectorAll('.wf-ai-compound-rule');
        var rules = [];
        Array.prototype.forEach.call(rows, function (row) {
            var idx = row.getAttribute('data-rule-index');
            var field = selectedText('wfAiRuleField_' + idx) || defaultConditionField();
            var meta = findAvailableField(field) || {};
            var op = selectedText('wfAiRuleOperator_' + idx) || 'not_empty';
            var val = selectedText('wfAiRuleValue_' + idx) || '';
            if (!field) return;
            rules.push({
                fieldPath: field,
                fieldLabel: meta.label || field,
                fieldType: meta.type || 'texto',
                operator: op,
                value: val
            });
        });
        return rules;
    }

    function syncCompoundRuleRow(index) {
        var field = $('wfAiRuleField_' + index);
        var op = $('wfAiRuleOperator_' + index);
        var valueRow = $('wfAiRuleValueRow_' + index);
        var valueInput = $('wfAiRuleValue_' + index);
        var hint = $('wfAiRuleHint_' + index);
        if (!field || !op) return;

        var meta = findAvailableField(field.value) || { type: 'texto', label: field.value, path: field.value };
        var oldOp = op.value;
        op.innerHTML = conditionOperatorOptions(meta.type, oldOp);
        if (valueRow) valueRow.style.display = operatorNeedsValue(op.value) ? '' : 'none';
        if (valueInput && !operatorNeedsValue(op.value)) valueInput.value = '';
        if (hint) hint.textContent = 'Tipo: ' + (meta.type || 'texto') + ' · Campo técnico: ' + (meta.path || '');
    }

    function bindCompoundRulesUi() {
        var box = $('wfAiCompoundRules');
        if (!box) return;

        Array.prototype.forEach.call(box.querySelectorAll('.wf-ai-rule-field'), function (sel) {
            sel.addEventListener('change', function () { syncCompoundRuleRow(sel.getAttribute('data-rule-index')); });
        });
        Array.prototype.forEach.call(box.querySelectorAll('.wf-ai-rule-operator'), function (sel) {
            sel.addEventListener('change', function () { syncCompoundRuleRow(sel.getAttribute('data-rule-index')); });
        });
        Array.prototype.forEach.call(box.querySelectorAll('[data-rule-remove]'), function (btn) {
            btn.addEventListener('click', function (ev) {
                ev.preventDefault();
                var idx = parseInt(btn.getAttribute('data-rule-remove'), 10);
                var rules = collectCompoundRulesFromForm();
                if (!isNaN(idx)) rules.splice(idx, 1);
                if (!rules.length) rules.push(defaultCompoundRule());
                renderCompoundRules(rules);
            });
        });
    }

    function renderCompoundRules(rules) {
        var box = $('wfAiCompoundRules');
        if (!box) return;
        rules = (rules && rules.length) ? rules : [defaultCompoundRule()];
        var html = '';
        for (var i = 0; i < rules.length; i++) html += conditionRuleRowHtml(rules[i], i);
        box.innerHTML = html;
        bindCompoundRulesUi();
        for (var j = 0; j < rules.length; j++) syncCompoundRuleRow(j);
    }

    function syncConditionModeFields() {
        var mode = selectedText('wfAiStepConditionMode') || 'simple';
        var simpleBox = $('wfAiStepConditionSimpleBox');
        var compoundBox = $('wfAiStepConditionCompoundBox');
        if (simpleBox) simpleBox.style.display = mode === 'compound' ? 'none' : '';
        if (compoundBox) compoundBox.style.display = mode === 'compound' ? '' : 'none';
        if (mode === 'compound') {
            var box = $('wfAiCompoundRules');
            if (box && !box.querySelector('.wf-ai-compound-rule')) renderCompoundRules([defaultCompoundRule()]);
        }
    }

    function applyAvailableFieldToCompoundRule(path) {
        var rules = collectCompoundRulesFromForm();
        var useReplace = !rules.length || (rules.length === 1 && !String(rules[0].value || '').trim());
        if (useReplace) rules = [defaultCompoundRule(path)];
        else rules.push(defaultCompoundRule(path));
        renderCompoundRules(rules);
        setStatus('Campo agregado a la condición compuesta: ' + path, 'ok');
    }

    function syncConditionFields() {
        var field = $('wfAiStepField');
        var op = $('wfAiStepOperator');
        var valueRow = $('wfAiStepValueRow');
        var valueInput = $('wfAiStepConditionValue');
        var typeHint = $('wfAiStepFieldTypeHint');
        if (!field || !op) return;

        var meta = findAvailableField(field.value) || { type: 'texto', label: field.value, path: field.value };
        var oldOp = op.value;
        op.innerHTML = conditionOperatorOptions(meta.type, oldOp);

        if (valueRow) valueRow.style.display = operatorNeedsValue(op.value) ? '' : 'none';
        if (valueInput && !operatorNeedsValue(op.value)) valueInput.value = '';
        if (typeHint) typeHint.textContent = 'Tipo: ' + (meta.type || 'texto') + ' · Campo técnico: ' + (meta.path || '');
    }

    function conditionInfo(step) {
        var compoundText = compoundRulesText(step);
        if (compoundText) {
            return {
                text: compoundText,
                trueLead: 'si se cumple la condición compuesta',
                falseLead: 'si no se cumple la condición compuesta'
            };
        }

        if (step && step.fieldPath) {
            var meta = findAvailableField(step.fieldPath) || {};
            var label = step.fieldLabel || meta.label || step.fieldPath;
            var op = step.operator || 'not_empty';
            var val = String(step.value || '').trim();
            var text = 'validar que ' + label + ' ' + operatorPhrase(op);
            if (operatorNeedsValue(op) && val) text += ' ' + val;
            return {
                text: text,
                trueLead: 'si se cumple la validación de ' + label,
                falseLead: 'si no se cumple la validación de ' + label
            };
        }

        var cond = String(step.condition || '').trim();
        var amount = String(step.amount || '').trim() || '100000';
        if (cond === 'cae') {
            return {
                text: 'verificar si tiene CAE informado',
                trueLead: 'si tiene CAE informado',
                falseLead: 'si no tiene CAE informado'
            };
        }
        return {
            text: 'verificar si el total supera ' + amount,
            trueLead: 'si supera ' + amount,
            falseLead: 'si no supera ' + amount
        };
    }

    function branchPrefix(branch, ctx, step) {
        branch = String(branch || 'then');
        step = step || {};

        if (branch === 'if_cond_true' || branch === 'if_cond_false') {
            var cond = null;
            if (step.branchSourceId && ctx.conditionsById) cond = ctx.conditionsById[step.branchSourceId];
            if (!cond) cond = ctx.lastCondition;
            if (cond) return (branch === 'if_cond_true' ? cond.trueLead : cond.falseLead) + ' ';
        }

        if (branch === 'if_task_ok' || branch === 'if_task_reject') {
            var taskRole = '';
            if (step.branchSourceId && ctx.tasksById && ctx.tasksById[step.branchSourceId]) taskRole = ctx.tasksById[step.branchSourceId].role;
            if (!taskRole) taskRole = ctx.lastTaskRole;
            if (taskRole) return 'si la tarea de ' + taskRole + (branch === 'if_task_ok' ? ' queda apta ' : ' queda no apta ');
            return branch === 'if_task_ok' ? 'si la tarea queda apta ' : 'si la tarea queda no apta ';
        }

        return '';
    }

    function findLastStepIdByType(typeName) {
        for (var i = guideSteps.length - 1; i >= 0; i--) {
            if (guideSteps[i] && guideSteps[i].type === typeName) return guideSteps[i].id || null;
        }
        return null;
    }

    function findLastHumanTaskOwnerIdForResultBranch() {
        // fix25: si ya agregué una acción dentro de APROBADO, esa acción puede ser otra tarea humana.
        // Para cargar la rama NO_APTO de la misma tarea original, no debe tomarse esa tarea hija como dueña.
        for (var i = guideSteps.length - 1; i >= 0; i--) {
            var step = guideSteps[i];
            if (!step || step.type !== 'human_task') continue;
            if (step.branch === 'if_task_ok' || step.branch === 'if_task_reject') continue;
            return step.id || null;
        }
        return findLastStepIdByType('human_task');
    }

    function hasHumanTaskResultBranches() {
        return guideSteps.some(function (x) { return x && (x.branch === 'if_task_ok' || x.branch === 'if_task_reject'); });
    }

    function findBranchOwnerIndex(step, stepIndex, ownerType) {
        if (!step) return -1;

        // fix40c: cuando el destino fue elegido por clic visual, branchSourceId llega
        // desde el DOM como texto. Los ids internos de guideSteps suelen ser numéricos.
        // Si se compara con ===, no matchea y el fallback termina asociando el paso
        // a la última tarea/condición anterior, que puede ser otra rama profunda.
        // Con source explícito, nunca hay que caer por fallback a otro dueño.
        var explicitSourceId = step.branchSourceId !== undefined &&
            step.branchSourceId !== null &&
            String(step.branchSourceId) !== '';

        if (explicitSourceId) {
            var wanted = String(step.branchSourceId);
            for (var i = 0; i < guideSteps.length; i++) {
                if (guideSteps[i] &&
                    String(guideSteps[i].id) === wanted &&
                    guideSteps[i].type === ownerType) {
                    return i;
                }
            }
            return -1;
        }

        // Compatibilidad con pasos viejos sin branchSourceId: mantener el criterio previo.
        for (var j = stepIndex - 1; j >= 0; j--) {
            if (guideSteps[j] && guideSteps[j].type === ownerType) return j;
        }
        return -1;
    }

    function branchOptionsHtml(selectedValue) {
        var sel = selectedValue || 'then';
        var hasCondition = guideSteps.some(function (x) { return x && x.type === 'condition'; });
        var hasTask = guideSteps.some(function (x) { return x && x.type === 'human_task'; });
        var items = [
            ['then', 'Luego / paso normal']
        ];
        if (hasCondition) {
            items.push(['if_cond_true', 'Rama SI de la última condición']);
            items.push(['if_cond_false', 'Rama NO de la última condición']);
        }
        if (hasTask) {
            items.push(['if_task_ok', 'Resultado APTO de la última tarea humana']);
            items.push(['if_task_reject', 'Resultado NO APTO de la última tarea humana']);
        }
        return items.map(function (x) {
            return '<option value="' + x[0] + '"' + (x[0] === sel ? ' selected' : '') + '>' + htmlEncode(x[1]) + '</option>';
        }).join('');
    }

    // fix39d: si el usuario ya eligió visualmente una rama/resultado,
    // el formulario no vuelve a pedir "Cuándo". El destino queda definido por el clic.
    function guideBranchRowHtml(selectedValue) {
        var editing = editingStepIndex >= 0 && editingStepIndex < guideSteps.length;
        if (!editing && !activeTargetIsMain()) {
            var label = (guideActiveTarget && guideActiveTarget.label) || 'rama seleccionada';
            return '' +
                '<div class="wf-ai-guide-context-locked">' +
                '  <div class="wf-ai-guide-context-locked-title">Ubicación definida por clic</div>' +
                '  <div class="wf-ai-guide-context-locked-label">' + htmlEncode(label) + '</div>' +
                '  <div class="wf-ai-guide-context-locked-help">No se muestra “Cuándo” porque ya seleccionaste visualmente dónde agregar este paso.</div>' +
                '</div>';
        }
        return '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml(selectedValue || 'then') + '</select></div>';
    }

    function activeTargetIsMain() {
        return !guideActiveTarget || guideActiveTarget.branch === 'then' || !guideActiveTarget.sourceId;
    }

    function resetGuideActiveTarget() {
        guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };
        renderActiveTargetBox();
        renderGuideSteps();
        setStatus('Destino activo: Flujo principal.', 'ok');
    }

    function isActiveGuideTarget(branch, sourceId) {
        return !!guideActiveTarget &&
            guideActiveTarget.branch === branch &&
            String(guideActiveTarget.sourceId || '') === String(sourceId || '');
    }

    function setGuideActiveTarget(branch, sourceId, label) {
        branch = String(branch || 'then');
        sourceId = sourceId == null ? null : sourceId;
        if (branch === 'then' || !sourceId) {
            guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };
        } else {
            guideActiveTarget = { branch: branch, sourceId: sourceId, label: label || branch };
        }
        renderActiveTargetBox();
        renderGuideSteps();
        setStatus('Destino activo: ' + (guideActiveTarget.label || 'Flujo principal') + '.', 'ok');
    }

    function applyActiveTargetToStep(step) {
        if (!step || activeTargetIsMain()) return step;
        step.branch = guideActiveTarget.branch;
        step.branchSourceId = guideActiveTarget.sourceId;
        return step;
    }

    function renderActiveTargetBox() {
        var box = $('wfAiActiveTarget');
        if (!box) return;
        var label = (guideActiveTarget && guideActiveTarget.label) || 'Flujo principal';
        var isMain = activeTargetIsMain();
        box.className = 'wf-ai-active-target' + (isMain ? ' is-main' : ' is-branch');
        box.innerHTML =
            '<div>' +
            '  <div class="wf-ai-active-target-caption">Agregando próximo paso en</div>' +
            '  <div class="wf-ai-active-target-label">' + htmlEncode(label) + '</div>' +
            '</div>' +
            '<button type="button" class="btn wf-ai-mini-btn" id="wfAiUseMainFlow">Flujo principal</button>';
        var btn = $('wfAiUseMainFlow');
        if (btn) btn.addEventListener('click', function (ev) {
            ev.preventDefault();
            resetGuideActiveTarget();
        });
    }

    function insertAtCursor(el, text) {
        if (!el) return;
        var value = String(el.value || '');
        var start = typeof el.selectionStart === 'number' ? el.selectionStart : value.length;
        var end = typeof el.selectionEnd === 'number' ? el.selectionEnd : start;
        el.value = value.substring(0, start) + text + value.substring(end);
        try {
            el.focus();
            el.selectionStart = el.selectionEnd = start + text.length;
        } catch (e) { }
    }

    function setSelectOrInputValue(id, value) {
        var el = $(id);
        if (!el) return false;
        if (el.tagName && el.tagName.toLowerCase() === 'select') {
            var found = false;
            Array.prototype.forEach.call(el.options || [], function (opt) {
                if (opt.value === value) found = true;
            });
            if (!found) {
                var opt = document.createElement('option');
                opt.value = value;
                opt.text = value;
                el.appendChild(opt);
            }
        }
        el.value = value;
        return true;
    }

    function applyAvailableFieldToCurrentForm(path) {
        path = String(path || '').trim();
        if (!path) return;
        var type = selectedText('wfAiStepType') || 'doc_load';
        var token = '${' + path + '}';

        if (type === 'condition') {
            if ((selectedText('wfAiStepConditionMode') || 'simple') === 'compound') {
                applyAvailableFieldToCompoundRule(path);
                return;
            }
            if (setSelectOrInputValue('wfAiStepField', path)) {
                syncConditionFields();
                setStatus('Campo cargado en la condición: ' + path, 'ok');
                return;
            }
        }

        if (type === 'state_set') {
            var mode = selectedText('wfAiStepStateMode') || 'simple';
            if (mode === 'simple') {
                setSelectOrInputValue('wfAiStepValueField', path);
                setControlValue('wfAiStepValue', token);
                setStatus('Dato cargado como valor de la variable: ' + path, 'ok');
                return;
            }
        }

        if (type === 'file_write') {
            var modeWrite = selectedText('wfAiStepFileWriteSourceMode') || 'manual';
            if (modeWrite === 'context') {
                setSelectOrInputValue('wfAiStepFileWriteOriginField', path);
                setControlValue('wfAiStepFileWriteOrigin', path);
                setStatus('Variable origen cargada para escribir archivo: ' + path, 'ok');
                return;
            }
            insertAtCursor($('wfAiStepFileWriteContent'), token);
            setStatus('Dato insertado en el contenido del archivo: ' + token, 'ok');
            return;
        }

        if (type === 'file_read' && path === 'input.filePath') {
            setControlValue('wfAiStepFileReadPath', token);
            setStatus('Ruta cargada desde dato disponible: ' + token, 'ok');
            return;
        }

        if (type === 'queue_publish') {
            var queuePublishMode = selectedText('wfAiStepQueuePayloadMode') || 'simple';
            if (queuePublishMode === 'fields') {
                var queueTarget = queuePayloadLastValueInput;
                if (!queueTarget || !document.body.contains(queueTarget)) {
                    var queueRows = $('wfAiQueuePayloadFields');
                    queueTarget = queueRows && queueRows.querySelector('[data-queue-payload-value]');
                }
                if (queueTarget) {
                    queueTarget.value = token;
                    queuePayloadLastValueInput = queueTarget;
                    updateQueuePayloadPreview();
                    setStatus('Dato cargado como valor del campo del mensaje: ' + token, 'ok');
                    return;
                }
            } else if (queuePublishMode === 'simple') {
                insertAtCursor($('wfAiStepQueuePayloadSimple'), token);
                setStatus('Dato insertado en el mensaje: ' + token, 'ok');
                return;
            } else {
                insertAtCursor($('wfAiStepQueuePayload'), token);
                setStatus('Dato insertado en el contenido avanzado: ' + token, 'ok');
                return;
            }
        }

        var active = document.activeElement;
        if (active && $('wfAiStepFields') && $('wfAiStepFields').contains(active) &&
            (active.tagName || '').toLowerCase().match(/^(input|textarea)$/)) {
            insertAtCursor(active, token);
            setStatus('Dato insertado: ' + token, 'ok');
            return;
        }

        if (type === 'logger') {
            insertAtCursor($('wfAiStepMessage'), token);
            setStatus('Dato insertado en el mensaje del log: ' + token, 'ok');
            return;
        }
        if (type === 'notify') {
            insertAtCursor($('wfAiStepMessage'), token);
            setStatus('Dato insertado en el mensaje de notificación: ' + token, 'ok');
            return;
        }
        if (type === 'email_send') {
            insertAtCursor($('wfAiStepBody'), token);
            setStatus('Dato insertado en el cuerpo del correo: ' + token, 'ok');
            return;
        }
        if (type === 'http_request') {
            insertAtCursor($('wfAiStepHttpBody'), token);
            setStatus('Dato insertado en el body HTTP: ' + token, 'ok');
            return;
        }
        if (type === 'sql_query') {
            insertAtCursor($('wfAiStepSqlParams'), token);
            setStatus('Dato insertado en parámetros SQL: ' + token, 'ok');
            return;
        }
        if (type === 'subflow') {
            insertAtCursor($('wfAiStepSubflowInput'), token);
            setStatus('Dato insertado en Input JSON del subflujo: ' + token, 'ok');
            return;
        }

        setStatus('Dato seleccionado: ' + path + '. Elegí un campo del formulario para insertarlo.', 'ok');
    }


    function sqlShortText(query) {
        var q = String(query || '').replace(/\s+/g, ' ').trim();
        if (!q) return '(sin SQL)';
        return q.length > 60 ? q.substring(0, 57) + '...' : q;
    }

    function parseJsonObjectOrEmpty(text) {
        var raw = String(text || '').trim();
        if (!raw) return {};
        try {
            var obj = JSON.parse(raw);
            if (obj && typeof obj === 'object' && !Array.isArray(obj)) return obj;
        } catch (e) { }
        return null;
    }

    function parseStateSimpleValueForPlan(text) {
        var raw = String(text == null ? '' : text);
        var trimmed = raw.trim();
        if (!trimmed) return '';

        // En modo simple, el valor común queda como texto.
        // Si el usuario escribe explícitamente un objeto/array JSON, se conserva como estructura.
        if ((trimmed.charAt(0) === '{' && trimmed.charAt(trimmed.length - 1) === '}') ||
            (trimmed.charAt(0) === '[' && trimmed.charAt(trimmed.length - 1) === ']')) {
            try { return JSON.parse(trimmed); } catch (e) { return raw; }
        }
        return raw;
    }

    function isStateJsonMode(step) {
        return !!(step && (step.mode === 'json' || step.setJson));
    }

    function stateSetObjectFromStep(step) {
        if (!step) return {};
        if (isStateJsonMode(step)) {
            var obj = parseJsonObjectOrEmpty(step.setJson || '');
            return obj || {};
        }
        var key = String(step.key || '').trim();
        if (!key) return {};
        var set = {};
        set[key] = parseStateSimpleValueForPlan(step.value || '');
        return set;
    }

    function stateSetKeysForStep(step) {
        var obj = stateSetObjectFromStep(step);
        return Object.keys(obj || {});
    }

    function stateSetTitle(step) {
        var keys = stateSetKeysForStep(step);
        if (!keys.length) return 'Guardar variable: (sin destino)';
        if (keys.length === 1) return 'Guardar variable: ' + keys[0];
        return 'Guardar variables: ' + keys.length + ' dato(s)';
    }

    function createStepTitle(step) {
        if (!step) return '';
        if (step.type === 'doc_load') return 'Cargar documento: ' + docPhrase(step.docTipo || '');
        if (step.type === 'condition') return 'Condición: ' + conditionInfo(step).text;
        if (step.type === 'human_task') return 'Tarea humana: ' + ((step.destType === 'usuario' || step.user) ? (step.user || '(usuario)') : (step.role || '(rol)')) + ' / ' + (step.title || step.purpose || 'tarea');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
        if (step.type === 'http_request') return 'Solicitud HTTP: ' + ((step.method || 'GET') + ' ' + (step.url || '(sin URL)'));
        if (step.type === 'sql_query') return 'Consulta SQL: ' + sqlShortText(step.query || '');
        if (step.type === 'file_read') return 'Archivo: Leer ' + (step.path || '(sin ruta)');
        if (step.type === 'file_write') return 'Archivo: Escribir ' + (step.path || '(sin ruta)');
        if (step.type === 'queue_publish') return 'Cola: Publicar en ' + (step.queue || '(sin cola)');
        if (step.type === 'queue_consume') return 'Cola: Consumir de ' + (step.queue || '(sin cola)');
        if (step.type === 'subflow') return 'Ejecutar workflow: ' + subflowDisplayName(step);
        if (step.type === 'state_set') return stateSetTitle(step);
        if (step.type === 'state_remove') return 'Quitar variable: ' + (step.key || '');
        if (step.type === 'delay') return 'Demora: ' + (step.ms || '1000') + ' ms';
        if (step.type === 'retry') return 'Reintentar: ' + (step.reintentos || '3') + ' reintento(s)';
        if (step.type === 'error_handler') return 'Manejador de Error';
        if (step.type === 'logger') return 'Registrar log: ' + (step.level || 'Info');
        if (step.type === 'end') return 'Finalizar flujo';
        return step.type;
    }

    function stepBody(step, ctx) {
        if (!step) return '';
        if (step.type === 'doc_load') {
            return 'cargar ' + docPhrase(step.docTipo) + ' desde ' + (step.path || 'input.filePath');
        }
        if (step.type === 'condition') {
            var info = conditionInfo(step);
            ctx.lastCondition = info;
            if (step.id) ctx.conditionsById[step.id] = info;
            if (step.fieldPath) {
                var phrase = 'validar el campo ' + step.fieldPath + ' con operador ' + (step.operator || 'not_empty');
                if (operatorNeedsValue(step.operator) && String(step.value || '').trim()) phrase += ' valor ' + String(step.value || '').trim();
                return phrase;
            }
            return info.text;
        }
        if (step.type === 'human_task') {
            var role = step.role || '';
            var user = step.user || '';
            var title = step.title || step.purpose || '';
            ctx.lastTaskRole = user || role;
            if (step.id) ctx.tasksById[step.id] = { role: user || role, purpose: title };

            var taskParts = [];
            if (step.destType === 'usuario' || user) taskParts.push('Usuario = ' + user);
            else taskParts.push('Rol = ' + role);
            if (title) taskParts.push('Título = ' + title);
            if (String(step.description || '').trim()) taskParts.push('Descripción = ' + String(step.description).trim());
            if ((parseInt(step.deadlineMinutes || 0, 10) || 0) > 0) taskParts.push('Vencimiento = ' + parseInt(step.deadlineMinutes, 10));
            if (String(step.pendingState || '').trim()) taskParts.push('Estado = ' + String(step.pendingState).trim());
            return 'crear una tarea con ' + taskParts.join('; ');
        }
        if (step.type === 'email_send') {
            return 'enviar un email a ' + (step.to || 'destinatario@empresa.com') +
                ' con asunto ' + (step.subject || 'Aviso Workflow Studio') +
                ' y cuerpo ' + (step.body || 'Se generó un aviso desde Workflow Studio');
        }
        if (step.type === 'notify') {
            var dest = step.destType === 'usuario'
                ? 'al usuario ' + (step.user || 'OMARD\\OMARD')
                : 'al rol ' + (step.role || 'COMPRAS');
            return 'notificar internamente ' + dest +
                ' con asunto ' + (step.title || 'Aviso interno') +
                ' y mensaje ' + (step.message || 'Hay una novedad pendiente en el workflow');
        }
        if (step.type === 'http_request') {
            return 'hacer solicitud HTTP ' + (step.method || 'GET') + ' a ' + (step.url || '/Api/Ping.ashx');
        }
        if (step.type === 'sql_query') {
            return 'ejecutar consulta SQL ' + sqlShortText(step.query || '');
        }
        if (step.type === 'file_read') {
            return 'leer archivo ' + (step.path || 'C:\\Temp\\entrada.txt') + ' y guardar el contenido en ' + (step.output || 'archivo');
        }
        if (step.type === 'file_write') {
            return 'escribir archivo ' + (step.path || 'C:\\Temp\\salida.txt');
        }
        if (step.type === 'queue_publish') {
            var queueNamePhrase = step.queue || '';
            var queuePayloadPhrase = String(queuePayloadTextForStep(step) || '').replace(/\s+/g, ' ').trim();
            var sameQueuePublish = !!(ctx.lastQueue && String(ctx.lastQueue).toLowerCase() === String(queueNamePhrase).toLowerCase());
            ctx.lastQueue = queueNamePhrase;
            // FIX84B2: la frase humana describe la intención y el contenido; no obliga a
            // recordar la propiedad técnica "payload" ni a repetir la cola activa.
            return (sameQueuePublish ? 'publicar ' : 'publicar en la cola ' + queueNamePhrase + ' ') + (queuePayloadPhrase || 'un mensaje');
        }
        if (step.type === 'queue_consume') {
            var queueNameConsume = step.queue || '';
            var queueTakePhrase = parseInt(step.take || '1', 10);
            if (isNaN(queueTakePhrase) || queueTakePhrase < 1) queueTakePhrase = 1;
            var sameQueueConsume = !!(ctx.lastQueue && String(ctx.lastQueue).toLowerCase() === String(queueNameConsume).toLowerCase());
            ctx.lastQueue = queueNameConsume;
            return 'leer ' + queueTakePhrase + (queueTakePhrase === 1 ? ' mensaje' : ' mensajes') + (sameQueueConsume ? '' : ' de la cola ' + queueNameConsume);
        }
        if (step.type === 'subflow') {
            var txt = 'ejecutar otro workflow ' + (step.ref || '(sin workflow)');
            if (step.alias) txt += ' con alias ' + step.alias;
            return txt;
        }
        if (step.type === 'state_set') {
            return 'guardar variable ' + (step.key || 'wf.variable') + ' con valor ' + (step.value || 'valor');
        }
        if (step.type === 'state_remove') {
            return 'quitar variable ' + (step.key || 'wf.variable');
        }
        if (step.type === 'delay') {
            return 'esperar ' + (step.ms || '1000') + ' ms';
        }
        if (step.type === 'retry') {
            return 'reintentar el siguiente paso hasta ' + (step.reintentos || '3') + ' reintento(s) con espera ' + (step.backoffMs || '500') + ' ms';
        }
        if (step.type === 'error_handler') {
            return 'marcar error con mensaje ' + (step.message || 'Error controlado por el workflow');
        }
        if (step.type === 'logger') {
            return step.message ? 'registrar un log ' + (step.level || 'Info') + ' con mensaje ' + step.message : 'registrar un log ' + (step.level || 'Info');
        }
        if (step.type === 'end') {
            return 'finalizar';
        }
        return '';
    }

    function cleanGuidePhraseText(text) {
        var t = String(text == null ? '' : text);

        // Defensa UX: en algunos recorridos/copias podía quedar texto de etiqueta del
        // formulario dentro de la frase enviada al intérprete, por ejemplo "Agregar paso:".
        // No cambia el grafo ni las acciones; solo limpia ruido antes de armar/interpretar.
        t = t.replace(/(^|[\r\n,;.])\s*Agregar\s+paso\s*:\s*/gi, function (m, sep) {
            return sep ? (sep + ' ') : '';
        });

        // Normalización suave para evitar dobles separadores después de limpiar etiquetas.
        t = t.replace(/\s+,\s+/g, ', ')
            .replace(/,\s*,+/g, ',')
            .replace(/\s+\./g, '.')
            .replace(/\s{2,}/g, ' ')
            .trim();

        return t;
    }

    // FIX84C2Bg2c: si una rama termina explícitamente en Fin y el siguiente paso
    // común también es Fin, ambos representan el mismo cierre funcional. Mantener dos nodos
    // Fin sólo porque el editor los mostró en dos lugares rompe la convergencia Frase ↔ Paso a paso.
    function nextMainGuideStepIndexAfter(ownerIndex) {
        for (var i = ownerIndex + 1; i < guideSteps.length; i++) {
            if (guideSteps[i] && !isBranchChildStep(guideSteps[i])) return i;
        }
        return -1;
    }

    function isRedundantBranchEndStep(step, stepIndex) {
        if (!step || step.type !== 'end' || !isBranchChildStep(step)) return false;

        var ownerType = (step.branch === 'if_task_ok' || step.branch === 'if_task_reject')
            ? 'human_task'
            : ((step.branch === 'if_cond_true' || step.branch === 'if_cond_false') ? 'condition' : '');
        if (!ownerType) return false;

        var ownerIndex = findBranchOwnerIndex(step, stepIndex, ownerType);
        if (ownerIndex < 0) return false;

        var nextMainIndex = nextMainGuideStepIndexAfter(ownerIndex);
        return nextMainIndex >= 0
            && guideSteps[nextMainIndex]
            && guideSteps[nextMainIndex].type === 'end';
    }

    function buildIncrementalPhrase() {
        // FIX84B2: Inicio y Fin normales son estructura del workflow, no palabras que el
        // usuario deba recordar. Solo se verbaliza un Fin cuando es funcional a una rama.
        var parts = [];
        var ctx = { lastCondition: null, lastTaskRole: null, lastQueue: null, conditionsById: {}, tasksById: {}, branchesUsed: false, commonAfterBranchExplained: false };

        guideSteps.forEach(function (step, stepIndex) {
            // FIX84C2Bg2c: no verbalizar un Fin de rama que ya converge al Fin común.
            if (isRedundantBranchEndStep(step, stepIndex)) return;
            var normalEnd = step && step.type === 'end' && (!step.branch || step.branch === 'main' || step.branch === 'always');
            if (normalEnd) return;
            var prefix = branchPrefix(step.branch, ctx, step);
            var body = stepBody(step, ctx);

            // Separador importante para el intérprete ML.NET:
            // sin "luego", un cuerpo de email/notificación puede absorber el texto
            // del paso siguiente. Ej.: cuerpo ... notificar internamente ...
            if (!prefix && parts.length > 0) {
                if (ctx.branchesUsed && !ctx.commonAfterBranchExplained) {
                    prefix = 'luego de cualquiera de las ramas ';
                    ctx.commonAfterBranchExplained = true;
                } else {
                    prefix = 'luego ';
                }
            }

            if (body) {
                parts.push(prefix + body);
                if (step.branch === 'if_cond_true' || step.branch === 'if_cond_false' || step.branch === 'if_task_ok' || step.branch === 'if_task_reject') ctx.branchesUsed = true;
            }
        });

        return cleanGuidePhraseText(parts.length ? (parts.join(', ') + '.') : '');
    }

    function hasEndStep() {
        return guideSteps.some(function (x) { return x.type === 'end'; });
    }


    // ------------------------------------------------------------
    // fix26: validador funcional del constructor guiado
    // ------------------------------------------------------------
    function pushUnique(list, text) {
        text = String(text || '').trim();
        if (!text) return;
        if (list.indexOf(text) < 0) list.push(text);
    }

    function stepShortName(step, idx) {
        var title = createStepTitle(step || {});
        return 'Paso ' + (idx + 1) + (title ? ' — ' + title : '');
    }

    function mainStepIndexesForValidation() {
        var indexes = [];
        guideSteps.forEach(function (step, idx) {
            if (!step || isBranchChildStep(step)) return;
            indexes.push(idx);
        });
        return indexes;
    }

    function nextMainIndexAfter(mainIndexes, idx) {
        for (var i = 0; i < mainIndexes.length; i++) {
            if (mainIndexes[i] === idx) return i + 1 < mainIndexes.length ? mainIndexes[i + 1] : -1;
        }
        return -1;
    }

    function humanDestinationKey(step) {
        if (!step || step.type !== 'human_task') return '';
        if (step.destType === 'usuario' || step.user) return 'USUARIO:' + normalizeKey(step.user || '');
        return 'ROL:' + normalizeKey(step.role || '');
    }

    function humanDestinationText(step) {
        if (!step || step.type !== 'human_task') return '';
        if (step.destType === 'usuario' || step.user) return step.user || '(usuario)';
        return step.role || '(rol)';
    }

    function isProbablyTestPath(path) {
        var v = String(path || '').trim();
        if (!v) return false;
        if (normalizeKey(v) === 'INPUT.FILEPATH' || v.indexOf('${') >= 0) return false;
        var compact = v.replace(/^['"]|['"]$/g, '').trim();
        if (/^[a-z]$/i.test(compact)) return true;
        if (compact.length <= 2 && compact.indexOf(':') < 0) return true;
        return false;
    }

    function isProbablyInvalidEmail(to) {
        var v = String(to || '').trim();
        if (!v) return true;
        if (normalizeKey(v) === 'DESTINATARIO@EMPRESA.COM') return true;
        return v.indexOf('@') < 1 || v.indexOf('.') < 0;
    }

    function firstBranchHumanDestination(indexes) {
        indexes = indexes || [];
        for (var i = 0; i < indexes.length; i++) {
            var st = guideSteps[indexes[i]];
            if (st && st.type === 'human_task') return st;
        }
        return null;
    }

    function buildFunctionalValidation() {
        var result = { ok: true, errors: [], warnings: [] };
        if (!guideSteps.length) return result;

        var maps = buildStructuredBranchMaps();
        var allIndexes = [];
        guideSteps.forEach(function (step, idx) {
            if (step) allIndexes.push(idx);
        });

        function branchSourceKey(step) {
            return String(step && step.branchSourceId || '');
        }

        function sameSequenceIndexes(idx) {
            var base = guideSteps[idx];
            if (!base) return [];
            var baseIsBranch = isBranchChildStep(base);
            var baseBranch = String(base.branch || '');
            var baseSource = branchSourceKey(base);
            var indexes = [];

            guideSteps.forEach(function (candidate, ci) {
                if (!candidate) return;
                if (!baseIsBranch) {
                    if (!isBranchChildStep(candidate)) indexes.push(ci);
                    return;
                }

                if (String(candidate.branch || '') === baseBranch && branchSourceKey(candidate) === baseSource) {
                    indexes.push(ci);
                }
            });

            return indexes;
        }

        function nextIndexInSameSequence(idx) {
            var indexes = sameSequenceIndexes(idx);
            for (var i = 0; i < indexes.length; i++) {
                if (indexes[i] === idx) return i + 1 < indexes.length ? indexes[i + 1] : -1;
            }
            return -1;
        }

        function hasTaskResultBranches(taskGroups) {
            return !!(taskGroups && ((taskGroups.ok && taskGroups.ok.length) || (taskGroups.reject && taskGroups.reject.length)));
        }

        if (!hasEndStep()) {
            pushUnique(result.warnings, 'Falta finalizar el flujo. Agregá “Finalizar flujo” como último paso para dejar la propuesta cerrada.');
        }

        allIndexes.forEach(function (idx) {
            var step = guideSteps[idx];
            if (!step) return;

            var nextSameIdx = nextIndexInSameSequence(idx);

            if (step.type === 'end' && nextSameIdx >= 0) {
                pushUnique(result.warnings, 'Hay pasos después de “Finalizar flujo” en la misma rama/secuencia. Revisá el orden, porque esos pasos podrían quedar fuera del recorrido.');
            }

            if (step.type === 'doc_load' && isProbablyTestPath(step.path)) {
                pushUnique(result.warnings, 'La ruta del documento en ' + stepShortName(step, idx) + ' parece una ruta de prueba (“' + String(step.path || '').trim() + '”).');
            }

            if (step.type === 'condition') {
                var compoundRules = conditionRuleList(step);
                if (compoundRules.length) {
                    compoundRules.forEach(function (r, ri) {
                        if (operatorNeedsValue(r.operator) && !String(r.value || '').trim()) {
                            pushUnique(result.warnings, stepShortName(step, idx) + ': la regla ' + (ri + 1) + ' necesita un valor.');
                        }
                    });
                } else if (operatorNeedsValue(step.operator) && !String(step.value || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el operador seleccionado necesita un valor.');
                }

                var condGroups = maps.conditions[idx] || { si: [], no: [] };
                if (!condGroups.si.length) pushUnique(result.warnings, stepShortName(step, idx) + ': la rama SI está vacía. Si se cumple, continuará directo al siguiente paso común.');
                if (!condGroups.no.length) pushUnique(result.warnings, stepShortName(step, idx) + ': la rama NO está vacía. Si no se cumple, continuará directo al siguiente paso común.');

                if ((condGroups.si.length || condGroups.no.length) && nextSameIdx >= 0 && guideSteps[nextSameIdx] && guideSteps[nextSameIdx].type !== 'end') {
                    pushUnique(result.warnings, 'Después de ' + stepShortName(step, idx) + ', el paso común “' + createStepTitle(guideSteps[nextSameIdx]) + '” se ejecutará luego de cualquiera de las ramas de esa condición.');
                }
            }

            if (step.type === 'human_task') {
                if (step.destType === 'usuario' && !resolveUserSelection(step.user)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el usuario asignado no coincide con un usuario real del catálogo.');
                }

                var taskGroups = maps.tasks[idx] || { ok: [], reject: [] };
                var hasResultBranches = hasTaskResultBranches(taskGroups);

                if (hasResultBranches) {
                    if (!taskGroups.ok.length) pushUnique(result.warnings, stepShortName(step, idx) + ': falta definir qué pasa si la tarea queda APROBADA/APTA.');
                    if (!taskGroups.reject.length) pushUnique(result.warnings, stepShortName(step, idx) + ': falta definir qué pasa si la tarea queda RECHAZADA/NO APTA.');

                    var ownerDest = humanDestinationKey(step);
                    var okHuman = firstBranchHumanDestination(taskGroups.ok);
                    var rejectHuman = firstBranchHumanDestination(taskGroups.reject);
                    if (okHuman && ownerDest && ownerDest === humanDestinationKey(okHuman)) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': la rama APTO vuelve al mismo destino (' + humanDestinationText(step) + '). Es válido si representa otra etapa, pero conviene diferenciar claramente el objetivo.');
                    }
                    if (rejectHuman && ownerDest && ownerDest === humanDestinationKey(rejectHuman)) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': la rama NO APTO vuelve al mismo destino (' + humanDestinationText(step) + '). Revisá si corresponde.');
                    }

                    if (nextSameIdx >= 0 && guideSteps[nextSameIdx] && guideSteps[nextSameIdx].type !== 'end') {
                        pushUnique(result.warnings, 'Después de ' + stepShortName(step, idx) + ', el paso común “' + createStepTitle(guideSteps[nextSameIdx]) + '” se ejecutará luego de cualquiera de los resultados humanos de esa tarea.');
                    }
                } else if (nextSameIdx < 0) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': la tarea humana no tiene un paso posterior ni ramas APTO/NO APTO definidas. Agregá un paso de salida o elegí una rama de resultado.');
                }

                if (nextSameIdx >= 0) {
                    var nextStep = guideSteps[nextSameIdx];
                    if (nextStep && nextStep.type === 'human_task' && humanDestinationKey(step) && humanDestinationKey(step) === humanDestinationKey(nextStep)) {
                        pushUnique(result.warnings, 'Hay dos tareas humanas consecutivas para el mismo destino (' + humanDestinationText(step) + '). Es válido si son etapas distintas, pero conviene que el objetivo/título lo deje claro.');
                    }
                }
            }

            if (step.type === 'email_send' && isProbablyInvalidEmail(step.to)) {
                pushUnique(result.warnings, stepShortName(step, idx) + ': el destinatario del correo parece incompleto o de ejemplo.');
            }

            if (step.type === 'notify' && step.destType === 'usuario' && !resolveUserSelection(step.user)) {
                pushUnique(result.warnings, stepShortName(step, idx) + ': el usuario de la notificación no coincide con un usuario real del catálogo.');
            }

            if (step.type === 'http_request') {
                if (!String(step.url || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la URL de la solicitud HTTP.');
                } else if (/^https?:\/\//i.test(String(step.url || '')) && String(step.url || '').indexOf('localhost') < 0) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': la URL parece externa. Para intranet conviene usar una URL relativa o del servidor interno.');
                }
            }

            if (step.type === 'sql_query') {
                if (!String(step.query || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la consulta/comando SQL.');
                }
                var sql = String(step.query || '').trim();
                if (/\b(drop|truncate|alter)\b/i.test(sql)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el SQL contiene una instrucción peligrosa. Revisalo antes de ejecutar.');
                }
                if (/\bdelete\b/i.test(sql) && !/\bwhere\b/i.test(sql)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': DELETE sin WHERE puede afectar demasiados registros.');
                }
                if (String(step.paramsJson || '').trim()) {
                    try { JSON.parse(step.paramsJson); }
                    catch (e) { pushUnique(result.warnings, stepShortName(step, idx) + ': los parámetros SQL deben ser JSON válido, por ejemplo {"Id":150484}.'); }
                }
            }

            if (step.type === 'file_read') {
                if (!String(step.path || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la ruta del archivo a leer.');
                else if (isProbablyTestPath(step.path)) pushUnique(result.warnings, stepShortName(step, idx) + ': la ruta del archivo parece una ruta de prueba (“' + String(step.path || '').trim() + '”).');
                if (!String(step.output || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar dónde guardar el contenido leído.');
                else if (!isValidStatePath(step.output)) pushUnique(result.warnings, stepShortName(step, idx) + ': el destino de salida tiene formato inválido. Usá algo como archivo.texto o biz.archivo.texto.');
                if (String(step.output || '').indexOf('file.read.') === 0) pushUnique(result.warnings, stepShortName(step, idx) + ': no conviene guardar el contenido sobre salidas técnicas file.read.*.');
            }

            if (step.type === 'file_write') {
                if (!String(step.path || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la ruta del archivo a escribir.');
                else if (isProbablyTestPath(step.path)) pushUnique(result.warnings, stepShortName(step, idx) + ': la ruta del archivo parece una ruta de prueba (“' + String(step.path || '').trim() + '”).');
                if (step.contentMode === 'fromVar') {
                    if (!String(step.origen || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la variable origen para escribir.');
                    else if (!isValidStatePath(step.origen)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable origen tiene formato inválido.');
                } else if (!String(step.content || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el contenido a escribir.');
                }
            }

            if (step.type === 'queue_publish') {
                if (!String(step.queue || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la cola donde publicar.');
                else if (String(step.queue || '').length > 100) pushUnique(result.warnings, stepShortName(step, idx) + ': el nombre de cola supera 100 caracteres.');
                var queuePayloadValue = queuePayloadValueFromStep(step);
                if (queuePayloadValue === '' || queuePayloadValue == null ||
                    (typeof queuePayloadValue === 'object' && !Array.isArray(queuePayloadValue) && !Object.keys(queuePayloadValue).length)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar qué información querés publicar.');
                }
                if (String(step.payloadMode || '') === 'fields') {
                    var queueFieldNames = {};
                    normalizeQueuePayloadFields(step.payloadFields || []).forEach(function (field) {
                        var normalizedName = normalizeKey(field.name);
                        if (!queuePayloadFieldNameIsValid(field.name)) pushUnique(result.warnings, stepShortName(step, idx) + ': el campo "' + field.name + '" tiene formato inválido.');
                        if (normalizedName && queueFieldNames[normalizedName]) pushUnique(result.warnings, stepShortName(step, idx) + ': el campo "' + field.name + '" está repetido.');
                        queueFieldNames[normalizedName] = true;
                    });
                }
            }

            if (step.type === 'queue_consume') {
                if (!String(step.queue || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la cola a consumir.');
                else if (String(step.queue || '').length > 100) pushUnique(result.warnings, stepShortName(step, idx) + ': el nombre de cola supera 100 caracteres.');
                var queueTake = parseInt(step.take || '1', 10);
                if (isNaN(queueTake) || queueTake < 1 || queueTake > 100) pushUnique(result.warnings, stepShortName(step, idx) + ': la cantidad a tomar debe estar entre 1 y 100.');
                if (!String(step.connectionStringName || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la conexión SQL.');
            }

            if (step.type === 'subflow') {
                if (!String(step.ref || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta seleccionar el workflow a ejecutar.');
                var sj = parseJsonObjectOrEmpty(step.inputJson || '{}');
                if (sj === null) pushUnique(result.warnings, stepShortName(step, idx) + ': el Input JSON debe ser un objeto JSON válido.');
                if (step.alias && !subflowAliasIsValid(step.alias)) pushUnique(result.warnings, stepShortName(step, idx) + ': el alias debe usar letras/números/_ y no puede empezar con número.');
                var md = parseInt(step.maxDepth || '10', 10);
                if (isNaN(md) || md < 1 || md > 50) pushUnique(result.warnings, stepShortName(step, idx) + ': la profundidad máxima debe estar entre 1 y 50.');
            }

            if (step.type === 'state_set') {
                var isAdvancedStateSet = String(step.mode || '').toLowerCase() === 'advanced';
                if (isAdvancedStateSet) {
                    if (!String(step.json || '').trim()) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el JSON a guardar.');
                    } else {
                        var parsed = parseJsonObjectOrEmpty(step.json);
                        if (parsed === null) {
                            pushUnique(result.warnings, stepShortName(step, idx) + ': el JSON a guardar no es válido.');
                        } else {
                            var keys = Object.keys(parsed || {});
                            if (!keys.length) pushUnique(result.warnings, stepShortName(step, idx) + ': el JSON a guardar está vacío.');
                            keys.forEach(function (k) {
                                if (!isValidStatePath(k)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable destino "' + k + '" tiene formato inválido.');
                                if (isTechnicalOutputPath(k)) pushUnique(result.warnings, stepShortName(step, idx) + ': estás por guardar sobre una salida técnica (' + k + '). Conviene usar biz.* o wf.vars.*.');
                            });
                        }
                    }
                } else {
                    if (!String(step.key || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la variable destino.');
                    else if (!isValidStatePath(step.key)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable destino tiene un formato inválido. Usá algo como biz.prueba.fix30.');
                    if (isTechnicalOutputPath(step.key)) pushUnique(result.warnings, stepShortName(step, idx) + ': estás por guardar sobre una salida técnica (' + step.key + '). Conviene usar biz.* o wf.vars.*.');
                    if (!String(step.value || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el valor a guardar.');
                }
            }

            if (step.type === 'state_remove') {
                if (!String(step.key || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la variable a quitar.');
                else if (!isValidStatePath(step.key)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable a quitar tiene un formato inválido.');
                if (isTechnicalOutputPath(step.key)) pushUnique(result.warnings, stepShortName(step, idx) + ': estás por quitar una salida técnica (' + step.key + '). Conviene quitar solo variables propias.');
            }

            if (step.type === 'retry') {
                var r = parseInt(step.reintentos || '3', 10);
                var b = parseInt(step.backoffMs || '500', 10);
                if (isNaN(r) || r < 0) pushUnique(result.warnings, stepShortName(step, idx) + ': la cantidad de reintentos debe ser 0 o mayor.');
                if (r > 10) pushUnique(result.warnings, stepShortName(step, idx) + ': muchos reintentos pueden demorar el flujo. Revisá si realmente necesitás más de 10.');
                if (isNaN(b) || b < 0) pushUnique(result.warnings, stepShortName(step, idx) + ': el backoff debe ser 0 o mayor.');
                if (b > 60000) pushUnique(result.warnings, stepShortName(step, idx) + ': backoff mayor a 60000 ms puede dejar la instancia esperando mucho tiempo.');
                var nextRetryIdx = nextIndexInSameSequence(idx);
                var nextRetryStep = nextRetryIdx >= 0 ? guideSteps[nextRetryIdx] : null;
                if (!nextRetryStep) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': Reintentar debe ubicarse antes del nodo que querés reintentar. Ahora no tiene un paso siguiente.');
                } else if (nextRetryStep.type === 'end') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': Reintentar antes de Finalizar flujo no aporta valor. Ubicalo antes de HTTP, SQL, correo, documento, archivo o cola.');
                } else if (nextRetryStep.type === 'human_task') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': no conviene reintentar una tarea humana porque podría crear tareas duplicadas.');
                } else if (['http_request', 'sql_query', 'email_send', 'doc_load', 'file_read', 'file_write', 'queue_publish', 'queue_consume', 'subflow'].indexOf(nextRetryStep.type) < 0) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el siguiente paso normalmente no requiere reintento. Usalo principalmente antes de HTTP, SQL, correo, documento, archivo o cola.');
                }
            }

            if (step.type === 'error_handler') {
                if (!String(step.message || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el mensaje de error.');
                }
                if (step.action === 'retry') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': “Volver a intentar” en util.error solo marca intención; para reintentos reales agregá el paso Reintentar antes del nodo que puede fallar.');
                }
            }

            if (step.type === 'logger') {
                if (!String(step.message || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el mensaje del log.');
            }
        });

        var aliasCount = {};
        var subflowCount = 0;
        allIndexes.forEach(function (idx) {
            var st = guideSteps[idx];
            if (!st || st.type !== 'subflow') return;
            subflowCount++;
            if (st.alias) {
                var ak = normalizeKey(st.alias);
                aliasCount[ak] = (aliasCount[ak] || 0) + 1;
            }
        });
        allIndexes.forEach(function (idx) {
            var st = guideSteps[idx];
            if (!st || st.type !== 'subflow') return;
            if (st.alias && aliasCount[normalizeKey(st.alias)] > 1) pushUnique(result.warnings, stepShortName(st, idx) + ': hay otro subflujo con el mismo alias. Conviene usar alias únicos.');
            if (subflowCount > 1 && !String(st.alias || '').trim()) pushUnique(result.warnings, stepShortName(st, idx) + ': hay más de un subflujo. Conviene definir alias para poder distinguir salidas subflows.<alias>.*.');
        });

        // fix40b: advertencias finales por ramas profundas.
        // Antes se validaba solo el flujo principal; por eso una rama interna incompleta podía mostrar
        // "Sin advertencias funcionales" aunque faltaran salidas APTO/NO APTO o pasos posteriores.
        result.ok = !result.errors.length;
        return result;
    }

    function renderFunctionalValidationPanel(validation) {
        validation = validation || { errors: [], warnings: [] };
        var errors = validation.errors || [];
        var warnings = validation.warnings || [];
        var html = '<div class="wf-ai-functional-validation">';
        html += '<div class="wf-ai-functional-validation-title">Validación funcional del constructor</div>';

        if (!errors.length && !warnings.length) {
            html += '<div class="wf-ai-functional-ok">Sin advertencias funcionales. La estructura del flujo se ve consistente.</div>';
            html += '</div>';
            return html;
        }

        if (errors.length) {
            html += '<ul class="wf-ai-functional-errors">';
            errors.forEach(function (x) { html += '<li>' + htmlEncode(x) + '</li>'; });
            html += '</ul>';
        }
        if (warnings.length) {
            html += '<ul class="wf-ai-functional-warnings">';
            warnings.forEach(function (x) { html += '<li>' + htmlEncode(x) + '</li>'; });
            html += '</ul>';
        }
        html += '</div>';
        return html;
    }

    function updatePromptFromSteps(runAfter) {
        resetGuidedPlanConfirmation();
        var prompt = $('wfAiPrompt');
        if (!prompt) return;
        if (!guideSteps.length) {
            setStatus('Agregá al menos un paso al constructor.', 'warn');
            return;
        }
        prompt.value = buildIncrementalPhrase();
        var functional = buildFunctionalValidation();
        var issueCount = (functional.errors || []).length + (functional.warnings || []).length;
        var msg = runAfter ? 'Frase generada. Interpretando...' : 'Frase generada. Revisala y presioná Interpretar.';
        if (issueCount) msg += ' Validación funcional: ' + issueCount + ' advertencia(s) para revisar.';
        setStatus(msg, issueCount ? 'warn' : 'ok');
        if (runAfter) interpretar();
    }

    function fieldHtmlForType(type) {
        type = String(type || 'doc_load');
        if (type === 'doc_load') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Documento</label><select id="wfAiStepDoc" class="wf-ai-select">' + docOptions(firstDoc(['NOTA_CREDITO_ELECTRONICA_AR', 'FACTURA_ELECTRONICA_AR'])) + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Origen archivo</label><input id="wfAiStepPath" class="wf-ai-input" value="input.filePath" /></div>';
        }
        if (type === 'condition') {
            var defaultField = defaultConditionField();
            var meta = findAvailableField(defaultField) || { type: 'texto' };
            return '' +
                '<div class="wf-ai-guide-row"><label>Tipo de condición</label><select id="wfAiStepConditionMode" class="wf-ai-select"><option value="simple">Simple: una regla</option><option value="compound">Compuesta: varias reglas</option></select></div>' +
                '<div id="wfAiStepConditionSimpleBox">' +
                '<div class="wf-ai-guide-row"><label>Campo a validar</label><select id="wfAiStepField" class="wf-ai-select">' + availableFieldOptions(defaultField) + '</select></div>' +
                '<div class="wf-ai-guide-hint" id="wfAiStepFieldTypeHint"></div>' +
                '<div class="wf-ai-guide-row"><label>Operador</label><select id="wfAiStepOperator" class="wf-ai-select">' + conditionOperatorOptions(meta.type, '') + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepValueRow"><label>Valor</label><input id="wfAiStepConditionValue" class="wf-ai-input" placeholder="Ej.: 100000, texto, fecha o ${otro.campo}" /></div>' +
                '</div>' +
                '<div id="wfAiStepConditionCompoundBox" style="display:none">' +
                '<div class="wf-ai-guide-row"><label>Modo</label><select id="wfAiStepRulesMode" class="wf-ai-select"><option value="all">Todas las reglas deben cumplirse / Y</option><option value="any">Cualquiera de las reglas debe cumplirse / O</option></select></div>' +
                '<div id="wfAiCompoundRules" class="wf-ai-compound-rules"></div>' +
                '<div class="wf-ai-guide-actions wf-ai-compound-actions"><button type="button" class="btn" id="wfAiAddCompoundRule">Agregar regla</button></div>' +
                '<div class="wf-ai-guide-note">Ejemplo: CAE no está vacío Y total mayor que 200000 Y itemsCount mayor que 0. Podés hacer clic en Datos disponibles para agregar reglas con campos reales.</div>' +
                '</div>' +
                '<div class="wf-ai-guide-note">Después hacé clic en SI CUMPLE o NO CUMPLE en la columna de pasos para agregar acciones dentro de cada rama.</div>';
        }
        if (type === 'human_task') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Destino</label><select id="wfAiStepTaskDestType" class="wf-ai-select"><option value="rol">Rol</option><option value="usuario">Usuario</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskRoleRow"><label>Rol</label><select id="wfAiStepRole" class="wf-ai-select"><option value="">— elegir rol —</option>' + roleOptions('') + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskUserRow" style="display:none"><label>Usuario</label>' + userInputHtml('wfAiStepTaskUser', '', false) + '</div>' +
                '<div class="wf-ai-guide-row"><label>Título</label><input id="wfAiStepTaskTitle" class="wf-ai-input" placeholder="Ej.: Revisar factura. Si queda vacío, se propone según el destino." /></div>' +
                '<div class="wf-ai-guide-row"><label>Descripción (opcional)</label><textarea id="wfAiStepTaskDescription" class="wf-ai-input wf-ai-textarea" placeholder="Detalle adicional para la persona que recibe la tarea"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Vencimiento en minutos (opcional)</label><input id="wfAiStepTaskDeadline" class="wf-ai-input" type="number" min="0" value="0" /></div>' +
                '<details class="wf-ai-guide-note"><summary>Opciones avanzadas</summary>' +
                '<div class="wf-ai-guide-row"><label>Estado pendiente (opcional)</label><input id="wfAiStepTaskPendingState" class="wf-ai-input" placeholder="Si queda vacío, usa el estado normal según el destino" /></div>' +
                '<div class="wf-ai-guide-note">El scope de la tarea se toma del contexto del workflow (input.scopeKey / sector); no se configura manualmente acá.</div>' +
                '</details>' +
                '<div class="wf-ai-guide-note">Después hacé clic en APTO o NO APTO en la columna de pasos para agregar acciones según el resultado humano.</div>';
        }
        if (type === 'email_send') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Para</label><input id="wfAiStepTo" class="wf-ai-input" placeholder="usuario@empresa.com" /></div>' +
                '<div class="wf-ai-guide-row"><label>Asunto</label><input id="wfAiStepSubject" class="wf-ai-input" value="Aviso Workflow Studio" /></div>' +
                '<div class="wf-ai-guide-row"><label>Cuerpo</label><input id="wfAiStepBody" class="wf-ai-input" value="Se generó un aviso desde Workflow Studio" /></div>';
        }
        if (type === 'notify') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Destino</label><select id="wfAiStepDestType" class="wf-ai-select"><option value="rol">Rol</option><option value="usuario">Usuario</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepRoleRow"><label>Rol</label><select id="wfAiStepRole" class="wf-ai-select">' + roleOptions(firstRole(['COMPRAS'])) + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepUserRow" style="display:none"><label>Usuario</label>' + userInputHtml('wfAiStepUser', firstUser(['OMARD\\OMARD'])) + '</div>' +
                '<div class="wf-ai-guide-row"><label>Asunto</label><input id="wfAiStepTitle" class="wf-ai-input" value="Aviso interno" /></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje</label><input id="wfAiStepMessage" class="wf-ai-input" value="Hay una novedad pendiente en el workflow" /></div>';
        }
        if (type === 'http_request') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Método</label><select id="wfAiStepHttpMethod" class="wf-ai-select"><option value="GET">GET</option><option value="POST">POST</option><option value="PUT">PUT</option><option value="PATCH">PATCH</option><option value="DELETE">DELETE</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>URL</label><input id="wfAiStepHttpUrl" class="wf-ai-input" value="/Api/Ping.ashx" placeholder="Ej.: /Api/Ping.ashx" /></div>' +
                '<div class="wf-ai-guide-row"><label>Body</label><textarea id="wfAiStepHttpBody" class="wf-ai-input wf-ai-textarea" placeholder="Opcional. Para GET dejalo vacío."></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Content-Type</label><input id="wfAiStepHttpContentType" class="wf-ai-input" value="application/json" /></div>' +
                '<div class="wf-ai-guide-row"><label>Timeout ms</label><input id="wfAiStepHttpTimeout" class="wf-ai-input" value="10000" /></div>' +
                '<div class="wf-ai-guide-row"><label>Falla con status &gt;=</label><input id="wfAiStepHttpFailMin" class="wf-ai-input" value="400" /></div>' +
                '<div class="wf-ai-guide-note">Usá preferentemente URLs relativas o internas. La respuesta deja disponibles payload.status, payload.body y payload.json para pasos siguientes.</div>';
        }
        if (type === 'sql_query') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Conexión</label><input id="wfAiStepSqlConnection" class="wf-ai-input" value="DefaultConnection" /></div>' +
                '<div class="wf-ai-guide-row"><label>SQL</label><textarea id="wfAiStepSqlQuery" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: SELECT 1"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Parámetros JSON</label><textarea id="wfAiStepSqlParams" class="wf-ai-input wf-ai-textarea" placeholder="Opcional. Ej.: {&quot;Id&quot;:150484}"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Máx. filas a guardar</label><input id="wfAiStepSqlMaxRows" class="wf-ai-input" value="100" /></div>' +
                '<div class="wf-ai-guide-note">Usa el nodo data.sql existente. En SELECT deja visibles sql.rows, sql.rowCount, sql.first y sql.scalar en Datos de la instancia.</div>';
        }
        if (type === 'file_read') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Ruta archivo</label><input id="wfAiStepFileReadPath" class="wf-ai-input" placeholder="Ej.: C:\\Temp\\entrada.txt o ${input.filePath}" /></div>' +
                '<div class="wf-ai-guide-row"><label>Guardar contenido en</label><input id="wfAiStepFileReadOutput" class="wf-ai-input" value="archivo" placeholder="Ej.: archivo.texto o biz.archivo.texto" /></div>' +
                '<div class="wf-ai-guide-row"><label>Leer como JSON</label><select id="wfAiStepFileReadAsJson" class="wf-ai-select"><option value="false">No, texto</option><option value="true">Sí, JSON</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Encoding</label><input id="wfAiStepFileReadEncoding" class="wf-ai-input" value="utf-8" /></div>' +
                '<div class="wf-ai-guide-row"><label>Compresión</label><select id="wfAiStepFileReadZipMode" class="wf-ai-select"><option value="auto">Auto</option><option value="none">Sin compresión</option><option value="zip">ZIP</option><option value="gzip">GZIP</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Entrada ZIP</label><input id="wfAiStepFileReadZipEntry" class="wf-ai-input" placeholder="Opcional. Ej.: datos.json" /></div>' +
                '<div class="wf-ai-guide-note">Usa el handler real file.read. Deja disponible el contenido en la variable que indiques y metadatos como file.read.lastPath y file.read.lastLength.</div>';
        }
        if (type === 'file_write') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Ruta destino</label><input id="wfAiStepFileWritePath" class="wf-ai-input" placeholder="Ej.: C:\\Temp\\salida.txt" /></div>' +
                '<div class="wf-ai-guide-row"><label>Origen contenido</label><select id="wfAiStepFileWriteSourceMode" class="wf-ai-select"><option value="manual">Texto manual / plantilla</option><option value="context">Variable de DatosContexto</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepFileWriteOriginFieldRow"><label>Variable origen</label><select id="wfAiStepFileWriteOriginField" class="wf-ai-select">' + availableFieldOptionsWithBlank('', '— seleccionar variable —') + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepFileWriteOriginManualRow"><label>O escribir origen manual</label><input id="wfAiStepFileWriteOrigin" class="wf-ai-input" placeholder="Ej.: archivo o biz.compra" /></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepFileWriteContentRow"><label>Contenido</label><textarea id="wfAiStepFileWriteContent" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: Instancia ${wf.instanceId}, estado ${wf.estado}"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Sobrescribir si existe</label><select id="wfAiStepFileWriteOverwrite" class="wf-ai-select"><option value="true">Sí</option><option value="false">No</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Encoding</label><input id="wfAiStepFileWriteEncoding" class="wf-ai-input" value="utf-8" /></div>' +
                '<div class="wf-ai-guide-row"><label>ZIP</label><select id="wfAiStepFileWriteZipMode" class="wf-ai-select"><option value="none">No</option><option value="zip">Sí, escribir ZIP</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Entrada ZIP</label><input id="wfAiStepFileWriteEntry" class="wf-ai-input" placeholder="Opcional. Ej.: salida.txt" /></div>' +
                '<div class="wf-ai-guide-note">Usa el handler real file.write. Si elegís variable de DatosContexto, el sistema escribe el valor de esa variable; si es objeto, lo serializa como JSON.</div>';
        }
        if (type === 'queue_publish') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Nombre de cola</label><input id="wfAiStepQueueName" class="wf-ai-input" value="" placeholder="Ej.: documentos-pendientes" maxlength="100" /></div>' +
                '<div class="wf-ai-guide-row"><label>¿Qué querés publicar?</label><select id="wfAiStepQueuePayloadMode" class="wf-ai-select"><option value="simple">Mensaje simple</option><option value="fields">Campos del mensaje</option><option value="advanced">Texto / JSON avanzado</option></select></div>' +
                '<div id="wfAiQueuePayloadSimpleBox">' +
                '  <div class="wf-ai-guide-row"><label>Mensaje</label><textarea id="wfAiStepQueuePayloadSimple" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: Quiero un mate"></textarea></div>' +
                '  <div class="wf-ai-guide-note">Publicá texto directamente. No hace falta conocer JSON ni nombres internos del nodo.</div>' +
                '</div>' +
                '<div id="wfAiQueuePayloadGuidedBox" style="display:none">' +
                '  <div class="wf-ai-guide-note">Definí sólo los campos de negocio que necesites, por ejemplo origen, documentoId o importe. El valor puede ser texto o un dato disponible.</div>' +
                '  <div id="wfAiQueuePayloadFields" class="wf-ai-queue-fields"></div>' +
                '  <button type="button" class="btn wf-ai-queue-add" id="wfAiAddQueuePayloadField">Agregar campo</button>' +
                '  <div class="wf-ai-queue-preview-title">Vista previa del mensaje</div>' +
                '  <pre id="wfAiQueuePayloadPreview" class="wf-ai-queue-preview"></pre>' +
                '</div>' +
                '<div id="wfAiQueuePayloadAdvancedBox" style="display:none">' +
                '  <div class="wf-ai-guide-row"><label>Contenido avanzado</label><textarea id="wfAiStepQueuePayload" class="wf-ai-input wf-ai-textarea" placeholder="Texto libre o JSON"></textarea></div>' +
                '</div>' +
                '<details class="wf-ai-guide-note"><summary>Opciones técnicas</summary>' +
                '  <div class="wf-ai-guide-row"><label>Broker</label><input id="wfAiStepQueueBroker" class="wf-ai-input" value="sql" readonly /></div>' +
                '  <div class="wf-ai-guide-row"><label>Conexión</label><input id="wfAiStepQueueConnection" class="wf-ai-input" value="DefaultConnection" /></div>' +
                '</details>' +
                '<div class="wf-ai-guide-note">Después de publicar quedan disponibles queue.last.* y, cuando el mensaje tiene campos, queue.last.payload.*.</div>';
        }
        if (type === 'queue_consume') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Broker</label><input id="wfAiStepQueueBroker" class="wf-ai-input" value="sql" readonly /></div>' +
                '<div class="wf-ai-guide-row"><label>Nombre de cola</label><input id="wfAiStepQueueName" class="wf-ai-input" value="" placeholder="Ej.: documentos-pendientes" maxlength="100" /></div>' +
                '<div class="wf-ai-guide-row"><label>Cantidad a tomar</label><input id="wfAiStepQueueTake" class="wf-ai-input" type="number" min="1" max="100" value="1" /></div>' +
                '<div class="wf-ai-guide-row"><label>Conexión</label><input id="wfAiStepQueueConnection" class="wf-ai-input" value="DefaultConnection" /></div>' +
                '<div class="wf-ai-guide-row"><label>Logs de diagnóstico</label><select id="wfAiStepQueueDebug" class="wf-ai-select"><option value="false">No</option><option value="true">Sí</option></select></div>' +
                '<div class="wf-ai-guide-note">Consume mensajes pendientes de WF_Queue mediante queue.consume. Prefetch y outputPrefix se mantienen con los valores compatibles del nodo actual. Después deja disponibles queue.hasMessage, queue.messageId, queue.message, queue.messages, payload y queue.error.</div>';
        }
        if (type === 'subflow') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Workflow a ejecutar</label><select id="wfAiStepSubflowRef" class="wf-ai-select">' + workflowDefOptions(firstWorkflowDef()) + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Alias opcional</label><input id="wfAiStepSubflowAlias" class="wf-ai-input" placeholder="Ej.: validacion, compras, control" /></div>' +
                '<div class="wf-ai-guide-row"><label>Input JSON</label><textarea id="wfAiStepSubflowInput" class="wf-ai-input wf-ai-textarea" placeholder="{&#10;  &quot;filePath&quot;: &quot;${input.filePath}&quot;,&#10;  &quot;importe&quot;: &quot;${biz.notaCredito.total}&quot;&#10;}">{&#10;  &quot;filePath&quot;: &quot;${input.filePath}&quot;&#10;}</textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Profundidad máxima</label><input id="wfAiStepSubflowMaxDepth" class="wf-ai-input" value="10" /></div>' +
                '<div class="wf-ai-guide-note">Ejecuta una definición existente creando una instancia hija. Después podés usar ${subflow.instanceId}, ${subflow.childState} y, si usás alias, ${subflows.alias.childState}.</div>';
        }
        if (type === 'state_set') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Modo</label><select id="wfAiStepStateMode" class="wf-ai-select"><option value="simple">Simple: una variable</option><option value="json">Avanzado: JSON / varios datos</option></select></div>' +
                '<div id="wfAiStepStateSimpleBox">' +
                '<div class="wf-ai-guide-row"><label>Variable destino</label><input id="wfAiStepKey" class="wf-ai-input" placeholder="Ej.: biz.prueba.fix30" /></div>' +
                '<div class="wf-ai-guide-row"><label>Copiar desde dato disponible</label><select id="wfAiStepValueField" class="wf-ai-select">' + availableFieldOptionsWithBlank('', '— escribir valor manual —') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Valor</label><textarea id="wfAiStepValue" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: OK_FIX30, ${sql.rowCount} o ${payload.status}"></textarea></div>' +
                '<div class="wf-ai-guide-note">El destino es el nombre real de la variable. Ej.: biz.prueba.fix30. No escribas “=” acá.</div>' +
                '</div>' +
                '<div id="wfAiStepStateJsonBox" style="display:none">' +
                '<div class="wf-ai-guide-row"><label>JSON a guardar</label><textarea id="wfAiStepSetJson" class="wf-ai-input wf-ai-textarea" placeholder="{&#10;  &quot;biz.prueba.fix30&quot;: &quot;OK_FIX30&quot;,&#10;  &quot;biz.compra&quot;: { &quot;estado&quot;: &quot;Pendiente&quot;, &quot;importe&quot;: 150000 }&#10;}"></textarea></div>' +
                '<div class="wf-ai-guide-note">Usá este modo para guardar varios datos, objetos o arrays. Debe ser un JSON objeto válido.</div>' +
                '</div>' +
                '<div class="wf-ai-guide-note">Usá variables propias como biz.* o wf.vars.*. No conviene pisar salidas técnicas como sql.*, payload.*, logger.*, notify.*.</div>';
        }
        if (type === 'state_remove') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Variable a quitar</label><input id="wfAiStepKey" class="wf-ai-input" placeholder="Ej.: biz.prueba.fix30" /></div>' +
                '<div class="wf-ai-guide-note">Quita una variable de DatosContexto. Para evitar confusión, usalo principalmente sobre variables creadas por vos.</div>';
        }
        if (type === 'delay') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Milisegundos</label><input id="wfAiStepMs" class="wf-ai-input" value="1000" /></div>';
        }
        if (type === 'retry') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Reintentos</label><input id="wfAiStepRetryCount" class="wf-ai-input" value="3" /></div>' +
                '<div class="wf-ai-guide-row"><label>Backoff ms</label><input id="wfAiStepRetryBackoff" class="wf-ai-input" value="500" /></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje opcional</label><input id="wfAiStepRetryMessage" class="wf-ai-input" value="Reintento agregado por Constructor IA" /></div>' +
                '<div class="wf-ai-guide-note">Reintentar ejecuta el nodo siguiente. Ubicalo antes del paso que puede fallar. Reintenta si el siguiente nodo devuelve error o lanza excepción. Para HTTP, dejá activo “Falla con status &gt;= 400”.</div>';
        }
        if (type === 'error_handler') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Mensaje de error</label><textarea id="wfAiStepErrorMessage" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: Falló la consulta SQL. Instancia ${wf.instanceId}">Error controlado por el workflow</textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Continuar luego de marcar error</label><select id="wfAiStepErrorCapture" class="wf-ai-select"><option value="true">Sí, continuar por salida normal</option><option value="false">No, usar salida error si existe</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Dejar aviso en log/contexto</label><select id="wfAiStepErrorNotify" class="wf-ai-select"><option value="false">No</option><option value="true">Sí</option></select></div>' +
                '<div class="wf-ai-guide-row"><label>Volver a intentar</label><select id="wfAiStepErrorRetry" class="wf-ai-select"><option value="false">No</option><option value="true">Sí, solo marca intención</option></select></div>' +
                '<div class="wf-ai-guide-note">Este nodo usa util.error existente: marca wf.error en DatosContexto. Si se ejecuta, la instancia queda marcada como Error al finalizar. Para reintentos reales agregá el paso Reintentar antes del nodo que puede fallar.</div>';
        }
        if (type === 'logger') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Nivel</label><select id="wfAiStepLogLevel" class="wf-ai-select">' +
                '  <option value="Info">Info</option>' +
                '  <option value="Warn">Warn</option>' +
                '  <option value="Error">Error</option>' +
                '  <option value="Debug">Debug</option>' +
                '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje</label><textarea id="wfAiStepMessage" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: Operación completada o Total=${sql.rowCount}"></textarea></div>' +
                '<div class="wf-ai-guide-note">Podés usar variables con ${...}, por ejemplo ${wf.instanceId}, ${payload.status}, ${sql.rowCount} o campos de Datos disponibles.</div>';
        }
        if (type === 'end') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-note">Agrega el cierre del flujo. Normalmente conviene dejarlo como último paso.</div>';
        }
        return '';
    }

    function syncFileWriteModeFields() {
        var mode = $('wfAiStepFileWriteSourceMode');
        var originFieldRow = $('wfAiStepFileWriteOriginFieldRow');
        var originManualRow = $('wfAiStepFileWriteOriginManualRow');
        var contentRow = $('wfAiStepFileWriteContentRow');
        if (!mode) return;
        var useContext = mode.value === 'context';
        if (originFieldRow) originFieldRow.style.display = useContext ? '' : 'none';
        if (originManualRow) originManualRow.style.display = useContext ? '' : 'none';
        if (contentRow) contentRow.style.display = useContext ? 'none' : '';
    }

    function syncDynamicStepFields() {
        var condition = $('wfAiStepCondition');
        var amountRow = $('wfAiStepAmountRow');
        if (condition && amountRow) amountRow.style.display = condition.value === 'total_gt' ? '' : 'none';
        syncConditionModeFields();
        syncConditionFields();

        var dest = $('wfAiStepDestType');
        var roleRow = $('wfAiStepRoleRow');
        var userRow = $('wfAiStepUserRow');
        if (dest && roleRow && userRow) {
            roleRow.style.display = dest.value === 'rol' ? '' : 'none';
            userRow.style.display = dest.value === 'usuario' ? '' : 'none';
        }

        var taskDest = $('wfAiStepTaskDestType');
        var taskRoleRow = $('wfAiStepTaskRoleRow');
        var taskUserRow = $('wfAiStepTaskUserRow');
        if (taskDest && taskRoleRow && taskUserRow) {
            taskRoleRow.style.display = taskDest.value === 'rol' ? '' : 'none';
            taskUserRow.style.display = taskDest.value === 'usuario' ? '' : 'none';
        }

        syncFileWriteModeFields();
        syncStateSetModeFields();
        syncQueuePayloadModeFields();
    }

    function setControlValue(id, value) {
        var el = $(id);
        if (!el) return;
        el.value = value == null ? '' : String(value);
    }

    function updateGuideEditMode() {
        var add = $('wfAiGuideAdd');
        var cancel = $('wfAiGuideCancelEdit');
        var type = $('wfAiStepType');
        var editing = editingStepIndex >= 0 && editingStepIndex < guideSteps.length;

        if (add) add.textContent = editing ? 'Guardar cambios' : 'Agregar paso';
        if (cancel) cancel.style.display = editing ? '' : 'none';
        if (type) type.title = editing ? 'Podés cambiar el tipo de paso o corregir sus propiedades.' : '';
    }

    function fillStepForm(step) {
        if (!step) return;

        setControlValue('wfAiStepBranch', step.branch || 'then');

        if (step.type === 'doc_load') {
            setControlValue('wfAiStepDoc', step.docTipo || firstDoc(['NOTA_CREDITO_ELECTRONICA_AR', 'FACTURA_ELECTRONICA_AR']));
            setControlValue('wfAiStepPath', step.path || 'input.filePath');
        } else if (step.type === 'condition') {
            var existingRules = conditionRuleList(step);
            if (existingRules.length) {
                setControlValue('wfAiStepConditionMode', 'compound');
                setControlValue('wfAiStepRulesMode', step.rulesMode || 'all');
                syncConditionModeFields();
                renderCompoundRules(existingRules);
            } else if (step.fieldPath) {
                setControlValue('wfAiStepConditionMode', 'simple');
                syncConditionModeFields();
                setControlValue('wfAiStepField', step.fieldPath);
                syncConditionFields();
                setControlValue('wfAiStepOperator', step.operator || 'not_empty');
                syncConditionFields();
                setControlValue('wfAiStepConditionValue', step.value || '');
            } else {
                setControlValue('wfAiStepConditionMode', 'simple');
                syncConditionModeFields();
                // Compatibilidad con pasos creados antes de fix24.
                var fallback = defaultConditionField();
                if (step.condition === 'cae') {
                    var fields = buildAvailableFields();
                    for (var i = 0; i < fields.length; i++) {
                        if (String(fields[i].path || '').toLowerCase().indexOf('.cae') >= 0) { fallback = fields[i].path; break; }
                    }
                    setControlValue('wfAiStepField', fallback);
                    syncConditionFields();
                    setControlValue('wfAiStepOperator', 'not_empty');
                } else {
                    setControlValue('wfAiStepField', fallback);
                    syncConditionFields();
                    setControlValue('wfAiStepOperator', '>');
                    setControlValue('wfAiStepConditionValue', step.amount || '100000');
                }
                syncConditionFields();
            }
        } else if (step.type === 'human_task') {
            setControlValue('wfAiStepTaskDestType', step.destType || (step.user ? 'usuario' : 'rol'));
            setControlValue('wfAiStepRole', step.role || '');
            setControlValue('wfAiStepTaskUser', step.user || '');
            setControlValue('wfAiStepTaskTitle', step.title || step.purpose || '');
            setControlValue('wfAiStepTaskDescription', step.description || '');
            setControlValue('wfAiStepTaskDeadline', step.deadlineMinutes == null ? '0' : step.deadlineMinutes);
            setControlValue('wfAiStepTaskPendingState', step.pendingState || '');
        } else if (step.type === 'email_send') {
            setControlValue('wfAiStepTo', step.to || 'destinatario@empresa.com');
            setControlValue('wfAiStepSubject', step.subject || 'Aviso Workflow Studio');
            setControlValue('wfAiStepBody', step.body || 'Se generó un aviso desde Workflow Studio');
        } else if (step.type === 'notify') {
            setControlValue('wfAiStepDestType', step.destType || 'rol');
            setControlValue('wfAiStepRole', step.role || firstRole(['COMPRAS']));
            setControlValue('wfAiStepUser', step.user || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD');
            setControlValue('wfAiStepTitle', step.title || 'Aviso interno');
            setControlValue('wfAiStepMessage', step.message || 'Hay una novedad pendiente en el workflow');
        } else if (step.type === 'http_request') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepHttpMethod', step.method || 'GET');
            setControlValue('wfAiStepHttpUrl', step.url || '/Api/Ping.ashx');
            setControlValue('wfAiStepHttpBody', step.body || '');
            setControlValue('wfAiStepHttpContentType', step.contentType || 'application/json');
            setControlValue('wfAiStepHttpTimeout', step.timeoutMs || '10000');
            setControlValue('wfAiStepHttpFailMin', step.failStatusMin || '400');
        } else if (step.type === 'sql_query') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepSqlConnection', step.connectionStringName || 'DefaultConnection');
            setControlValue('wfAiStepSqlQuery', step.query || 'SELECT 1');
            setControlValue('wfAiStepSqlParams', step.paramsJson || '');
            setControlValue('wfAiStepSqlMaxRows', step.maxRows || '100');
        } else if (step.type === 'file_read') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepFileReadPath', step.path || '');
            setControlValue('wfAiStepFileReadOutput', step.output || 'archivo');
            setControlValue('wfAiStepFileReadAsJson', step.asJson ? 'true' : 'false');
            setControlValue('wfAiStepFileReadEncoding', step.encoding || 'utf-8');
            setControlValue('wfAiStepFileReadZipMode', step.zipMode || 'auto');
            setControlValue('wfAiStepFileReadZipEntry', step.zipEntry || '');
        } else if (step.type === 'file_write') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepFileWritePath', step.path || '');
            setControlValue('wfAiStepFileWriteSourceMode', step.sourceMode || 'manual');
            setControlValue('wfAiStepFileWriteOriginField', step.origen || '');
            setControlValue('wfAiStepFileWriteOrigin', step.origen || '');
            setControlValue('wfAiStepFileWriteContent', step.content || '');
            setControlValue('wfAiStepFileWriteOverwrite', step.overwrite === false ? 'false' : 'true');
            setControlValue('wfAiStepFileWriteEncoding', step.encoding || 'utf-8');
            setControlValue('wfAiStepFileWriteZipMode', step.zipMode || 'none');
            setControlValue('wfAiStepFileWriteEntry', step.entryName || '');
        } else if (step.type === 'queue_publish') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepQueueBroker', step.broker || 'sql');
            setControlValue('wfAiStepQueueName', step.queue || '');
            var queueMode = String(step.payloadMode || '').toLowerCase();
            var queueFields = queuePayloadFieldsForStep(step);
            if (!queueMode) {
                if (queueFields.length) queueMode = 'fields';
                else {
                    var existingPayload = queuePayloadValueFromStep(step);
                    queueMode = existingPayload && typeof existingPayload === 'object' ? 'advanced' : 'simple';
                }
            }
            setControlValue('wfAiStepQueuePayloadMode', queueMode);
            if (queueMode === 'fields') renderQueuePayloadRows(queueFields);
            else if (queueMode === 'simple') setControlValue('wfAiStepQueuePayloadSimple', step.payloadText || '');
            else setControlValue('wfAiStepQueuePayload', step.payloadText || '');
            setControlValue('wfAiStepQueueConnection', step.connectionStringName || 'DefaultConnection');
        } else if (step.type === 'queue_consume') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepQueueBroker', step.broker || 'sql');
            setControlValue('wfAiStepQueueName', step.queue || '');
            setControlValue('wfAiStepQueueTake', step.take || '1');
            setControlValue('wfAiStepQueueConnection', step.connectionStringName || 'DefaultConnection');
            setControlValue('wfAiStepQueueDebug', step.debug ? 'true' : 'false');
        } else if (step.type === 'subflow') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepSubflowRef', step.ref || '');
            setControlValue('wfAiStepSubflowAlias', step.alias || '');
            setControlValue('wfAiStepSubflowInput', step.inputJson || '{\n  "filePath": "${input.filePath}"\n}');
            setControlValue('wfAiStepSubflowMaxDepth', step.maxDepth || '10');
        } else if (step.type === 'state_set') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            var mode = isStateJsonMode(step) ? 'json' : 'simple';
            setControlValue('wfAiStepStateMode', mode);
            if (mode === 'json') {
                setControlValue('wfAiStepSetJson', step.setJson || '{}');
            } else {
                setControlValue('wfAiStepKey', step.key || '');
                setControlValue('wfAiStepValue', step.value || '');
            }
        } else if (step.type === 'state_remove') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepKey', step.key || '');
        } else if (step.type === 'delay') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepMs', step.ms || '1000');
        } else if (step.type === 'retry') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepRetryCount', step.reintentos || '3');
            setControlValue('wfAiStepRetryBackoff', step.backoffMs || '500');
            setControlValue('wfAiStepRetryMessage', step.message || 'Reintento agregado por Constructor IA');
        } else if (step.type === 'error_handler') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepErrorMessage', step.message || 'Error controlado por el workflow');
            setControlValue('wfAiStepErrorCapture', step.capture === false ? 'false' : 'true');
            setControlValue('wfAiStepErrorNotify', step.notify ? 'true' : 'false');
            setControlValue('wfAiStepErrorRetry', step.retry ? 'true' : 'false');
        } else if (step.type === 'logger') {
            setControlValue('wfAiStepBranch', step.branch || 'then');
            setControlValue('wfAiStepLogLevel', step.level || 'Info');
            setControlValue('wfAiStepMessage', step.message || '');
        }

        syncDynamicStepFields();
    }

    function beginEditGuideStep(idx) {
        if (idx < 0 || idx >= guideSteps.length) return;
        editingStepIndex = idx;
        var step = guideSteps[idx];
        var type = $('wfAiStepType');
        if (type) type.value = step.type || 'doc_load';
        renderStepFields();
        fillStepForm(step);
        updateGuideEditMode();
        renderGuideSteps();
        setStatus('Editando paso ' + (idx + 1) + '. Corregí los datos y presioná “Guardar cambios”.', 'ok');
        var fields = $('wfAiStepFields');
        if (fields && fields.scrollIntoView) fields.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    function cancelEditGuideStep() {
        editingStepIndex = -1;
        updateGuideEditMode();
        renderGuideSteps();
        setStatus('Edición cancelada.', 'ok');
    }

    function renderStepFields() {
        var type = selectedText('wfAiStepType') || 'doc_load';
        var box = $('wfAiStepFields');
        if (!box) return;
        box.innerHTML = fieldHtmlForType(type);
        if (type === 'queue_publish') renderQueuePayloadRows([]);

        var condition = $('wfAiStepCondition');
        if (condition) condition.addEventListener('change', syncDynamicStepFields);

        var conditionMode = $('wfAiStepConditionMode');
        if (conditionMode) conditionMode.addEventListener('change', syncDynamicStepFields);

        var addCompoundRule = $('wfAiAddCompoundRule');
        if (addCompoundRule) addCompoundRule.addEventListener('click', function (ev) {
            ev.preventDefault();
            var rules = collectCompoundRulesFromForm();
            rules.push(defaultCompoundRule());
            renderCompoundRules(rules);
        });

        var field = $('wfAiStepField');
        if (field) field.addEventListener('change', syncConditionFields);

        var operator = $('wfAiStepOperator');
        if (operator) operator.addEventListener('change', syncConditionFields);

        var dest = $('wfAiStepDestType');
        if (dest) dest.addEventListener('change', syncDynamicStepFields);

        var taskDest = $('wfAiStepTaskDestType');
        if (taskDest) taskDest.addEventListener('change', syncDynamicStepFields);

        var stateValueField = $('wfAiStepValueField');
        if (stateValueField) stateValueField.addEventListener('change', syncStateValueFromField);

        var stateMode = $('wfAiStepStateMode');
        if (stateMode) stateMode.addEventListener('change', syncDynamicStepFields);

        var fileWriteMode = $('wfAiStepFileWriteSourceMode');
        if (fileWriteMode) fileWriteMode.addEventListener('change', syncDynamicStepFields);

        var queuePayloadMode = $('wfAiStepQueuePayloadMode');
        if (queuePayloadMode) queuePayloadMode.addEventListener('change', syncDynamicStepFields);

        var addQueuePayloadField = $('wfAiAddQueuePayloadField');
        if (addQueuePayloadField) addQueuePayloadField.addEventListener('click', function (ev) {
            ev.preventDefault();
            addQueuePayloadFieldRow({ name: '', value: '' });
            var rows = $('wfAiQueuePayloadFields');
            var lastName = rows && rows.querySelector('[data-queue-payload-row]:last-child [data-queue-payload-name]');
            if (lastName && lastName.focus) lastName.focus();
        });

        syncDynamicStepFields();
        renderActiveTargetBox();
        updateGuideEditMode();
    }

    function validateCurrentStepForm() {
        var type = selectedText('wfAiStepType') || 'doc_load';

        if (type === 'condition') {
            var conditionMode = selectedText('wfAiStepConditionMode') || 'simple';
            if (conditionMode === 'compound') {
                var rules = collectCompoundRulesFromForm();
                if (!rules.length) return 'La condición compuesta debe tener al menos una regla.';
                for (var cr = 0; cr < rules.length; cr++) {
                    if (!rules[cr].fieldPath) return 'La regla ' + (cr + 1) + ' debe tener un campo.';
                    if (operatorNeedsValue(rules[cr].operator) && !String(rules[cr].value || '').trim()) {
                        return 'La regla ' + (cr + 1) + ' necesita un valor para el operador seleccionado.';
                    }
                }
            } else {
                if (!selectedText('wfAiStepField')) return 'Seleccioná el campo a validar.';
                if (operatorNeedsValue(selectedText('wfAiStepOperator')) && !selectedText('wfAiStepConditionValue')) {
                    return 'El operador seleccionado necesita un valor.';
                }
            }
        }

        if (type === 'notify' && selectedText('wfAiStepDestType') === 'usuario') {
            var notifyUser = resolveUserSelection(selectedText('wfAiStepUser'));
            if (!notifyUser) {
                var nf = $('wfAiStepUser');
                if (nf && nf.focus) nf.focus();
                return 'Seleccioná un usuario válido de la lista para la notificación interna.';
            }
            setControlValue('wfAiStepUser', notifyUser);
        }

        if (type === 'human_task') {
            var taskDestType = selectedText('wfAiStepTaskDestType') || 'rol';
            if (taskDestType === 'usuario') {
                var taskUser = resolveUserSelection(selectedText('wfAiStepTaskUser'));
                if (!taskUser) {
                    var tf = $('wfAiStepTaskUser');
                    if (tf && tf.focus) tf.focus();
                    return 'Seleccioná un usuario válido de la lista para la tarea humana.';
                }
                setControlValue('wfAiStepTaskUser', taskUser);
            } else if (!selectedText('wfAiStepRole')) {
                var tr = $('wfAiStepRole');
                if (tr && tr.focus) tr.focus();
                return 'Seleccioná un rol real para la tarea humana.';
            }

            var deadlineText = selectedText('wfAiStepTaskDeadline');
            if (deadlineText && (!/^\d+$/.test(deadlineText) || parseInt(deadlineText, 10) < 0)) {
                var td = $('wfAiStepTaskDeadline');
                if (td && td.focus) td.focus();
                return 'El vencimiento debe ser una cantidad de minutos mayor o igual a 0.';
            }
        }

        if (type === 'sql_query') {
            if (parseJsonObjectOrEmpty(selectedText('wfAiStepSqlParams')) === null) {
                var sf = $('wfAiStepSqlParams');
                if (sf && sf.focus) sf.focus();
                return 'Los parámetros SQL deben ser un objeto JSON válido. Ejemplo: {"Id":150484}. Dejalo vacío si no usás parámetros.';
            }
        }

        if (type === 'file_read') {
            if (!selectedText('wfAiStepFileReadPath')) {
                var frp = $('wfAiStepFileReadPath');
                if (frp && frp.focus) frp.focus();
                return 'Indicá la ruta del archivo a leer.';
            }
            if (!isValidStatePath(selectedText('wfAiStepFileReadOutput') || '')) {
                var fro = $('wfAiStepFileReadOutput');
                if (fro && fro.focus) fro.focus();
                return 'Indicá una variable válida para guardar el contenido leído. Ejemplo: archivo.texto o biz.archivo.texto.';
            }
        }

        if (type === 'file_write') {
            if (!selectedText('wfAiStepFileWritePath')) {
                var fwp = $('wfAiStepFileWritePath');
                if (fwp && fwp.focus) fwp.focus();
                return 'Indicá la ruta del archivo a escribir.';
            }
            var wm = selectedText('wfAiStepFileWriteSourceMode') || 'manual';
            var origin = selectedText('wfAiStepFileWriteOriginField') || selectedText('wfAiStepFileWriteOrigin');
            if (wm === 'context' && !isValidStatePath(origin || '')) {
                var fwo = $('wfAiStepFileWriteOrigin');
                if (fwo && fwo.focus) fwo.focus();
                return 'Indicá una variable origen válida para escribir. Ejemplo: archivo.texto o biz.compra.';
            }
            if (wm !== 'context' && !selectedText('wfAiStepFileWriteContent')) {
                var fwc = $('wfAiStepFileWriteContent');
                if (fwc && fwc.focus) fwc.focus();
                return 'Indicá el contenido a escribir o cambiá el origen a Variable de DatosContexto.';
            }
        }

        if (type === 'logger' && !selectedText('wfAiStepMessage')) {
            var lmf = $('wfAiStepMessage');
            if (lmf && lmf.focus) lmf.focus();
            return 'Indicá qué querés registrar en el log.';
        }

        if (type === 'queue_publish' || type === 'queue_consume') {
            var queueName = selectedText('wfAiStepQueueName');
            if (!queueName) {
                var qnf = $('wfAiStepQueueName');
                if (qnf && qnf.focus) qnf.focus();
                return 'Indicá el nombre de la cola.';
            }
            if (queueName.length > 100) {
                var qnl = $('wfAiStepQueueName');
                if (qnl && qnl.focus) qnl.focus();
                return 'El nombre de la cola no puede superar 100 caracteres.';
            }
            if (type === 'queue_consume' && !selectedText('wfAiStepQueueConnection')) {
                var qcf = $('wfAiStepQueueConnection');
                if (qcf && qcf.focus) qcf.focus();
                return 'Indicá el nombre de la conexión SQL.';
            }
        }

        if (type === 'queue_publish') {
            var queuePayloadMode = selectedText('wfAiStepQueuePayloadMode') || 'simple';
            if (queuePayloadMode === 'simple') {
                var simplePayload = selectedText('wfAiStepQueuePayloadSimple');
                if (!simplePayload) {
                    var qps = $('wfAiStepQueuePayloadSimple');
                    if (qps && qps.focus) qps.focus();
                    return 'Indicá qué información querés publicar.';
                }
            } else if (queuePayloadMode === 'fields') {
                var queueFields = collectQueuePayloadFieldsFromForm();
                if (!queueFields.length) return 'Agregá al menos un campo al mensaje.';
                var queueNames = {};
                for (var qfi = 0; qfi < queueFields.length; qfi++) {
                    var queueField = queueFields[qfi];
                    if (!queuePayloadFieldNameIsValid(queueField.name)) {
                        var invalidName = $('wfAiQueuePayloadFields') && $('wfAiQueuePayloadFields').querySelectorAll('[data-queue-payload-name]')[qfi];
                        if (invalidName && invalidName.focus) invalidName.focus();
                        return 'El nombre del campo debe comenzar con letra o _ y usar sólo letras, números o _. Ejemplo: origen o documentoId.';
                    }
                    var queueNameKey = normalizeKey(queueField.name);
                    if (queueNames[queueNameKey]) return 'El campo "' + queueField.name + '" está repetido.';
                    queueNames[queueNameKey] = true;
                }
            } else {
                var advancedPayload = selectedText('wfAiStepQueuePayload');
                if (!advancedPayload) {
                    var qpf = $('wfAiStepQueuePayload');
                    if (qpf && qpf.focus) qpf.focus();
                    return 'Indicá el texto o JSON avanzado que querés publicar.';
                }
                var advancedTrim = advancedPayload.trim();
                if ((advancedTrim.charAt(0) === '{' || advancedTrim.charAt(0) === '[')) {
                    try { JSON.parse(advancedTrim); }
                    catch (queueJsonError) {
                        var qpj = $('wfAiStepQueuePayload');
                        if (qpj && qpj.focus) qpj.focus();
                        return 'El contenido parece JSON pero no es válido. Corregilo o usá Campos del mensaje.';
                    }
                }
            }
        }

        if (type === 'queue_consume') {
            var qt = parseInt(selectedText('wfAiStepQueueTake') || '1', 10);
            if (isNaN(qt) || qt < 1 || qt > 100) {
                var qtf = $('wfAiStepQueueTake');
                if (qtf && qtf.focus) qtf.focus();
                return 'Cantidad a tomar debe ser un número entre 1 y 100.';
            }
        }

        if (type === 'subflow') {
            if (!selectedText('wfAiStepSubflowRef')) {
                var sr = $('wfAiStepSubflowRef');
                if (sr && sr.focus) sr.focus();
                return 'Seleccioná el workflow que querés ejecutar como subflujo.';
            }
            if (parseJsonObjectOrEmpty(selectedText('wfAiStepSubflowInput') || '{}') === null) {
                var si = $('wfAiStepSubflowInput');
                if (si && si.focus) si.focus();
                return 'El Input JSON del subflujo debe ser un objeto JSON válido.';
            }
            var sa = selectedText('wfAiStepSubflowAlias');
            if (sa && !subflowAliasIsValid(sa)) {
                var sfAlias = $('wfAiStepSubflowAlias');
                if (sfAlias && sfAlias.focus) sfAlias.focus();
                return 'El alias del subflujo debe usar letras/números/_ y no puede empezar con número.';
            }
            var smd = parseInt(selectedText('wfAiStepSubflowMaxDepth') || '10', 10);
            if (isNaN(smd) || smd < 1 || smd > 50) {
                var smf = $('wfAiStepSubflowMaxDepth');
                if (smf && smf.focus) smf.focus();
                return 'La profundidad máxima del subflujo debe estar entre 1 y 50.';
            }
        }

        if (type === 'state_set') {
            var stateMode = selectedText('wfAiStepStateMode') || 'simple';
            if (stateMode === 'json') {
                var setObj = parseJsonObjectOrEmpty(selectedText('wfAiStepSetJson'));
                if (setObj === null || !Object.keys(setObj).length) {
                    var jf = $('wfAiStepSetJson');
                    if (jf && jf.focus) jf.focus();
                    return 'El JSON a guardar debe ser un objeto válido y tener al menos una propiedad. Ejemplo: {"biz.prueba.fix30":"OK_FIX30"}';
                }
                var badKeys = Object.keys(setObj).filter(function (k) { return !isValidStatePath(k); });
                if (badKeys.length) return 'Estas variables destino no tienen formato válido: ' + badKeys.join(', ');
            } else {
                var key = selectedText('wfAiStepKey');
                if (!isValidStatePath(key)) {
                    var kf = $('wfAiStepKey');
                    if (kf && kf.focus) kf.focus();
                    return 'La variable destino debe tener formato de ruta simple, por ejemplo biz.prueba.fix30 o wf.vars.estado. No uses espacios ni ${...} en el nombre.';
                }
                if (!selectedText('wfAiStepValue')) {
                    var vf = $('wfAiStepValue');
                    if (vf && vf.focus) vf.focus();
                    return 'Indicá el valor a guardar para la variable.';
                }
            }
        }

        if (type === 'state_remove' && !isValidStatePath(selectedText('wfAiStepKey'))) {
            var rf = $('wfAiStepKey');
            if (rf && rf.focus) rf.focus();
            return 'La variable a quitar debe tener formato de ruta simple, por ejemplo biz.prueba.fix30 o wf.vars.estado.';
        }

        if (type === 'retry') {
            var rr = parseInt(selectedText('wfAiStepRetryCount') || '3', 10);
            var rb = parseInt(selectedText('wfAiStepRetryBackoff') || '500', 10);
            if (isNaN(rr) || rr < 0 || rr > 50) {
                var rcf = $('wfAiStepRetryCount');
                if (rcf && rcf.focus) rcf.focus();
                return 'Reintentos debe ser un número entre 0 y 50.';
            }
            if (isNaN(rb) || rb < 0 || rb > 600000) {
                var rbf = $('wfAiStepRetryBackoff');
                if (rbf && rbf.focus) rbf.focus();
                return 'Backoff ms debe ser un número entre 0 y 600000.';
            }
        }

        if (type === 'error_handler' && !selectedText('wfAiStepErrorMessage')) {
            var ef = $('wfAiStepErrorMessage');
            if (ef && ef.focus) ef.focus();
            return 'Indicá el mensaje de error que quedará registrado en wf.error.message.';
        }

        return '';
    }

    function collectStepFromForm(existingId) {
        var type = selectedText('wfAiStepType') || 'doc_load';
        var step = { id: existingId || guideSeq++, type: type };

        if (type === 'doc_load') {
            step.docTipo = selectedText('wfAiStepDoc') || firstDoc(['NOTA_CREDITO_ELECTRONICA_AR']);
            step.path = selectedText('wfAiStepPath') || 'input.filePath';
        } else if (type === 'condition') {
            var conditionMode = selectedText('wfAiStepConditionMode') || 'simple';
            if (conditionMode === 'compound') {
                step.rulesMode = selectedText('wfAiStepRulesMode') || 'all';
                step.rules = collectCompoundRulesFromForm();
                step.condition = 'compound';
            } else {
                var fieldPath = selectedText('wfAiStepField') || defaultConditionField();
                var meta = findAvailableField(fieldPath) || {};
                step.fieldPath = fieldPath;
                step.fieldLabel = meta.label || fieldPath;
                step.fieldType = meta.type || 'texto';
                step.operator = selectedText('wfAiStepOperator') || 'not_empty';
                step.value = selectedText('wfAiStepConditionValue') || '';
                // Compatibilidad para textos antiguos y para el parser existente.
                if (String(fieldPath || '').toLowerCase().indexOf('.total') >= 0 && step.operator === '>') {
                    step.condition = 'total_gt';
                    step.amount = step.value || '100000';
                } else if (String(fieldPath || '').toLowerCase().indexOf('.cae') >= 0 && step.operator === 'not_empty') {
                    step.condition = 'cae';
                }
            }
        } else if (type === 'human_task') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.destType = selectedText('wfAiStepTaskDestType') || 'rol';
            step.role = step.destType === 'rol' ? (selectedText('wfAiStepRole') || '') : '';
            step.user = step.destType === 'usuario' ? (selectedText('wfAiStepTaskUser') || '') : '';
            step.title = selectedText('wfAiStepTaskTitle') || '';
            step.purpose = step.title;
            step.description = selectedText('wfAiStepTaskDescription') || '';
            step.deadlineMinutes = parseInt(selectedText('wfAiStepTaskDeadline') || '0', 10) || 0;
            step.pendingState = selectedText('wfAiStepTaskPendingState') || '';
        } else if (type === 'email_send') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.to = selectedText('wfAiStepTo') || 'destinatario@empresa.com';
            step.subject = selectedText('wfAiStepSubject') || 'Aviso Workflow Studio';
            step.body = selectedText('wfAiStepBody') || 'Se generó un aviso desde Workflow Studio';
        } else if (type === 'notify') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.destType = selectedText('wfAiStepDestType') || 'rol';
            step.role = selectedText('wfAiStepRole') || firstRole(['COMPRAS']);
            step.user = selectedText('wfAiStepUser') || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD';
            step.title = selectedText('wfAiStepTitle') || 'Aviso interno';
            step.message = selectedText('wfAiStepMessage') || 'Hay una novedad pendiente en el workflow';
        } else if (type === 'http_request') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.method = selectedText('wfAiStepHttpMethod') || 'GET';
            step.url = selectedText('wfAiStepHttpUrl') || '/Api/Ping.ashx';
            step.body = selectedText('wfAiStepHttpBody') || '';
            step.contentType = selectedText('wfAiStepHttpContentType') || 'application/json';
            step.timeoutMs = selectedText('wfAiStepHttpTimeout') || '10000';
            step.failStatusMin = selectedText('wfAiStepHttpFailMin') || '400';
        } else if (type === 'sql_query') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.connectionStringName = selectedText('wfAiStepSqlConnection') || 'DefaultConnection';
            step.query = selectedText('wfAiStepSqlQuery') || 'SELECT 1';
            step.paramsJson = selectedText('wfAiStepSqlParams') || '';
            step.maxRows = selectedText('wfAiStepSqlMaxRows') || '100';
        } else if (type === 'file_read') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.path = selectedText('wfAiStepFileReadPath') || '';
            step.output = selectedText('wfAiStepFileReadOutput') || 'archivo';
            step.asJson = selectedText('wfAiStepFileReadAsJson') === 'true';
            step.encoding = selectedText('wfAiStepFileReadEncoding') || 'utf-8';
            step.zipMode = selectedText('wfAiStepFileReadZipMode') || 'auto';
            step.zipEntry = selectedText('wfAiStepFileReadZipEntry') || '';
        } else if (type === 'file_write') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.path = selectedText('wfAiStepFileWritePath') || '';
            step.sourceMode = selectedText('wfAiStepFileWriteSourceMode') || 'manual';
            step.origen = selectedText('wfAiStepFileWriteOriginField') || selectedText('wfAiStepFileWriteOrigin') || '';
            step.content = selectedText('wfAiStepFileWriteContent') || '';
            step.overwrite = selectedText('wfAiStepFileWriteOverwrite') !== 'false';
            step.encoding = selectedText('wfAiStepFileWriteEncoding') || 'utf-8';
            step.zipMode = selectedText('wfAiStepFileWriteZipMode') || 'none';
            step.entryName = selectedText('wfAiStepFileWriteEntry') || '';
        } else if (type === 'queue_publish') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.broker = selectedText('wfAiStepQueueBroker') || '';
            step.queue = selectedText('wfAiStepQueueName') || '';
            step.payloadMode = selectedText('wfAiStepQueuePayloadMode') || 'simple';
            if (step.payloadMode === 'fields') {
                step.payloadFields = collectQueuePayloadFieldsFromForm();
                step.payloadText = JSON.stringify(queuePayloadObjectFromFields(step.payloadFields), null, 2);
            } else if (step.payloadMode === 'simple') {
                step.payloadFields = [];
                step.payloadText = selectedText('wfAiStepQueuePayloadSimple') || '';
            } else {
                step.payloadFields = [];
                step.payloadText = selectedText('wfAiStepQueuePayload') || '';
            }
            step.connectionStringName = selectedText('wfAiStepQueueConnection') || '';
        } else if (type === 'queue_consume') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.broker = selectedText('wfAiStepQueueBroker') || '';
            step.queue = selectedText('wfAiStepQueueName') || '';
            step.take = selectedText('wfAiStepQueueTake') || '';
            step.connectionStringName = selectedText('wfAiStepQueueConnection') || '';
            step.debug = selectedText('wfAiStepQueueDebug') === 'true';
        } else if (type === 'subflow') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.ref = selectedText('wfAiStepSubflowRef') || '';
            step.alias = selectedText('wfAiStepSubflowAlias') || '';
            step.inputJson = selectedText('wfAiStepSubflowInput') || '{}';
            step.maxDepth = selectedText('wfAiStepSubflowMaxDepth') || '10';
        } else if (type === 'state_set') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.mode = selectedText('wfAiStepStateMode') || 'simple';
            if (step.mode === 'json') {
                step.setJson = selectedText('wfAiStepSetJson') || '{}';
                step.key = '';
                step.value = '';
            } else {
                step.key = selectedText('wfAiStepKey') || '';
                step.value = selectedText('wfAiStepValue') || '';
                step.setJson = '';
            }
        } else if (type === 'state_remove') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.key = selectedText('wfAiStepKey') || '';
        } else if (type === 'delay') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.ms = selectedText('wfAiStepMs') || '1000';
        } else if (type === 'retry') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.reintentos = selectedText('wfAiStepRetryCount') || '3';
            step.backoffMs = selectedText('wfAiStepRetryBackoff') || '500';
            step.message = selectedText('wfAiStepRetryMessage') || 'Reintento agregado por Constructor IA';
        } else if (type === 'error_handler') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.message = selectedText('wfAiStepErrorMessage') || 'Error controlado por el workflow';
            step.capture = selectedText('wfAiStepErrorCapture') !== 'false';
            step.notify = selectedText('wfAiStepErrorNotify') === 'true';
            step.retry = selectedText('wfAiStepErrorRetry') === 'true';
        } else if (type === 'logger') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.level = selectedText('wfAiStepLogLevel') || 'Info';
            step.message = selectedText('wfAiStepMessage') || '';
        } else if (type === 'end') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
        }

        if (step.branch === 'if_cond_true' || step.branch === 'if_cond_false') {
            step.branchSourceId = findLastStepIdByType('condition');
        } else if (step.branch === 'if_task_ok' || step.branch === 'if_task_reject') {
            step.branchSourceId = findLastHumanTaskOwnerIdForResultBranch();
        } else {
            step.branchSourceId = null;
        }

        return step;
    }

    function renderSingleGuideStep(step, idx, extraClass) {
        var isEditing = idx === editingStepIndex;
        var html = '';
        html += '<li class="wf-ai-step-item' + (extraClass ? ' ' + extraClass : '') + (isEditing ? ' is-editing' : '') + '" data-guide-action="edit" data-step-index="' + idx + '">';
        html += '<div class="wf-ai-step-title">' + htmlEncode(createStepTitle(step)) + (isEditing ? ' <span class="wf-ai-step-editing">editando</span>' : '') + '</div>';
        html += '<div class="wf-ai-step-tools">';
        html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="up" data-step-index="' + idx + '">↑</button>';
        html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="down" data-step-index="' + idx + '">↓</button>';
        html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="edit" data-step-index="' + idx + '">Modificar</button>';
        html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="delete" data-step-index="' + idx + '">Quitar</button>';
        html += '</div>';
        html += '</li>';
        return html;
    }

    function renderGuideStepTree(step, idx, extraClass, branchMap, taskBranchMap, path) {
        path = path || {};
        var key = String(idx);
        if (path[key]) {
            return renderSingleGuideStep(step, idx, extraClass);
        }
        var nextPath = {};
        Object.keys(path).forEach(function (k) { nextPath[k] = path[k]; });
        nextPath[key] = true;

        var html = renderSingleGuideStep(step, idx, extraClass);
        if (step && step.type === 'condition') html += renderConditionBranches(idx, branchMap, taskBranchMap, nextPath);
        if (step && step.type === 'human_task') html += renderTaskResultBranches(idx, branchMap, taskBranchMap, nextPath);
        return html;
    }

    function renderConditionBranches(conditionIndex, branchMap, taskBranchMap, path) {
        var groups = branchMap[conditionIndex] || { si: [], no: [] };
        var owner = guideSteps[conditionIndex] || {};
        var ownerId = owner.id || '';
        var ownerTitle = createStepTitle(owner) || 'Condición';
        var yesActive = isActiveGuideTarget('if_cond_true', ownerId) ? ' is-active-target' : '';
        var noActive = isActiveGuideTarget('if_cond_false', ownerId) ? ' is-active-target' : '';
        var html = '<div class="wf-ai-if-branches">';

        html += '<div class="wf-ai-if-branch wf-ai-if-branch-yes' + yesActive + '" data-guide-target-branch="if_cond_true" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Rama SI de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-if-branch-title">SI cumple</div>';
        if (groups.si.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.si.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para esta rama.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-if-branch wf-ai-if-branch-no' + noActive + '" data-guide-target-branch="if_cond_false" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Rama NO de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-if-branch-title">NO cumple</div>';
        if (groups.no.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.no.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para esta rama.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-if-branch-help">Hacé clic sobre SI CUMPLE o NO CUMPLE para que el próximo paso se agregue en esa rama.</div>';
        html += '</div>';
        return html;
    }


    function renderTaskResultBranches(taskIndex, branchMap, taskBranchMap, path) {
        var groups = taskBranchMap[taskIndex] || { ok: [], reject: [] };
        var owner = guideSteps[taskIndex] || {};
        var ownerId = owner.id || '';
        var ownerTitle = createStepTitle(owner) || 'Tarea humana';
        var okActive = isActiveGuideTarget('if_task_ok', ownerId) ? ' is-active-target' : '';
        var rejectActive = isActiveGuideTarget('if_task_reject', ownerId) ? ' is-active-target' : '';
        var html = '<div class="wf-ai-task-branches">';

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-ok' + okActive + '" data-guide-target-branch="if_task_ok" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Resultado APTO de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-task-branch-title">APTO</div>';
        if (groups.ok.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.ok.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-reject' + rejectActive + '" data-guide-target-branch="if_task_reject" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Resultado NO APTO de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-task-branch-title">NO APTO</div>';
        if (groups.reject.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.reject.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch-help">Hacé clic en APTO o NO APTO para agregar el próximo paso en ese resultado humano.</div>';
        html += '</div>';
        return html;
    }

    function renderGuideSteps() {
        var box = $('wfAiGuideSteps');
        if (!box) return;

        if (!activeTargetIsMain()) {
            var exists = guideSteps.some(function (x) { return x && String(x.id || '') === String(guideActiveTarget.sourceId || ''); });
            if (!exists) guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };
        }

        if (!guideSteps.length) {
            box.innerHTML = '<div class="wf-ai-guide-empty">Todavía no agregaste pasos. Elegí una acción y presioná “Agregar paso”.</div>';
            renderAvailableFields();
            return;
        }

        var branchMap = {};
        var taskBranchMap = {};
        var skip = {};
        guideSteps.forEach(function (step, idx) {
            if (!step) return;

            if (step.branch === 'if_cond_true' || step.branch === 'if_cond_false') {
                var ownerIndex = findBranchOwnerIndex(step, idx, 'condition');
                if (ownerIndex >= 0) {
                    if (!branchMap[ownerIndex]) branchMap[ownerIndex] = { si: [], no: [] };
                    if (step.branch === 'if_cond_true') branchMap[ownerIndex].si.push({ step: step, idx: idx });
                    else branchMap[ownerIndex].no.push({ step: step, idx: idx });
                    skip[idx] = true;
                }
            }

            if (step.branch === 'if_task_ok' || step.branch === 'if_task_reject') {
                var taskOwnerIndex = findBranchOwnerIndex(step, idx, 'human_task');
                if (taskOwnerIndex >= 0) {
                    if (!taskBranchMap[taskOwnerIndex]) taskBranchMap[taskOwnerIndex] = { ok: [], reject: [] };
                    if (step.branch === 'if_task_ok') taskBranchMap[taskOwnerIndex].ok.push({ step: step, idx: idx });
                    else taskBranchMap[taskOwnerIndex].reject.push({ step: step, idx: idx });
                    skip[idx] = true;
                }
            }
        });

        var html = '<ol class="wf-ai-step-list">';
        guideSteps.forEach(function (step, idx) {
            if (skip[idx]) return;
            html += renderGuideStepTree(step, idx, '', branchMap, taskBranchMap, {});
        });
        html += '</ol>';
        html += renderFunctionalValidationPanel(buildFunctionalValidation());
        box.innerHTML = html;

        Array.prototype.forEach.call(box.querySelectorAll('[data-guide-action]'), function (btn) {
            btn.addEventListener('click', function (ev) {
                ev.preventDefault();
                ev.stopPropagation();
                var action = btn.getAttribute('data-guide-action');
                var idx = parseInt(btn.getAttribute('data-step-index'), 10);
                if (isNaN(idx) || idx < 0 || idx >= guideSteps.length) return;
                if (action === 'edit') {
                    beginEditGuideStep(idx);
                    return;
                }
                if (action === 'delete') {
                    guideSteps.splice(idx, 1);
                    if (editingStepIndex === idx) editingStepIndex = -1;
                    else if (editingStepIndex > idx) editingStepIndex--;
                } else if (action === 'up' && idx > 0) {
                    var a = guideSteps[idx - 1];
                    guideSteps[idx - 1] = guideSteps[idx];
                    guideSteps[idx] = a;
                    if (editingStepIndex === idx) editingStepIndex = idx - 1;
                    else if (editingStepIndex === idx - 1) editingStepIndex = idx;
                } else if (action === 'down' && idx < guideSteps.length - 1) {
                    var b = guideSteps[idx + 1];
                    guideSteps[idx + 1] = guideSteps[idx];
                    guideSteps[idx] = b;
                    if (editingStepIndex === idx) editingStepIndex = idx + 1;
                    else if (editingStepIndex === idx + 1) editingStepIndex = idx;
                }
                renderGuideSteps();
                updateGuideEditMode();
                updatePromptFromSteps(false);
            });
        });

        Array.prototype.forEach.call(box.querySelectorAll('[data-guide-target-branch]'), function (target) {
            target.addEventListener('click', function (ev) {
                ev.preventDefault();
                ev.stopPropagation();
                setGuideActiveTarget(
                    target.getAttribute('data-guide-target-branch'),
                    target.getAttribute('data-guide-target-source-id'),
                    target.getAttribute('data-guide-target-label')
                );
            });
        });

        renderAvailableFields();
        renderActiveTargetBox();
    }

    function saveGuideStep() {
        var formError = validateCurrentStepForm();
        if (formError) {
            setStatus(formError, 'warn');
            return;
        }

        if (editingStepIndex >= 0 && editingStepIndex < guideSteps.length) {
            var old = guideSteps[editingStepIndex];
            var edited = collectStepFromForm(old.id);
            guideSteps[editingStepIndex] = edited;
            editingStepIndex = -1;
            renderGuideSteps();
            updateGuideEditMode();
            updatePromptFromSteps(false);
            setStatus('Paso modificado. Revisá la frase generada.', 'ok');
            return;
        }

        var step = collectStepFromForm();
        applyActiveTargetToStep(step);
        guideSteps.push(step);
        renderGuideSteps();
        updateGuideEditMode();
        updatePromptFromSteps(false);
    }

    function clearGuideSteps() {
        guideSteps = [];
        editingStepIndex = -1;
        guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };
        renderGuideSteps();
        renderAvailableFields();
        renderActiveTargetBox();
        updateGuideEditMode();
        setStatus('Constructor limpio.', 'ok');
    }

    // ------------------------------------------------------------
    // fix22: modo amplio del constructor IA
    // ------------------------------------------------------------
    function setWideMode(on) {
        var panel = $('wfAiPanel');
        if (!panel) return;

        var backdrop = $('wfAiWideBackdrop');
        if (!backdrop) {
            backdrop = document.createElement('div');
            backdrop.id = 'wfAiWideBackdrop';
            backdrop.className = 'wf-ai-wide-backdrop';
            backdrop.style.display = 'none';
            document.body.appendChild(backdrop);
            backdrop.addEventListener('click', function () { setWideMode(false); });
        }

        if (on) {
            panel.classList.add('wf-ai-wide');
            document.body.classList.add('wf-ai-wide-active');
            backdrop.style.display = '';
        } else {
            panel.classList.remove('wf-ai-wide');
            document.body.classList.remove('wf-ai-wide-active');
            backdrop.style.display = 'none';
        }

        var openBtn = $('wfAiWideOpen');
        var closeBtn = $('wfAiWideClose');
        if (openBtn) openBtn.style.display = on ? 'none' : '';
        if (closeBtn) closeBtn.style.display = on ? '' : 'none';
    }

    function ensureWideModeUi() {
        var panel = $('wfAiPanel');
        if (!panel || $('wfAiWideBar')) return;

        var title = panel.querySelector('.wf-ai-title');
        var bar = document.createElement('div');
        bar.id = 'wfAiWideBar';
        bar.className = 'wf-ai-wide-bar';
        bar.innerHTML =
            '<button type="button" class="btn wf-ai-guide-toggle" id="wfAiWideOpen">Abrir modo amplio</button>' +
            '<button type="button" class="btn wf-ai-guide-toggle" id="wfAiWideClose" style="display:none">Cerrar modo amplio</button>' +
            '<span class="wf-ai-guide-caption">Para diseñar el proceso con más espacio, sin quedar encerrado en el inspector.</span>';

        if (title && title.parentNode) title.parentNode.insertBefore(bar, title.nextSibling);
        else panel.insertBefore(bar, panel.firstChild);

        var open = $('wfAiWideOpen');
        if (open) open.addEventListener('click', function () { setWideMode(true); });

        var close = $('wfAiWideClose');
        if (close) close.addEventListener('click', function () { setWideMode(false); });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && panel.classList.contains('wf-ai-wide')) setWideMode(false);
        });
    }

    function ensureGuideUi() {
        var prompt = $('wfAiPrompt');
        if (!prompt || $('wfAiGuide')) return;

        var guide = document.createElement('div');
        guide.id = 'wfAiGuide';
        guide.className = 'wf-ai-guide';
        guide.innerHTML =
            '<div class="wf-ai-guide-head">' +
            '  <button type="button" class="btn wf-ai-guide-toggle" id="wfAiGuideToggle">Crear flujo paso a paso</button>' +
            '  <span class="wf-ai-guide-caption">Constructor incremental con nodos ya probados por la IA.</span>' +
            '</div>' +
            '<div class="wf-ai-guide-body" id="wfAiGuideBody" style="display:none">' +
            '  <div class="wf-ai-guide-layout">' +
            '    <div class="wf-ai-guide-left">' +
            '      <div class="wf-ai-guide-note">Elegí qué querés agregar. Hacé clic en una rama SI/NO o APTO/NO APTO para fijar dónde va el próximo paso; el formulario de la derecha se adapta a esa ubicación.</div>' +
            '      <div id="wfAiGuideSteps" class="wf-ai-guide-steps"></div>' +
            '    </div>' +
            '    <div class="wf-ai-guide-center">' +
            '      <div id="wfAiAvailableFields" class="wf-ai-available-fields"></div>' +
            '    </div>' +
            '    <div class="wf-ai-guide-right">' +
            '      <div id="wfAiActiveTarget" class="wf-ai-active-target"></div>' +
            '      <div class="wf-ai-guide-row">' +
            '        <label>Agregar paso</label>' +
            '        <select id="wfAiStepType" class="wf-ai-select">' +
            '          <option value="doc_load">Cargar documento</option>' +
            '          <option value="condition">Validar condición</option>' +
            '          <option value="human_task">Mandar tarea humana</option>' +
            '          <option value="http_request">Solicitud HTTP</option>' +
            '          <option value="sql_query">Consulta SQL</option>' +
            '          <option value="subflow">Ejecutar otro workflow</option>' +
            '          <option value="file_read">Archivo: Leer</option>' +
            '          <option value="file_write">Archivo: Escribir</option>' +
            '          <option value="queue_publish">Cola: Publicar</option>' +
            '          <option value="queue_consume">Cola: Consumir</option>' +
            '          <option value="email_send">Enviar correo</option>' +
            '          <option value="notify">Notificación interna</option>' +
            '          <option value="state_set">Guardar variable</option>' +
            '          <option value="state_remove">Quitar variable</option>' +
            '          <option value="delay">Esperar / demora</option>' +
            '          <option value="retry">Reintentar</option>' +
            '          <option value="error_handler">Manejador de Error</option>' +
            '          <option value="logger">Registrar log</option>' +
            '          <option value="end">Finalizar flujo</option>' +
            '        </select>' +
            '      </div>' +
            '      <div id="wfAiStepFields" class="wf-ai-step-fields"></div>' +
            '      <div class="wf-ai-guide-actions">' +
            '        <button type="button" class="btn" id="wfAiGuideAdd">Agregar paso</button>' +
            '        <button type="button" class="btn" id="wfAiGuideCancelEdit" style="display:none">Cancelar modificación</button>' +
            '        <button type="button" class="btn" id="wfAiGuideBuild">Armar frase</button>' +
            '        <button type="button" class="btn" id="wfAiGuideBuildRun">Armar e interpretar</button>' +
            '        <button type="button" class="btn" id="wfAiGuideClear">Limpiar pasos</button>' +
            '      </div>' +
            '      <div class="wf-ai-guide-note">Paso a paso conserva la estructura elegida y la normaliza por la misma capa contractual que Frase. La frase inferior queda como descripción humana, no como fuente para reconstruir las ramas.</div>' +
            '    </div>' +
            '  </div>' +
            '</div>';

        prompt.parentNode.insertBefore(guide, prompt);

        var toggle = $('wfAiGuideToggle');
        if (toggle) toggle.addEventListener('click', function () {
            var body = $('wfAiGuideBody');
            if (!body) return;
            var open = body.style.display === 'none';
            body.style.display = open ? '' : 'none';
            toggle.textContent = open ? 'Ocultar constructor' : 'Crear flujo paso a paso';
        });

        var type = $('wfAiStepType');
        if (type) type.addEventListener('change', renderStepFields);

        var add = $('wfAiGuideAdd');
        if (add) add.addEventListener('click', saveGuideStep);

        var cancelEdit = $('wfAiGuideCancelEdit');
        if (cancelEdit) cancelEdit.addEventListener('click', cancelEditGuideStep);

        var build = $('wfAiGuideBuild');
        if (build) build.addEventListener('click', function () { updatePromptFromSteps(false); });

        var buildRun = $('wfAiGuideBuildRun');
        if (buildRun) buildRun.addEventListener('click', function () { updatePromptFromSteps(true); });

        var clear = $('wfAiGuideClear');
        if (clear) clear.addEventListener('click', clearGuideSteps);

        renderGuideSteps();
        renderStepFields();
        renderActiveTargetBox();
        updateGuideEditMode();
        loadGuideCatalogs();
    }

    // ------------------------------------------------------------
    // fix31: UX Inspector / Asistente IA colapsado
    // ------------------------------------------------------------
    var aiUxState = {
        contextKey: '',
        userOpenedForContext: false
    };

    function getCanvasNodeCount() {
        try { return document.querySelectorAll('#canvasWorld .node').length; }
        catch (e) { return 0; }
    }

    function getInspectorContext() {
        var title = $('inspectorTitle');
        var sub = $('inspectorSub');
        var titleText = title ? (title.textContent || '').trim() : '';
        var subText = sub ? (sub.textContent || '').trim() : '';
        var nodeCount = getCanvasNodeCount();

        var hasSelection = !!titleText &&
            titleText.indexOf('Seleccion') !== 0 &&
            titleText !== 'Nodo no encontrado' &&
            titleText !== 'Arista no encontrada';

        if (hasSelection) {
            return {
                mode: 'selection',
                key: 'selection|' + titleText + '|' + subText,
                message: 'Inspector activo para ' + titleText + '. El Asistente IA queda colapsado para dejar espacio a los parámetros del nodo.'
            };
        }

        if (nodeCount > 0) {
            return {
                mode: 'graph',
                key: 'graph|' + nodeCount,
                message: 'Hay un grafo cargado. El Asistente IA queda colapsado para priorizar el inspector; podés abrirlo para seguir agregando pasos.'
            };
        }

        return {
            mode: 'empty',
            key: 'empty|0',
            message: ''
        };
    }

    function ensureCollapsedLauncher() {
        var panel = $('wfAiPanel');
        if (!panel || !panel.parentNode) return null;

        var existing = $('wfAiCollapsed');
        if (existing) return existing;

        var box = document.createElement('div');
        box.id = 'wfAiCollapsed';
        box.className = 'wf-ai-collapsed';
        box.style.display = 'none';
        box.innerHTML =
            '<div class="wf-ai-collapsed-head">' +
            '  <div>' +
            '    <div class="wf-ai-kicker">Asistente IA</div>' +
            '    <div id="wfAiCollapsedMsg" class="wf-ai-collapsed-msg"></div>' +
            '  </div>' +
            '  <div class="wf-ai-collapsed-actions">' +
            '    <button type="button" class="btn wf-ai-decisions-toggle" id="wfAiShowDecisions" style="display:none" aria-expanded="false">Ver decisiones IA</button>' +
            '    <button type="button" class="btn" id="wfAiShow">Mostrar Asistente IA</button>' +
            '  </div>' +
            '</div>' +
            '<div id="wfAiAppliedSummary" class="wf-ai-applied-summary" style="display:none"></div>';

        panel.parentNode.insertBefore(box, panel);

        var btn = $('wfAiShow');
        if (btn) {
            btn.addEventListener('click', function () {
                panel.style.display = '';
                box.style.display = 'none';
                box.setAttribute('data-reason', '');
                aiUxState.userOpenedForContext = true;
                var prompt = $('wfAiPrompt');
                if (prompt) prompt.focus();
            });
        }

        var decisionsBtn = $('wfAiShowDecisions');
        if (decisionsBtn) {
            decisionsBtn.addEventListener('click', function () {
                var summary = $('wfAiAppliedSummary');
                if (!summary || !aiAppliedDecisionSummary) return;
                var open = summary.style.display === 'none';
                summary.style.display = open ? '' : 'none';
                decisionsBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
                decisionsBtn.textContent = open ? 'Ocultar decisiones' : 'Ver decisiones IA';
            });
        }

        updateAppliedDecisionSummaryUi();
        return box;
    }

    // ------------------------------------------------------------
    // fix73b: memoria visible de las decisiones usadas al crear el workflow
    // ------------------------------------------------------------
    function appliedDecisionEffectText(questionKey, plan) {
        var phrase = getCurrentAiPhrase() || '';
        if (questionKey === 'notify-mode') {
            var mode = guidedNotifyActionMode(phrase);
            return 'Aplicado como ' + (mode === 'human_task' ? 'tareas humanas' : (mode === 'logger' ? 'registros de log' : 'notificaciones internas')) + ' para los destinatarios informados.';
        }
        if (questionKey === 'branch-no' || questionKey === 'missing-branch-no-action') {
            var noMode = guidedNoBranchActionMode(phrase);
            return 'La rama NO quedó con ' + (noMode === 'human_task' ? 'una tarea humana para COMPRAS' : (noMode === 'logger' ? 'un log Warn' : (noMode === 'end' ? 'cierre directo' : 'una notificación a COMPRAS'))) + '.';
        }
        if (questionKey === 'it-scope') {
            var scope = guidedItScope(phrase);
            return scope === 'both' ? 'IT quedó en ambas ramas.' : (scope === 'true' ? 'IT quedó en la rama SI.' : 'IT quedó en la rama NO.');
        }
        if (questionKey === 'data-message') {
            return fix74bContractDataSummary(plan && plan.contract);
        }
        if (questionKey === 'warning-mode') {
            var warningMode = guidedWarningActionMode(phrase);
            var noMode2 = guidedNoBranchActionMode(phrase);
            if (fallbackResolvedItem('missing-branch-no-action') || fallbackResolvedItem('branch-no')) {
                if (warningMode !== noMode2) return 'La decisión específica de la rama NO tuvo prioridad sobre esta opción general.';
            }
            return 'La advertencia se aplicó como ' + (warningMode === 'human_task' ? 'tarea humana' : (warningMode === 'logger' ? 'log Warn' : 'notificación interna')) + '.';
        }
        return '';
    }

    function buildAppliedDecisionSummary(plan) {
        var decisions = [];
        activeFallbackResolvedItems().forEach(function (x) {
            if (!x) return;
            decisions.push({
                title: String(x.title || 'Decisión').trim(),
                value: String(x.label || x.clarification || 'Opción seleccionada').trim(),
                effect: appliedDecisionEffectText(x.key || '', plan)
            });
        });
        var replacements = supersededFallbackResolvedItems().map(function (x) {
            return {
                title: String(x.title || 'Decisión').trim(),
                value: String(x.label || x.clarification || 'Opción anterior').trim(),
                reason: String(x.supersededReason || 'Fue reemplazada por una decisión más específica.').trim()
            };
        });

        var nodeCount = 0;
        ((plan && plan.actions) || []).forEach(function (a) {
            if (a && a.action === 'ADD_NODE') nodeCount++;
        });

        return {
            phrase: stripFallbackClarifications(getCurrentAiPhrase() || '').trim(),
            decisions: decisions,
            replacements: replacements,
            nodeCount: nodeCount,
            contractFingerprint: plan && plan.contract && plan.contract.fingerprint || ''
        };
    }

    function appliedDecisionSummaryHtml(summary) {
        summary = summary || {};
        var decisions = summary.decisions || [];
        var html = '';
        html += '<div class="wf-ai-applied-summary-head">';
        html += '  <div><div class="wf-ai-applied-summary-kicker">Decisiones usadas para crear este workflow</div>';
        html += '  <div class="wf-ai-applied-summary-title">Así se explica la ubicación de los nodos y las ramas.</div></div>';
        html += '  <span class="wf-ai-applied-summary-count">' + decisions.length + ' ' + (decisions.length === 1 ? 'decisión' : 'decisiones') + '</span>';
        html += '</div>';

        if (decisions.length) {
            html += '<ul class="wf-ai-applied-summary-list">';
            decisions.forEach(function (x) {
                html += '<li><strong>' + htmlEncode(x.title || 'Decisión') + '</strong><span>' + htmlEncode(x.value || 'Opción seleccionada') + '</span>';
                if (x.effect) html += '<em>Efecto: ' + htmlEncode(x.effect) + '</em>';
                html += '</li>';
            });
            html += '</ul>';
        } else {
            html += '<div class="wf-ai-applied-summary-empty">La frase se entendió sin pedir aclaraciones adicionales.</div>';
        }

        var replacements = summary.replacements || [];
        if (replacements.length) {
            html += '<details class="wf-ai-applied-summary-replacements"><summary>' + replacements.length + ' elección' + (replacements.length === 1 ? '' : 'es') + ' reemplazada' + (replacements.length === 1 ? '' : 's') + '</summary><ul>';
            replacements.forEach(function (x) {
                html += '<li><strong>' + htmlEncode(x.title || 'Decisión') + ':</strong> ' + htmlEncode(x.value || 'Opción anterior') + '<span>' + htmlEncode(x.reason || 'Fue reemplazada por una decisión más específica.') + '</span></li>';
            });
            html += '</ul></details>';
        }

        if (summary.nodeCount) {
            html += '<div class="wf-ai-applied-summary-result">Resultado aplicado: ' + summary.nodeCount + ' nodo' + (summary.nodeCount === 1 ? '' : 's') + ' creado' + (summary.nodeCount === 1 ? '' : 's') + ' en el canvas.</div>';
        }
        if (summary.contractFingerprint) {
            html += '<div class="wf-ai-applied-contract">Contrato fix74b validado · ' + htmlEncode(summary.contractFingerprint) + '</div>';
        }
        if (summary.phrase) {
            html += '<details class="wf-ai-applied-summary-phrase"><summary>Ver frase original</summary><div>' + htmlEncode(summary.phrase) + '</div></details>';
        }
        return html;
    }

    function updateAppliedDecisionSummaryUi() {
        var btn = $('wfAiShowDecisions');
        var summary = $('wfAiAppliedSummary');
        if (!btn || !summary) return;

        if (!aiAppliedDecisionSummary) {
            btn.style.display = 'none';
            btn.setAttribute('aria-expanded', 'false');
            btn.textContent = 'Ver decisiones IA';
            summary.style.display = 'none';
            summary.innerHTML = '';
            return;
        }

        btn.style.display = '';
        btn.setAttribute('aria-expanded', 'false');
        btn.textContent = 'Ver decisiones IA';
        summary.style.display = 'none';
        summary.innerHTML = appliedDecisionSummaryHtml(aiAppliedDecisionSummary);
    }

    function clearAppliedDecisionSummary() {
        aiAppliedDecisionSummary = null;
        updateAppliedDecisionSummaryUi();
    }

    function collapseAssistant(message, reason) {
        var panel = $('wfAiPanel');
        if (!panel) return;

        try { setWideMode(false); } catch (e) { }

        var collapsed = ensureCollapsedLauncher();
        var msg = $('wfAiCollapsedMsg');
        if (msg) msg.textContent = message || 'Asistente IA colapsado.';

        panel.style.display = 'none';
        if (collapsed) {
            collapsed.style.display = '';
            collapsed.setAttribute('data-reason', reason || 'manual');
        }
        updateAppliedDecisionSummaryUi();
    }

    function showAssistant() {
        var panel = $('wfAiPanel');
        var collapsed = $('wfAiCollapsed');
        if (!panel) return;
        panel.style.display = '';
        if (collapsed) {
            collapsed.style.display = 'none';
            collapsed.setAttribute('data-reason', '');
        }
    }


    // fix69b: cuando el Banco de regresión abre WorkflowUI.aspx?aiPhrase=...
    // copiamos la frase en el textarea del Constructor IA. No interpreta ni toca canvas.
    function getQueryValue(name) {
        try {
            if (window.URLSearchParams) {
                var sp = new URLSearchParams(window.location.search || '');
                return sp.get(name) || '';
            }
        } catch (e) { }

        try {
            var re = new RegExp('[?&]' + name.replace(/[\[\]]/g, '\\$&') + '=([^&]*)');
            var m = re.exec(window.location.search || '');
            return m ? decodeURIComponent(String(m[1] || '').replace(/\+/g, ' ')) : '';
        } catch (e2) { }

        return '';
    }

    function importRegressionPhraseIntoConstructor() {
        if (importedRegressionPhrase) return;

        var prompt = $('wfAiPrompt');
        if (!prompt) return;

        var phrase = getQueryValue('aiPhrase');
        if (!phrase) {
            try { phrase = window.sessionStorage.getItem('WF_AI_REGRESSION_PHRASE') || ''; } catch (e) { phrase = ''; }
        }

        phrase = String(phrase || '').trim();
        if (!phrase) return;

        importedRegressionPhrase = true;
        try { window.sessionStorage.removeItem('WF_AI_REGRESSION_PHRASE'); } catch (e2) { }

        phrase = cleanGuidePhraseText(phrase);
        prompt.value = phrase;

        var result = $('wfAiResult');
        if (result) result.innerHTML = '';

        lastPlan = null;
        aiUxState.userOpenedForContext = true;
        showAssistant();
        setStatus('Frase importada desde el Banco de regresión IA. Presioná Interpretar para generar la propuesta.', 'ok');

        try {
            prompt.focus();
            var len = prompt.value.length;
            if (prompt.setSelectionRange) prompt.setSelectionRange(len, len);
        } catch (e3) { }
    }

    function syncAssistantWithInspector() {
        var panel = $('wfAiPanel');
        if (!panel) return;

        var ctx = getInspectorContext();
        if (ctx.key !== aiUxState.contextKey) {
            aiUxState.contextKey = ctx.key;
            aiUxState.userOpenedForContext = false;
        }

        var collapsed = ensureCollapsedLauncher();
        var isPanelHidden = panel.style.display === 'none';
        var reason = collapsed ? (collapsed.getAttribute('data-reason') || '') : '';

        if (ctx.mode === 'empty') {
            if (isPanelHidden && (reason === 'selection' || reason === 'graph')) {
                showAssistant();
            }
            return;
        }

        if (!isPanelHidden && !aiUxState.userOpenedForContext && !panel.classList.contains('wf-ai-wide')) {
            collapseAssistant(ctx.message, ctx.mode === 'selection' ? 'selection' : 'graph');
        } else if (isPanelHidden && collapsed && !reason) {
            collapsed.style.display = '';
            collapsed.setAttribute('data-reason', ctx.mode === 'selection' ? 'selection' : 'graph');
        }
    }

    function startAssistantInspectorWatcher() {
        var inspector = $('inspector');
        if (!inspector || !window.MutationObserver) {
            setTimeout(syncAssistantWithInspector, 0);
            return;
        }

        var pending = false;
        var observer = new MutationObserver(function () {
            if (pending) return;
            pending = true;
            setTimeout(function () {
                pending = false;
                syncAssistantWithInspector();
            }, 0);
        });

        observer.observe(inspector, {
            childList: true,
            subtree: true,
            characterData: true,
            attributes: true
        });

        setTimeout(syncAssistantWithInspector, 0);
    }

    // ------------------------------------------------------------
    // Resultado del Asistente IA y aplicación al canvas
    // ------------------------------------------------------------
    function hideAssistantAfterApply(message) {
        clearVisualDraft();
        var panel = $('wfAiPanel');
        if (!panel) return;

        var result = $('wfAiResult');
        if (result) result.innerHTML = '';
        setStatus('', '');
        lastPlan = null;
        aiUxState.userOpenedForContext = false;

        collapseAssistant(message || 'Propuesta aplicada al canvas. Revisá el grafo antes de guardar.', 'applied');
    }

    function renderList(title, items, css) {
        if (!items || !items.length) return '';
        var h = '<div class="wf-ai-block ' + (css || '') + '"><div class="wf-ai-block-title">' + htmlEncode(title) + '</div><ul>';
        items.forEach(function (x) {
            if (typeof x === 'string') {
                h += '<li>' + htmlEncode(x) + '</li>';
            } else {
                h += '<li>' + htmlEncode(x.question || x.key || JSON.stringify(x)) + '</li>';
            }
        });
        h += '</ul></div>';
        return h;
    }

    // ------------------------------------------------------------
    // fix70: verificación visual de frase sin aplicar al canvas
    // ------------------------------------------------------------
    function uniqueArray(items) {
        var seen = {};
        var out = [];
        (items || []).forEach(function (item) {
            var key = String(item || '').trim();
            if (!key || seen[key]) return;
            seen[key] = true;
            out.push(key);
        });
        return out;
    }

    function planNodeTypes(plan) {
        var types = [];
        ((plan && plan.actions) || []).forEach(function (a) {
            if (a && a.action === 'ADD_NODE' && a.nodeType) types.push(a.nodeType);
        });
        return uniqueArray(types);
    }

    function isHumanTaskResultAction(action) {
        if (!action || String(action.nodeType || '') !== 'control.if') return false;
        var p = action.params || {};
        return String(p.field || '').trim().toLowerCase() === 'wf.tarea.resultado';
    }

    function humanReadableAiActionLabel(action) {
        if (!action) return '';
        var label = String(action.label || visualDraftHumanType(action.nodeType) || '').trim();
        var p = action.params || {};

        // FIX84C2Bg2: el IF sobre wf.tarea.resultado es implementación interna.
        // En la lectura funcional mostramos la tarea como una decisión APTO/NO APTO,
        // no como un segundo paso de negocio llamado "Resultado ... aprobado".
        if (isHumanTaskResultAction(action)) return 'Resultado APTO / NO APTO';

        // El nombre contractual histórico se conserva para no romper regresiones ni JSON,
        // pero al usuario le mostramos la acción que pidió: registrar una advertencia.
        if (String(action.nodeType || '') === 'util.logger'
            && String(p.level || '').toLowerCase() === 'warn'
            && normalizePhraseForSearch(label).indexOf('registrar rechazo de ') === 0) {
            return 'Registrar advertencia';
        }

        return label;
    }

    function phraseVerificationText(plan) {
        var items = [];
        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || a.action !== 'ADD_NODE') return;
            if (isHumanTaskResultAction(a)) return;
            var line = '';
            if (a.nodeType) line += a.nodeType;
            var label = humanReadableAiActionLabel(a);
            if (label) line += (line ? ' · ' : '') + label;
            if (line) items.push(line);
        });
        return items;
    }

    // ------------------------------------------------------------
    // fix70b: separar advertencias funcionales de advertencias técnicas
    // ------------------------------------------------------------
    function isTechnicalAiWarning(text) {
        var x = String(text || '').toLowerCase();
        if (!x) return false;

        // Son mensajes útiles para desarrollo/soporte, pero no para el usuario funcional.
        // Se mantienen completos dentro del JSON técnico.
        if (x.indexOf('proveedor mlnet') >= 0) return true;
        if (x.indexOf('phrase engine') >= 0) return true;
        if (x.indexOf('provider legacy') >= 0) return true;
        if (x.indexOf('modelo entrenado externo') >= 0) return true;
        if (x.indexOf('interpretación local') >= 0 || x.indexOf('interpretacion local') >= 0) return true;
        return false;
    }

    function splitFunctionalWarnings(items) {
        var out = { functional: [], technical: [] };
        (items || []).forEach(function (item) {
            var text = (typeof item === 'string') ? item : (item && (item.question || item.key || item.message)) || JSON.stringify(item);
            if (isTechnicalAiWarning(text)) out.technical.push(item);
            else out.functional.push(item);
        });
        return out;
    }

    function collectPhraseWarnings(res, validation, plan) {
        var warnings = [];
        if (validation && validation.warnings) warnings = warnings.concat(validation.warnings);
        if (res && res.catalogWarnings) warnings = warnings.concat(res.catalogWarnings);
        if (plan && plan.warnings) warnings = warnings.concat(plan.warnings);
        return splitFunctionalWarnings(warnings);
    }


    // ------------------------------------------------------------
    // fix71: fallback amigable de desambiguación
    // ------------------------------------------------------------
    function normalizePhraseForSearch(text) {
        return String(text || '')
            .toLowerCase()
            .normalize('NFD').replace(/[\u0300-\u036f]/g, '');
    }

    function stripFallbackClarifications(text) {
        return String(text || '')
            .replace(/\s*Aclaración:\s*/ig, '\nAclaración: ')
            .split(/\nAclaración:\s*/i)[0]
            .trim();
    }

    function fallbackBaseKeyFor(text) {
        return normalizePhraseForSearch(stripFallbackClarifications(text));
    }

    function fallbackRewriteKey(text) {
        return normalizePhraseForSearch(stripFallbackClarifications(text || ''))
            .replace(/[^a-z0-9_]+/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function sameFallbackPhrase(a, b) {
        return fallbackRewriteKey(a) === fallbackRewriteKey(b);
    }

    function syncFallbackSession(userText) {
        var key = fallbackBaseKeyFor(userText || '');
        if (key !== aiFallbackBaseKey) {
            aiFallbackBaseKey = key;
            aiFallbackResolved = {};
            aiFallbackResolvedItems = [];
            aiFallbackAcceptedRewriteKey = '';
            aiVisualDraftSelectedIssueKey = '';
        }
    }

    function fallbackResolvedItemAny(questionKey) {
        questionKey = String(questionKey || '').trim();
        if (!questionKey) return null;
        for (var i = aiFallbackResolvedItems.length - 1; i >= 0; i--) {
            var item = aiFallbackResolvedItems[i];
            if (item && String(item.key || '') === questionKey) return item;
        }
        return null;
    }

    function fallbackItemText(item) {
        return normalizePhraseForSearch(((item && item.label) || '') + ' ' + ((item && item.clarification) || ''));
    }

    function markFallbackItemSuperseded(item, byItem, reason) {
        if (!item || !byItem || item === byItem) return;
        item.superseded = true;
        item.supersededByKey = String(byItem.key || '');
        item.supersededByLabel = String(byItem.label || byItem.title || 'una decisión más específica');
        item.supersededReason = String(reason || 'Una decisión más específica reemplazó esta opción.');
    }

    function recalculateFallbackDecisionConflicts() {
        aiFallbackResolvedItems.forEach(function (item) {
            if (!item) return;
            item.superseded = false;
            item.supersededByKey = '';
            item.supersededByLabel = '';
            item.supersededReason = '';
        });

        var specificNo = fallbackResolvedItemAny('missing-branch-no-action');
        var generalNo = fallbackResolvedItemAny('branch-no');
        var warning = fallbackResolvedItemAny('warning-mode');

        // La salida concreta de la rama NO es más específica que la pregunta genérica.
        if (specificNo && generalNo) {
            markFallbackItemSuperseded(
                generalNo,
                specificNo,
                'La salida específica de la rama NO reemplazó la elección general anterior.'
            );
        }

        // La forma de “advertencia” solo es necesaria cuando la rama eligió registrar una advertencia.
        if (warning) {
            if (specificNo) {
                markFallbackItemSuperseded(
                    warning,
                    specificNo,
                    'La salida específica de la rama NO definió directamente la acción para COMPRAS.'
                );
            } else if (generalNo) {
                var branchText = fallbackItemText(generalNo);
                if (branchText.indexOf('registrar una advertencia') < 0) {
                    markFallbackItemSuperseded(
                        warning,
                        generalNo,
                        'La rama NO quedó definida sin usar una advertencia configurable.'
                    );
                }
            }
        }
    }

    function activeFallbackResolvedItems() {
        recalculateFallbackDecisionConflicts();
        return aiFallbackResolvedItems.filter(function (item) { return item && !item.superseded; });
    }

    function supersededFallbackResolvedItems() {
        recalculateFallbackDecisionConflicts();
        return aiFallbackResolvedItems.filter(function (item) { return item && item.superseded; });
    }

    function markFallbackResolved(questionKey, title, label, clarification) {
        questionKey = String(questionKey || '').trim();
        if (!questionKey) return;
        aiFallbackResolved[questionKey] = true;
        var item = {
            key: questionKey,
            title: String(title || '').trim(),
            label: String(label || '').trim(),
            clarification: String(clarification || '').trim(),
            superseded: false,
            supersededByKey: '',
            supersededByLabel: '',
            supersededReason: ''
        };
        var replaced = false;
        for (var i = 0; i < aiFallbackResolvedItems.length; i++) {
            if (aiFallbackResolvedItems[i].key === questionKey) {
                aiFallbackResolvedItems[i] = item;
                replaced = true;
                break;
            }
        }
        if (!replaced) aiFallbackResolvedItems.push(item);
        recalculateFallbackDecisionConflicts();
    }

    function fallbackQuestionAlreadyAnswered(q, userText) {
        if (!q || !q.key) return false;
        if (aiFallbackResolved[q.key]) return true;
        var x = normalizePhraseForSearch(userText || '');
        if (q.key === 'notify-mode' && textHasAny(x, ['crear notificaciones internas util.notify', 'crear tareas humanas', 'registrar logs del workflow'])) return true;
        if (q.key === 'warning-mode' && textHasAny(x, ['advertencia debe ser una notificacion', 'advertencia debe ser un logger warn', 'advertencia debe crear una tarea'])) return true;
        if (q.key === 'branch-no' && textHasAny(x, ['si la condicion no se cumple', 'si la condición no se cumple'])) return true;
        if (q.key === 'data-message' && textHasAny(x, ['mensaje de la notificacion incluir', 'mensajes incluir', 'contenido de la accion incluir', 'contenido de las acciones incluir'])) return true;
        if (q.key === 'it-scope' && textHasAny(x, ['notificar a it en ambas ramas', 'notificar a it solo cuando'])) return true;
        if (q.key.indexOf('missing-') === 0 && textHasAny(x, ['completar el dato faltante'])) return true;
        return false;
    }

    function removeFallbackQuestionImmediately(btn) {
        var el = btn;
        while (el && el !== document && !(el.className && String(el.className).indexOf('wf-ai-fallback-question') >= 0)) {
            el = el.parentNode;
        }
        if (el && el.parentNode) {
            el.className += ' resolved';
            if (el.parentNode.removeChild) el.parentNode.removeChild(el);
        }
    }

    function renderFallbackResolvedSummary() {
        if (!aiFallbackResolvedItems.length) return '';
        var active = activeFallbackResolvedItems();
        var superseded = supersededFallbackResolvedItems();
        var html = '';
        html += '<div class="wf-ai-fallback-resolved">';
        html += '  <div class="wf-ai-fallback-label">Decisiones vigentes</div>';
        if (active.length) {
            html += '  <ul>';
            active.forEach(function (x) {
                var label = x.label || x.clarification || 'Opción seleccionada';
                html += '<li><strong>' + htmlEncode(x.title || 'Decisión') + ':</strong> ' + htmlEncode(label) + '</li>';
            });
            html += '  </ul>';
        }
        if (superseded.length) {
            html += '<div class="wf-ai-fallback-replaced">';
            html += '  <div class="wf-ai-fallback-replaced-title">Elecciones reemplazadas automáticamente</div>';
            html += '  <ul>';
            superseded.forEach(function (x) {
                html += '<li><strong>' + htmlEncode(x.title || 'Decisión') + ':</strong> ' + htmlEncode(x.label || x.clarification || 'Opción anterior');
                html += '<span>' + htmlEncode(x.supersededReason || 'Fue reemplazada por una decisión más específica.') + '</span></li>';
            });
            html += '  </ul>';
            html += '</div>';
        }
        html += '</div>';
        return html;
    }

    // ------------------------------------------------------------
    // fix73c: una decisión registrada debe cambiar el tipo real de nodo
    // ------------------------------------------------------------
    function fallbackResolvedItem(questionKey) {
        recalculateFallbackDecisionConflicts();
        var item = fallbackResolvedItemAny(questionKey);
        return item && !item.superseded ? item : null;
    }

    function fallbackDecisionText(questionKey) {
        var item = fallbackResolvedItem(questionKey);
        if (!item) return '';
        return normalizePhraseForSearch((item.label || '') + ' ' + (item.clarification || ''));
    }

    function fallbackDecisionMode(questionKey, defaultMode) {
        var txt = fallbackDecisionText(questionKey);
        if (!txt) return defaultMode || '';
        if (txt.indexOf('tarea humana') >= 0 || txt.indexOf('crear tarea') >= 0) return 'human_task';
        if (txt.indexOf('logger') >= 0 || txt.indexOf('log warn') >= 0 || txt.indexOf('registrar log') >= 0) return 'logger';
        if (txt.indexOf('terminar flujo') >= 0 || txt.indexOf('finalizar el flujo sin') >= 0) return 'end';
        if (txt.indexOf('notificacion') >= 0 || txt.indexOf('notificar') >= 0 || txt.indexOf('aviso visible') >= 0) return 'notify';
        return defaultMode || '';
    }

    function guidedNotifyActionMode(userText) {
        var mode = fallbackDecisionMode('notify-mode', '');
        if (mode) return mode;
        var x = normalizePhraseForSearch(userText || '');
        if (x.indexOf('crear tareas humanas') >= 0) return 'human_task';
        if (x.indexOf('registrar logs del workflow') >= 0) return 'logger';
        return 'notify';
    }

    function guidedWarningActionMode(userText) {
        var mode = fallbackDecisionMode('warning-mode', '');
        if (mode) return mode;
        var x = normalizePhraseForSearch(userText || '');
        if (x.indexOf('advertencia debe crear una tarea') >= 0) return 'human_task';
        if (x.indexOf('advertencia debe ser un logger warn') >= 0) return 'logger';
        return 'notify';
    }

    function guidedNoBranchActionMode(userText) {
        // La respuesta específica de cierre de rama tiene prioridad sobre la genérica.
        var specific = fallbackResolvedItem('missing-branch-no-action');
        if (specific) {
            var specificText = normalizePhraseForSearch((specific.label || '') + ' ' + (specific.clarification || ''));
            var specificMode = fallbackDecisionMode('missing-branch-no-action', 'notify');
            if (specificMode === 'notify' && specificText.indexOf('notificar') >= 0) return guidedNotifyActionMode(userText);
            return specificMode;
        }

        var branch = fallbackResolvedItem('branch-no');
        if (branch) {
            var branchText = normalizePhraseForSearch((branch.label || '') + ' ' + (branch.clarification || ''));
            if (branchText.indexOf('registrar una advertencia') >= 0) return guidedWarningActionMode(userText);
            var branchMode = fallbackDecisionMode('branch-no', 'notify');
            if (branchMode === 'notify' && branchText.indexOf('notificar') >= 0) return guidedNotifyActionMode(userText);
            return branchMode;
        }

        return guidedWarningActionMode(userText);
    }

    function guidedMessageData(userText) {
        var item = fallbackResolvedItem('data-message');
        var txt = item
            ? normalizePhraseForSearch((item.label || '') + ' ' + (item.clarification || ''))
            : normalizePhraseForSearch(userText || '');
        return {
            cae: txt.indexOf('cae') >= 0,
            proveedor: txt.indexOf('proveedor') >= 0
        };
    }

    function guidedActionNodeType(mode) {
        if (mode === 'human_task') return 'human.task';
        if (mode === 'logger') return 'util.logger';
        if (mode === 'end') return '';
        return 'util.notify';
    }

    function guidedActionVerb(mode, role) {
        role = role || 'destino';
        if (mode === 'human_task') return 'crear una tarea humana para ' + role;
        if (mode === 'logger') return 'registrar un log para ' + role;
        if (mode === 'end') return 'finalizar sin agregar otra acción';
        return 'notificar a ' + role;
    }

    function textHasAny(text, words) {
        var x = normalizePhraseForSearch(text);
        for (var i = 0; i < (words || []).length; i++) {
            if (x.indexOf(normalizePhraseForSearch(words[i])) >= 0) return true;
        }
        return false;
    }

    function planHasNodeType(plan, nodeType) {
        nodeType = String(nodeType || '').trim();
        var found = false;
        ((plan && plan.actions) || []).forEach(function (a) {
            if (a && a.action === 'ADD_NODE' && String(a.nodeType || '') === nodeType) found = true;
        });
        return found;
    }

    // FIX84C2Bg2b: "registrar una advertencia" ya expresa una acción de registro.
    // No debe abrir una aclaración artificial notificación/log/tarea si el plan común ya
    // la resolvió como util.logger Warn. La duda sigue disponible para usos realmente ambiguos
    // de "advertencia" donde no existe ese verbo de registro.
    function explicitWarningLoggerRequest(userText) {
        var x = normalizePhraseForSearch(userText || '');
        if (!x) return false;
        if (textHasAny(x, [
            'registrar una advertencia',
            'registrar advertencia',
            'registrar un warning',
            'registrar warning',
            'registrar un log warn',
            'registrar log warn',
            'registrar un log de advertencia',
            'registrar log de advertencia',
            'logger warn',
            'log warn'
        ])) return true;
        return textHasAny(x, ['dejar constancia']) && textHasAny(x, ['advertencia', 'warning']);
    }

    function planHasWarnLogger(plan) {
        var found = false;
        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || a.action !== 'ADD_NODE' || String(a.nodeType || '') !== 'util.logger') return;
            var p = a.params || {};
            if (String(p.level || '').trim().toLowerCase() === 'warn') found = true;
        });
        return found;
    }

    function fallbackWarningOutcomeScope(plan) {
        var actions = ((plan && plan.actions) || []).filter(function (a) {
            return a && a.action === 'ADD_NODE' && a.nodeType;
        });
        var conns = (plan && plan.proposedConnections) || [];
        var byLabel = {};
        var warnLoggers = [];

        actions.forEach(function (a) {
            byLabel[normalizePhraseForSearch(a.label || '')] = a;
            if (String(a.nodeType || '') === 'util.logger'
                && String((a.params || {}).level || '').trim().toLowerCase() === 'warn') warnLoggers.push(a);
        });

        // Si hay varias advertencias no adivinamos cuál está aclarando el usuario.
        if (warnLoggers.length !== 1) return null;
        var warningKey = normalizePhraseForSearch(warnLoggers[0].label || '');
        var branchEdge = null;
        for (var i = 0; i < conns.length; i++) {
            var c = conns[i];
            if (!c || normalizePhraseForSearch(c.to || '') !== warningKey) continue;
            var fromAction = byLabel[normalizePhraseForSearch(c.from || '')];
            if (fromAction && isHumanTaskResultAction(fromAction)) {
                branchEdge = c;
                break;
            }
        }
        if (!branchEdge) return null;

        var resultKey = normalizePhraseForSearch(branchEdge.from || '');
        var taskEdge = null;
        for (var j = 0; j < conns.length; j++) {
            var tc = conns[j];
            if (!tc || normalizePhraseForSearch(tc.to || '') !== resultKey) continue;
            var candidate = byLabel[normalizePhraseForSearch(tc.from || '')];
            if (candidate && String(candidate.nodeType || '') === 'human.task') {
                taskEdge = tc;
                break;
            }
        }
        if (!taskEdge) return null;

        var task = byLabel[normalizePhraseForSearch(taskEdge.from || '')];
        var taskParams = (task && task.params) || {};
        var taskRole = String(taskParams.rol || taskParams.RolDestino || taskParams.role || '').trim();
        var cond = fix74NormalizeCondition(branchEdge.condition);
        return {
            outcome: cond === 'true' ? 'APTO' : (cond === 'false' ? 'NO APTO' : ''),
            taskRole: taskRole,
            taskLabel: String((task && task.label) || '').trim()
        };
    }

    function scopedWarningClarification(mode, scope, roles) {
        if (!scope || !scope.outcome) return '';
        var owner = scope.taskRole || scope.taskLabel || 'la tarea humana';
        var detected = detectedRolesInText(roles || '');
        var destination = '';
        for (var i = 0; i < detected.length; i++) {
            if (!scope.taskRole || normalizeKey(detected[i]) !== normalizeKey(scope.taskRole)) {
                destination = detected[i];
                break;
            }
        }
        if (!destination && scope.taskRole) destination = scope.taskRole;

        var prefix = 'en el resultado ' + scope.outcome + ' de la tarea de ' + owner + ', ';
        if (mode === 'logger') return prefix + 'registrar un logger Warn del workflow y conservar esa acción dentro de esa misma rama.';
        if (mode === 'human_task') return prefix + 'crear una tarea humana de revisión y conservarla dentro de esa misma rama.';
        if (destination) return prefix + 'notificar internamente a ' + destination + ' con nivel advertencia y conservar esa acción dentro de esa misma rama.';
        return prefix + 'usar una notificación interna de nivel advertencia y conservar esa acción dentro de esa misma rama.';
    }

    function branchLooksIncomplete(plan) {
        var branches = (plan && plan.branchPlan && plan.branchPlan.branches) || [];
        for (var i = 0; i < branches.length; i++) {
            var b = branches[i] || {};
            var t = normalizePhraseForSearch((b.truePath || '') + ' ' + (b.falsePath || ''));
            if (t.indexOf('pendiente') >= 0 || t.indexOf('definir') >= 0) return true;
        }
        return false;
    }

    function validationMentionsBranches(validation) {
        var txt = '';
        ((validation && validation.errors) || []).forEach(function (e) { txt += ' ' + String(e || ''); });
        return textHasAny(txt, ['rama', 'ramas', 'salida si', 'salida no', 'true', 'false']);
    }

    function detectedRolesInText(userText) {
        var roles = [];
        var x = normalizePhraseForSearch(userText);
        (guideCatalog.roles || []).forEach(function (r) {
            var key = roleKey(r);
            var name = String(r && (r.Nombre || r.nombre || '') || '').trim();
            if (!key) return;
            if (x.indexOf(normalizePhraseForSearch(key)) >= 0 || (name && x.indexOf(normalizePhraseForSearch(name)) >= 0)) roles.push(key);
        });
        ['DIR_GENERAL', 'COMPRAS', 'ADM_FIN', 'OPERACIONES', 'IT'].forEach(function (r) {
            if (x.indexOf(normalizePhraseForSearch(r)) >= 0) roles.push(r);
        });
        return uniqueArray(roles);
    }

    function friendlyRoleList(userText) {
        var roles = detectedRolesInText(userText);
        return roles.length ? roles.join(', ') : 'los roles mencionados';
    }

    function isStructuredStepByStepResult(res) {
        if (!res) return false;
        if (res.structuredSource === true) return true;
        return normalizePhraseForSearch(res.sourceKind || '') === 'step_by_step';
    }

    function fallbackNeedsHelp(res, userText) {
        var plan = (res && res.plan) || {};
        var validation = (res && res.validation) || {};
        var missing = plan.missingData || [];
        if (!res || !res.ok) return true;
        if (missing.length) return true;
        if ((validation.errors || []).length) return true;
        if (branchLooksIncomplete(plan)) return true;

        // FIX84C2Bg2d: Paso a paso ya declaró tipo y parámetros de cada nodo.
        // La frase inferior es sólo una descripción humana del plan y NO puede volver a
        // convertir valores como Message=Informar, Título=Notificar..., etc. en intenciones.
        if (isStructuredStepByStepResult(res)) return false;

        if (textHasAny(userText, ['notificar', 'notificacion', 'notificación', 'avisar', 'informar']) && !planHasNodeType(plan, 'util.notify')) return true;
        if (textHasAny(userText, ['advertencia', 'warning']) && !planHasNodeType(plan, 'util.logger') && !planHasNodeType(plan, 'util.notify')) return true;
        return false;
    }

    function guidedItScope(userText) {
        if (!textHasAny(userText, ['it'])) return 'none';
        var decision = fallbackDecisionText('it-scope');
        if (decision) {
            if (decision.indexOf('ambas ramas') >= 0 || decision.indexOf('it siempre') >= 0) return 'both';
            if (decision.indexOf('solo si tiene cae') >= 0 || decision.indexOf('solo cuando la nc tenga cae') >= 0) return 'true';
            if (decision.indexOf('solo si falta cae') >= 0 || decision.indexOf('solo cuando la nc no tenga cae') >= 0) return 'false';
        }
        if (textHasAny(userText, ['notificar a it en ambas ramas', 'it siempre'])) return 'both';
        if (textHasAny(userText, ['notificar a it solo cuando la nc tenga cae', 'it solo si tiene cae'])) return 'true';
        return 'false';
    }

    function buildSuggestedRewrite(userText) {
        var hasNc = textHasAny(userText, ['nc', 'nota de credito', 'nota de crédito']);
        var hasCae = textHasAny(userText, ['cae']);
        var hasProveedor = textHasAny(userText, ['proveedor']);
        var hasIt = textHasAny(userText, ['it']);
        var itScope = guidedItScope(userText);
        var hasCompras = textHasAny(userText, ['compras']);
        var hasDir = textHasAny(userText, ['dir_general', 'direccion', 'dirección', 'dir general']);
        var hasNotify = textHasAny(userText, ['notificar', 'notificacion', 'notificación', 'avisar', 'informar']);

        if (hasNc && hasCae && hasNotify && (hasDir || hasCompras || hasIt)) {
            var txt = 'Cargar una NC. Si tiene CAE, notificar a DIR_GENERAL con el número de CAE.';
            txt += ' Si no tiene CAE, notificar a COMPRAS con nivel advertencia indicando que falta CAE.';
            if (hasProveedor || hasIt) {
                if (itScope === 'both') txt += ' Notificar a IT en ambas ramas con el proveedor obtenido de la NC.';
                else if (itScope === 'true') txt += ' En la rama SI, notificar a IT con el proveedor obtenido de la NC.';
                else txt += ' En esa misma rama NO, notificar a IT con el proveedor obtenido de la NC.';
            }
            txt += ' Finalizar el flujo.';
            return txt;
        }
        return '';
    }

    function canBuildGuidedResolution(userText) {
        // fix71d: patrón seguro para no dejar al usuario en un callejón sin salida.
        // No intenta cubrir todo el español libre. Solo habilita conversión guiada cuando
        // la frase ya contiene piezas suficientes para un flujo documental con CAE + notificaciones.
        return textHasAny(userText, ['nc', 'nota de credito', 'nota de crédito'])
            && textHasAny(userText, ['cae'])
            && textHasAny(userText, ['notificar', 'notificacion', 'notificación', 'avisar', 'informar'])
            && (textHasAny(userText, ['dir_general', 'direccion', 'dirección', 'dir general']) || textHasAny(userText, ['compras']) || textHasAny(userText, ['it']));
    }

    function fallbackHasOnlyActionableResolution(res, userText) {
        if (!fallbackNeedsHelp(res, userText)) return false;
        var questions = buildFallbackQuestions(res, userText);
        return questions.length === 0;
    }

    function shouldLetFallbackOwnErrors(res, userText) {
        return fallbackHasOnlyActionableResolution(res, userText);
    }

    function assistantPhaseStripHtml(activeStep, completedThrough) {
        var labels = ['Verificar frase', 'Resolver dudas', 'Revisar plan', 'Confirmar plan', 'Aplicar al canvas'];
        activeStep = parseInt(activeStep || '1', 10);
        completedThrough = parseInt(completedThrough || '0', 10);
        var html = '<div class="wf-ai-phase-strip" aria-label="Etapas del Constructor IA">';
        labels.forEach(function (label, idx) {
            var step = idx + 1;
            var css = step <= completedThrough ? ' done' : (step === activeStep ? ' active' : '');
            html += '<div class="wf-ai-phase' + css + '"><span>' + step + '</span><strong>' + htmlEncode(label) + '</strong></div>';
        });
        html += '</div>';
        return html;
    }

    function fallbackResolutionText(res, userText) {
        if (canBuildGuidedResolution(userText)) {
            return 'El intérprete libre no cerró bien todas las ramas. Te muestro los puntos que voy a completar y genero una propuesta estructurada para que la confirmes antes de aplicar.';
        }
        return 'No es un error del usuario: la frase tiene demasiadas decisiones para el intérprete libre. El camino seguro es armarla paso a paso con decisiones visibles.';
    }

    function guidedResolutionFixItems(userText) {
        var items = [];
        var hasIt = textHasAny(userText, ['it']);
        var itScope = guidedItScope(userText);
        var hasCompras = textHasAny(userText, ['compras']);
        var hasDir = textHasAny(userText, ['dir_general', 'direccion', 'dirección', 'dir general']);
        var data = guidedMessageData(userText);
        var notifyMode = guidedNotifyActionMode(userText);
        var noMode = guidedNoBranchActionMode(userText);

        function branchDataText(hasCae) {
            var included = [];
            if (data.cae && hasCae) included.push('CAE');
            if (data.proveedor) included.push('proveedor');
            var text = included.length ? ' incluyendo ' + included.join(' y ') : '';
            if (data.cae && !hasCae) {
                text += (text ? '; ' : ' ') + 'el CAE no se incluye porque esta rama representa CAE faltante';
            }
            return text + '.';
        }

        items.push('Validar CAE con una condición SI / NO completa.');
        if (hasDir) items.push('Rama SI: ' + guidedActionVerb(notifyMode, 'DIR_GENERAL') + branchDataText(true));
        if (hasCompras) items.push('Rama NO: ' + guidedActionVerb(noMode, 'COMPRAS') + (noMode === 'end' ? '.' : branchDataText(false)));
        if (hasIt) {
            if (itScope === 'both') {
                items.push('Rama SI: ' + guidedActionVerb(notifyMode, 'IT') + branchDataText(true));
                items.push('Rama NO: ' + guidedActionVerb(notifyMode, 'IT') + branchDataText(false));
            } else if (itScope === 'true') {
                items.push('Rama SI: ' + guidedActionVerb(notifyMode, 'IT') + branchDataText(true));
            } else {
                items.push('Rama NO: ' + guidedActionVerb(notifyMode, 'IT') + branchDataText(false));
            }
        }
        items.push('Cerrar todas las ramas con Fin para que no quede ninguna salida pendiente.');
        return items;
    }

    function renderGuidedResolutionAction(res, userText) {
        var html = '';
        var items = guidedResolutionFixItems(userText);
        html += '  <div class="wf-ai-fallback-pending actionable fixable">';
        html += '    <div class="wf-ai-fallback-label">Resolver acá</div>';
        html += '    <div class="wf-ai-fallback-fix-title">Ya detecté dónde quedó trabado el intérprete libre</div>';
        html += '    <div class="wf-ai-fallback-q-detail">No te dejo mirando el error. Voy a convertir esas decisiones en pasos explícitos y después te muestro el plan para confirmarlo.</div>';
        html += '    <ul class="wf-ai-fallback-fix-list">';
        items.forEach(function (x) { html += '<li>' + htmlEncode(x) + '</li>'; });
        html += '    </ul>';
        html += '    <div class="wf-ai-fallback-next-actions">';
        html += '      <button type="button" class="btn wf-ai-fallback-btn primary" data-wf-ai-guided-resolution="1">Resolver y revisar plan</button>';
        html += '      <button type="button" class="btn wf-ai-fallback-btn" data-wf-ai-open-guide="1">Editarlo paso a paso</button>';
        html += '    </div>';
        html += '  </div>';
        return html;
    }

    function buildGuidedResolutionStepsFromPhrase(userText) {
        if (!canBuildGuidedResolution(userText)) return false;

        var hasIt = textHasAny(userText, ['it']);
        var hasDir = textHasAny(userText, ['dir_general', 'direccion', 'dirección', 'dir general']);
        var hasCompras = textHasAny(userText, ['compras']);
        var itScope = guidedItScope(userText);
        var itInBothBranches = hasIt && itScope === 'both';
        var itOnlyWhenHasCae = hasIt && itScope === 'true';
        var itOnlyWhenMissingCae = hasIt && itScope === 'false';
        var notifyMode = guidedNotifyActionMode(userText);
        var noBranchMode = guidedNoBranchActionMode(userText);
        var messageData = guidedMessageData(userText);
        var docId = guideSeq++;
        var condId = guideSeq++;
        var steps = [];

        function selectedDataText(hasCae) {
            var parts = [];
            if (messageData.cae && hasCae) parts.push('CAE: {{biz.notaCredito.cae}}');
            if (messageData.proveedor) parts.push('Proveedor: {{biz.notaCredito.proveedor}}');
            return parts.length ? ' ' + parts.join('. ') + '.' : '';
        }

        function roleMessage(role, hasCae) {
            var base;
            if (role === 'DIR_GENERAL') base = hasCae ? 'La nota de crédito tiene CAE informado.' : 'La nota de crédito no tiene CAE informado.';
            else if (role === 'COMPRAS') base = hasCae ? 'Revisar la nota de crédito con CAE informado.' : 'La nota de crédito no tiene CAE informado. Revisar la documentación.';
            else base = hasCae ? 'Revisar los datos de la nota de crédito con CAE.' : 'Revisar los datos de la nota de crédito sin CAE.';
            return base + selectedDataText(hasCae);
        }

        function addRoleAction(mode, branch, role, title, message, level) {
            mode = mode || 'notify';
            if (mode === 'end') return;

            var common = {
                id: guideSeq++,
                branch: branch,
                branchSourceId: condId
            };

            if (mode === 'human_task') {
                common.type = 'human_task';
                common.destType = 'rol';
                common.role = role;
                common.user = '';
                common.purpose = message || ('Revisión para ' + role);
                steps.push(common);
                return;
            }

            if (mode === 'logger') {
                common.type = 'logger';
                common.level = level || 'Warn';
                common.message = role + ': ' + (message || 'Aviso generado por el Constructor IA');
                steps.push(common);
                return;
            }

            common.type = 'notify';
            common.destType = 'rol';
            common.role = role;
            common.user = '';
            common.title = title || ('Aviso para ' + role);
            common.level = level || 'info';
            common.message = message || 'Hay una novedad pendiente en el workflow.';
            steps.push(common);
        }

        steps.push({
            id: docId,
            type: 'doc_load',
            docTipo: firstDoc(['NOTA_CREDITO_ELECTRONICA_AR', 'NC_AR']) || 'NOTA_CREDITO_ELECTRONICA_AR',
            path: 'input.filePath'
        });

        steps.push({
            id: condId,
            type: 'condition',
            condition: 'cae',
            fieldPath: 'biz.notaCredito.cae',
            fieldLabel: 'CAE',
            fieldType: 'texto',
            operator: 'not_empty',
            value: ''
        });

        if (hasDir) {
            addRoleAction(
                notifyMode,
                'if_cond_true',
                'DIR_GENERAL',
                notifyMode === 'human_task' ? 'Aprobación Dirección' : 'NC con CAE informado',
                roleMessage('DIR_GENERAL', true),
                'info'
            );
        }

        if (hasCompras) {
            addRoleAction(
                noBranchMode,
                'if_cond_false',
                'COMPRAS',
                noBranchMode === 'human_task' ? 'Revisión de Compras' : 'NC sin CAE',
                roleMessage('COMPRAS', false),
                noBranchMode === 'logger' || noBranchMode === 'notify' ? 'Warn' : 'info'
            );
        }

        if (itInBothBranches || itOnlyWhenHasCae) {
            addRoleAction(
                notifyMode,
                'if_cond_true',
                'IT',
                'Proveedor de NC con CAE',
                roleMessage('IT', true),
                'info'
            );
        }
        if (itInBothBranches || itOnlyWhenMissingCae) {
            addRoleAction(
                notifyMode,
                'if_cond_false',
                'IT',
                'Proveedor de NC sin CAE',
                roleMessage('IT', false),
                'info'
            );
        }

        steps.push({ id: guideSeq++, type: 'end' });

        guideSteps = steps;
        editingStepIndex = -1;
        guideActiveTarget = { branch: 'then', sourceId: null, label: 'Flujo principal' };
        renderGuideSteps();
        renderAvailableFields();
        renderActiveTargetBox();
        updateGuideEditMode();
        updatePromptFromSteps(false);
        return true;
    }

    function resolveWithGuidedPlan() {
        var txt = $('wfAiPrompt');
        var userText = txt ? (txt.value || '') : '';
        var base = buildSuggestedRewrite(userText) || stripFallbackClarifications(userText);
        if (txt && base) txt.value = cleanGuidePhraseText(base).trim();

        if (buildGuidedResolutionStepsFromPhrase(userText || base || '')) {
            setWideMode(false);
            resetGuidedPlanConfirmation();
            var currentText = getCurrentAiPhrase();
            var structuredResult = buildStructuredGuideResult(currentText);
            setStatus('Resolviendo el plan con la capa común FIX84C2A...', 'busy');
            normalizeStructuredGuideResult(structuredResult, currentText, function (resolved) {
                if (resolved) {
                    resolved.guidedResolution = true;
                    resolved.requiresFinalConfirmation = true;
                }
                renderResult(resolved);
                setStatus('Resolución generada. Revisá el plan y confirmalo antes de aplicar al canvas.', 'ok');
            }, function (err) {
                renderResult({ ok: false, error: err.message || String(err) });
                setStatus('No se pudo resolver el plan por la capa común FIX84C2A.', 'error');
            });
            return;
        }

        openStepByStepGuide();
    }

    function addFallbackOption(question, label, clarification, css) {
        question.options.push({ label: label, clarification: clarification, css: css || '' });
    }

    function buildFallbackQuestions(res, userText) {
        var plan = (res && res.plan) || {};
        var validation = (res && res.validation) || {};
        var missing = plan.missingData || [];
        var questions = [];
        var structuredStepByStep = isStructuredStepByStepResult(res);
        // FIX84C2Bg2d: en Paso a paso los textos de propiedades son datos, no comandos.
        // Las preguntas léxicas quedan reservadas al modo Frase.
        var hasNotifyWords = !structuredStepByStep && textHasAny(userText, ['notificar', 'notificacion', 'notificación', 'avisar', 'informar']);
        var hasWarningWord = !structuredStepByStep && textHasAny(userText, ['advertencia', 'warning']);
        var hasCae = textHasAny(userText, ['cae']);
        var hasProveedor = textHasAny(userText, ['proveedor']);
        var hasIt = textHasAny(userText, ['it']);
        var roles = friendlyRoleList(userText);
        var missingText = normalizePhraseForSearch(missing.map(function (m) {
            return String((m && (m.question || m.key || m.message)) || m || '');
        }).join(' '));
        var needsSpecificNoBranch = missing.length && (
            missingText.indexOf('no se cumple') >= 0 ||
            missingText.indexOf('rama') >= 0 ||
            missingText.indexOf('condicion compuesta') >= 0
        );
        var generalBranchDecision = fallbackResolvedItem('branch-no');
        var generalBranchText = fallbackItemText(generalBranchDecision);
        var generalBranchUsesWarning = !!generalBranchDecision && generalBranchText.indexOf('registrar una advertencia') >= 0;
        var needsGeneralBranch = !needsSpecificNoBranch && (branchLooksIncomplete(plan) || validationMentionsBranches(validation));
        var needsGeneralBranchQuestion = needsGeneralBranch && !generalBranchDecision;

        if (hasNotifyWords && !planHasNodeType(plan, 'util.notify')) {
            var qNotify = {
                key: 'notify-mode',
                title: 'Cuando decís notificar / avisar / informar',
                detail: 'Para evitar adivinar, necesito confirmar si esto es una notificación interna, una tarea humana o un registro.',
                options: []
            };
            addFallbackOption(qNotify, 'Usar notificación interna', 'cuando diga notificar, avisar o informar a ' + roles + ', crear notificaciones internas util.notify, no tareas humanas.', 'primary');
            addFallbackOption(qNotify, 'Crear tarea humana', 'cuando diga enviar a ' + roles + ', crear tareas humanas para esos roles.', '');
            addFallbackOption(qNotify, 'Solo registrar log', 'cuando diga informar o advertir, registrar logs del workflow y no crear tarea humana.', '');
            questions.push(qNotify);
        }

        // fix73d: si el validador pide una salida concreta, no mostramos además la pregunta genérica.
        if (needsSpecificNoBranch) {
            var qMissingBranch = {
                key: 'missing-branch-no-action',
                title: 'Falta cerrar la rama NO',
                detail: 'El plan sigue pidiendo qué hacer cuando la condición NO se cumple. Elegí una única salida concreta.',
                options: []
            };
            addFallbackOption(qMissingBranch, 'NO: notificar a COMPRAS y finalizar', 'si la condición no se cumple, notificar a COMPRAS con nivel advertencia indicando que falta CAE y luego finalizar.', 'primary');
            addFallbackOption(qMissingBranch, 'NO: registrar log Warn y finalizar', 'si la condición no se cumple, registrar un logger Warn indicando que falta CAE y luego finalizar; no crear tarea humana en esa rama.', '');
            addFallbackOption(qMissingBranch, 'NO: crear tarea para COMPRAS', 'si la condición no se cumple, crear una tarea humana para COMPRAS y luego finalizar cuando se complete.', '');
            questions.push(qMissingBranch);
        } else if (needsGeneralBranchQuestion) {
            var qBranch = {
                key: 'branch-no',
                title: 'Falta completar una rama SI / NO',
                detail: 'Una condición nunca debería quedar con una rama pendiente. Decime qué hacer cuando no se cumple.',
                options: []
            };
            addFallbackOption(qBranch, 'NO: notificar a COMPRAS', 'si la condición no se cumple, notificar a COMPRAS y luego finalizar.', 'primary');
            addFallbackOption(qBranch, 'NO: registrar advertencia', 'si la condición no se cumple, registrar una advertencia Warn y luego finalizar.', '');
            addFallbackOption(qBranch, 'NO: terminar flujo', 'si la condición no se cumple, finalizar el flujo sin más acciones.', '');
            questions.push(qBranch);
        }

        // FIX84C2Bg2b: si el usuario ya dijo "registrar una advertencia" y el plan común
        // produjo un logger Warn, no existe ambigüedad real y no preguntamos nada.
        // Para los usos genuinamente ambiguos conservamos además el resultado APTO/NO APTO
        // dentro de la aclaración para que una elección no salte al flujo principal.
        var explicitWarningLoggerResolved = explicitWarningLoggerRequest(userText) && planHasWarnLogger(plan);
        var warningQuestionUseful = hasWarningWord
            && !explicitWarningLoggerResolved
            && !needsSpecificNoBranch
            && !needsGeneralBranchQuestion
            && (!generalBranchDecision || generalBranchUsesWarning);
        if (warningQuestionUseful) {
            var warningScope = fallbackWarningOutcomeScope(plan);
            var qWarn = {
                key: 'warning-mode',
                title: 'La palabra advertencia puede significar dos cosas',
                detail: 'Puede ser un aviso visible para un rol, un log técnico Warn o una tarea de revisión.',
                options: []
            };
            addFallbackOption(qWarn, 'Advertencia como notificación',
                scopedWarningClarification('notify', warningScope, userText) || 'la advertencia debe ser una notificación interna de nivel advertencia al rol indicado.', 'primary');
            addFallbackOption(qWarn, 'Advertencia como log Warn',
                scopedWarningClarification('logger', warningScope, userText) || 'la advertencia debe ser un logger Warn del workflow.', '');
            addFallbackOption(qWarn, 'Advertencia como tarea',
                scopedWarningClarification('human_task', warningScope, userText) || 'la advertencia debe crear una tarea humana de revisión.', '');
            questions.push(qWarn);
        }

        if (hasIt && hasCae) {
            var qScope = {
                key: 'it-scope',
                title: '¿La acción para IT va siempre o solo en una rama?',
                detail: 'Cuando una frase mezcla varias acciones, conviene ubicar cada una en la rama correcta.',
                options: []
            };
            addFallbackOption(qScope, 'IT siempre', 'notificar a IT en ambas ramas, tenga o no tenga CAE.', 'primary');
            addFallbackOption(qScope, 'IT solo si falta CAE', 'notificar a IT solo cuando la NC no tenga CAE.', '');
            addFallbackOption(qScope, 'IT solo si tiene CAE', 'notificar a IT solo cuando la NC tenga CAE.', '');
            questions.push(qScope);
        }

        if (hasCae || hasProveedor) {
            var qData = {
                key: 'data-message',
                title: 'Datos dentro del contenido',
                detail: 'Detecté datos del documento. Conviene aclarar cuáles deben incluirse en notificaciones, tareas o registros.',
                options: []
            };
            if (hasCae) addFallbackOption(qData, 'Incluir número de CAE', 'en el contenido de la acción incluir el número de CAE extraído de la NC.', '');
            if (hasProveedor) addFallbackOption(qData, 'Incluir proveedor', 'en el contenido de la acción incluir el proveedor obtenido de la NC.', '');
            if (hasCae && hasProveedor) addFallbackOption(qData, 'Incluir CAE y proveedor', 'en el contenido de las acciones incluir el número de CAE y el proveedor obtenido de la NC.', 'primary');
            if (qData.options.length) questions.push(qData);
        }

        if (missing.length && !needsSpecificNoBranch) {
            var qMissing = {
                key: 'missing-data',
                title: 'Dato faltante detectado',
                detail: 'El validador pidió completar información antes de aplicar al canvas.',
                options: []
            };
            missing.slice(0, 2).forEach(function (m) {
                var q = String((m && (m.question || m.key || m.message)) || m || '');
                addFallbackOption(qMissing, q, 'completar el dato faltante: ' + q, '');
            });
            questions.push(qMissing);
        }

        return questions.filter(function (q) { return !fallbackQuestionAlreadyAnswered(q, userText); }).slice(0, 5);
    }

    // ------------------------------------------------------------
    // fix73: propuesta visual no persistente sobre el canvas
    // ------------------------------------------------------------
    function visualDraftZoom() {
        var label = $('wfZoomLabel');
        var m = label && /([0-9]+)\s*%/.exec(label.textContent || '');
        var z = m ? (parseInt(m[1], 10) / 100) : 1;
        if (!isFinite(z) || z <= 0) z = 1;
        return Math.max(0.5, Math.min(1.8, z));
    }

    function clearVisualDraft() {
        var host = $('wfAiVisualDraft');
        if (host && host.parentNode) host.parentNode.removeChild(host);
        var banner = $('wfAiVisualDraftBanner');
        if (banner && banner.parentNode) banner.parentNode.removeChild(banner);
        var canvas = $('canvas');
        if (canvas) canvas.classList.remove('wf-ai-draft-mode');
        aiVisualDraftActive = false;
    }

    function ensureVisualDraftIssueSelection(questions) {
        questions = questions || [];
        aiVisualDraftLastQuestions = questions.slice(0);
        if (!questions.length) {
            aiVisualDraftSelectedIssueKey = '';
            return '';
        }
        var exists = questions.some(function (q) { return q && q.key === aiVisualDraftSelectedIssueKey; });
        if (!exists) aiVisualDraftSelectedIssueKey = String((questions[0] && questions[0].key) || '');
        return aiVisualDraftSelectedIssueKey;
    }

    function visualDraftQuestionByKey(questions, key) {
        questions = questions || [];
        for (var i = 0; i < questions.length; i++) {
            if (questions[i] && questions[i].key === key) return questions[i];
        }
        return questions[0] || null;
    }

    function visualDraftHumanType(type) {
        var map = {
            'util.start': 'Inicio',
            'util.end': 'Fin',
            'doc.load': 'Documento',
            'control.if': 'Condición',
            'util.notify': 'Notificación',
            'human.task': 'Tarea humana',
            'util.logger': 'Registro',
            'control.delay': 'Espera',
            'control.retry': 'Reintento',
            'queue.publish': 'Cola: Publicar',
            'queue.consume': 'Cola: Consumir'
        };
        return map[String(type || '')] || String(type || 'Paso');
    }

    function visualDraftActionNodes(plan) {
        var actions = ((plan && plan.actions) || []).filter(function (a) {
            return a && String(a.action || '').toUpperCase() === 'ADD_NODE' && a.nodeType;
        });
        var nodes = [];
        actions.forEach(function (a, idx) {
            // FIX84C2Bg2: en el borrador funcional, human.task es quien abre APTO/NO APTO.
            // El control.if sobre wf.tarea.resultado sigue existiendo en el plan técnico y
            // será aplicado al canvas, pero no se presenta como un paso de negocio adicional.
            if (isHumanTaskResultAction(a)) return;
            nodes.push({
                id: 'wf-ai-draft-action-' + idx,
                action: a,
                nodeType: String(a.nodeType || ''),
                label: humanReadableAiActionLabel(a),
                issueKeys: [],
                layer: idx,
                lane: 0
            });
        });
        return nodes;
    }

    function visualDraftFindNode(nodes, label, type) {
        var wantedLabel = normalizePhraseForSearch(label || '');
        var wantedType = String(type || '');
        var fallback = null;
        for (var i = 0; i < nodes.length; i++) {
            var n = nodes[i];
            if (!n || n.isIssue) continue;
            if (wantedType && n.nodeType !== wantedType) continue;
            if (!fallback) fallback = n;
            if (normalizePhraseForSearch(n.label) === wantedLabel) return n;
            if (normalizePhraseForSearch(n.action && n.action.label || '') === wantedLabel) return n;
        }
        if (!wantedType) {
            for (var j = 0; j < nodes.length; j++) {
                if (!nodes[j].isIssue && normalizePhraseForSearch(nodes[j].label) === wantedLabel) return nodes[j];
            }
        }
        return fallback;
    }

    function visualDraftRelevantNode(nodes, question) {
        var key = String((question && question.key) || '');
        var i;
        if (question && question.nodeType) {
            var contractual = visualDraftFindNode(nodes, question.nodeLabel || '', question.nodeType || '');
            if (contractual) return contractual;
        }
        if (key === 'branch-no' || key === 'missing-branch-no-action' || key === 'it-scope') {
            for (i = 0; i < nodes.length; i++) if (nodes[i].nodeType === 'control.if') return nodes[i];
        }
        if (key === 'data-message' || key === 'notify-mode') {
            for (i = 0; i < nodes.length; i++) if (nodes[i].nodeType === 'util.notify') return nodes[i];
        }
        if (key === 'warning-mode') {
            for (i = 0; i < nodes.length; i++) {
                if (nodes[i].nodeType === 'util.notify' || nodes[i].nodeType === 'util.logger') return nodes[i];
            }
        }
        for (i = nodes.length - 1; i >= 0; i--) {
            if (!nodes[i].isIssue && nodes[i].nodeType !== 'util.end') return nodes[i];
        }
        return nodes[0] || null;
    }

    function buildVisualDraftModel(plan, questions) {
        questions = questions || [];
        var nodes = visualDraftActionNodes(plan);
        var edges = [];
        var connections = (plan && plan.proposedConnections) || [];
        var hiddenHumanResultIf = {};
        var humanResultParent = {};

        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || !isHumanTaskResultAction(a)) return;
            hiddenHumanResultIf[normalizePhraseForSearch(a.label || '')] = a;
        });

        // El plan técnico conserva human.task -> control.if(wf.tarea.resultado). Para el
        // borrador funcional recordamos qué tarea es dueña de ese IF y luego lo colapsamos.
        connections.forEach(function (c) {
            if (!c) return;
            var toKey = normalizePhraseForSearch(c.to || '');
            if (hiddenHumanResultIf[toKey]) {
                humanResultParent[toKey] = { label: c.from || '', nodeType: c.fromNodeType || 'human.task' };
            }
        });

        connections.forEach(function (c) {
            if (!c) return;
            var fromLabel = c.from || '';
            var fromType = c.fromNodeType || '';
            var toLabel = c.to || '';
            var toType = c.toNodeType || '';
            var condition = String(c.condition || 'always');
            var displayCondition = condition;
            var fromKey = normalizePhraseForSearch(fromLabel);
            var toKey = normalizePhraseForSearch(toLabel);

            // La arista task -> IF técnico no se dibuja: el usuario ve directamente las
            // salidas APTO / NO APTO desde la propia tarea humana.
            if (hiddenHumanResultIf[toKey]) return;

            if (hiddenHumanResultIf[fromKey]) {
                var parent = humanResultParent[fromKey];
                if (!parent) return;
                fromLabel = parent.label;
                fromType = parent.nodeType;
                var branch = normalizePhraseForSearch(condition);
                if (branch === 'si' || branch === 'true') displayCondition = 'APTO';
                else if (branch === 'no' || branch === 'false') displayCondition = 'NO APTO';
            }

            var from = visualDraftFindNode(nodes, fromLabel, fromType);
            var to = visualDraftFindNode(nodes, toLabel, toType);
            if (!from || !to || from === to) return;
            edges.push({ from: from, to: to, condition: condition, displayCondition: displayCondition, issue: false });
        });

        if (!edges.length && nodes.length > 1) {
            for (var si = 0; si < nodes.length - 1; si++) {
                edges.push({ from: nodes[si], to: nodes[si + 1], condition: 'always', issue: false });
            }
        }

        var incoming = {};
        edges.forEach(function (e) { incoming[e.to.id] = (incoming[e.to.id] || 0) + 1; });
        nodes.forEach(function (n, idx) { n.layer = idx; n.lane = 0; });
        if (nodes.length) nodes[0].layer = 0;

        for (var pass = 0; pass < nodes.length + 4; pass++) {
            edges.forEach(function (e) {
                var nextLayer = (e.from.layer || 0) + 1;
                if ((e.to.layer || 0) < nextLayer) e.to.layer = nextLayer;
                var cond = normalizePhraseForSearch(e.condition || '');
                if (incoming[e.to.id] > 1 || e.to.nodeType === 'util.end') e.to.lane = 0;
                else if (cond === 'true' || cond === 'si') e.to.lane = (e.from.lane || 0) - 1;
                else if (cond === 'false' || cond === 'no') e.to.lane = (e.from.lane || 0) + 1;
                else e.to.lane = e.from.lane || 0;
            });
        }

        var issueColumn = 0;
        var occupied = {};
        nodes.forEach(function (n) { occupied[String(n.layer) + '|' + String(n.lane)] = true; });
        questions.forEach(function (q) {
            if (!q || !q.key) return;
            var target = visualDraftRelevantNode(nodes, q);
            var issueLayer = target ? (target.layer + 1) : (nodes.length ? nodes[nodes.length - 1].layer + 1 : 0);
            var issueLane = target ? target.lane + 1 + issueColumn : issueColumn;
            var condition = '?';

            if (q.key === 'branch-no' || q.key === 'missing-branch-no-action') {
                condition = 'NO';
                issueLane = target ? target.lane + 1 : issueColumn;
            } else if (q.key === 'it-scope') {
                condition = '¿SI / NO?';
                issueLane = target ? target.lane + 2 : issueColumn;
            }

            while (occupied[String(issueLayer) + '|' + String(issueLane)]) issueLane++;
            occupied[String(issueLayer) + '|' + String(issueLane)] = true;

            var issueNode = {
                id: 'wf-ai-draft-issue-' + q.key,
                nodeType: 'wf.ai.issue',
                label: q.title || 'Falta definir',
                detail: q.detail || 'Hacé clic para resolver esta decisión.',
                issueKeys: [q.key],
                isIssue: true,
                layer: issueLayer,
                lane: issueLane
            };

            issueColumn++;
            nodes.push(issueNode);
            if (target) edges.push({ from: target, to: issueNode, condition: condition, issue: true, issueKey: q.key });
        });

        if (!nodes.length && questions.length) {
            questions.forEach(function (q, idx) {
                nodes.push({
                    id: 'wf-ai-draft-issue-' + q.key,
                    nodeType: 'wf.ai.issue',
                    label: q.title || 'Falta definir',
                    detail: q.detail || '',
                    issueKeys: [q.key],
                    isIssue: true,
                    layer: idx,
                    lane: 0
                });
            });
        }

        var minLane = 0;
        nodes.forEach(function (n) { if (n.lane < minLane) minLane = n.lane; });
        var laneOffset = Math.abs(minLane);
        nodes.forEach(function (n) {
            n.x = 70 + (n.layer * 235);
            n.y = 105 + ((n.lane + laneOffset) * 165);
            n.w = n.isIssue ? 210 : 190;
            n.h = n.isIssue ? 92 : 78;
        });
        return { nodes: nodes, edges: edges };
    }

    function visualDraftEdgePath(from, to) {
        var x1 = from.x + from.w;
        var y1 = from.y + from.h / 2;
        var x2 = to.x;
        var y2 = to.y + to.h / 2;
        var bend = Math.max(55, Math.abs(x2 - x1) * 0.45);
        return 'M ' + x1 + ' ' + y1 + ' C ' + (x1 + bend) + ' ' + y1 + ', ' + (x2 - bend) + ' ' + y2 + ', ' + x2 + ' ' + y2;
    }

    function focusVisualDraftIssue(questionKey) {
        questionKey = String(questionKey || '');
        if (!questionKey) return;
        aiVisualDraftSelectedIssueKey = questionKey;
        if (lastAssistantResult) renderPhraseVerification(lastAssistantResult, getCurrentAiPhrase());
        setTimeout(function () {
            var card = document.querySelector('[data-wf-ai-question-key="' + questionKey.replace(/"/g, '') + '"]');
            if (card) {
                card.classList.add('wf-ai-fallback-question-focus');
                if (card.scrollIntoView) card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
            setStatus('Resolvé esta duda con las opciones del Inspector. El borrador se redibujará automáticamente.', 'warn');
        }, 0);
    }

    function renderVisualDraft(res, userText, options) {
        options = options || {};
        clearVisualDraft();
        var plan = (res && res.plan) || {};
        var questions = options.questions || [];
        var model = buildVisualDraftModel(plan, questions);
        if (!model.nodes.length) return;

        ensureVisualDraftIssueSelection(questions);
        var canvas = $('canvas');
        var world = $('canvasWorld');
        if (!canvas || !world) return;

        canvas.classList.add('wf-ai-draft-mode');
        var maxX = 900, maxY = 500;
        model.nodes.forEach(function (n) {
            maxX = Math.max(maxX, n.x + n.w + 100);
            maxY = Math.max(maxY, n.y + n.h + 100);
        });
        var fitX = Math.max(0.42, (canvas.clientWidth - 36) / maxX);
        var fitY = Math.max(0.42, (canvas.clientHeight - 54) / maxY);
        var zoom = Math.min(visualDraftZoom(), fitX, fitY);
        canvas.scrollLeft = 0;
        canvas.scrollTop = 0;

        var host = document.createElement('div');
        host.id = 'wfAiVisualDraft';
        host.className = 'wf-ai-visual-draft';
        host.style.width = maxX + 'px';
        host.style.height = maxY + 'px';
        host.style.transform = 'scale(' + zoom + ')';
        host.style.transformOrigin = '0 0';

        var svgNs = 'http://www.w3.org/2000/svg';
        var svg = document.createElementNS(svgNs, 'svg');
        svg.setAttribute('class', 'wf-ai-visual-draft-edges');
        svg.setAttribute('width', maxX);
        svg.setAttribute('height', maxY);
        svg.setAttribute('viewBox', '0 0 ' + maxX + ' ' + maxY);

        var defs = document.createElementNS(svgNs, 'defs');
        var marker = document.createElementNS(svgNs, 'marker');
        marker.setAttribute('id', 'wfAiDraftArrow');
        marker.setAttribute('markerWidth', '10');
        marker.setAttribute('markerHeight', '7');
        marker.setAttribute('refX', '9');
        marker.setAttribute('refY', '3.5');
        marker.setAttribute('orient', 'auto');
        var poly = document.createElementNS(svgNs, 'polygon');
        poly.setAttribute('points', '0 0, 10 3.5, 0 7');
        marker.appendChild(poly);
        defs.appendChild(marker);

        var markerIssue = document.createElementNS(svgNs, 'marker');
        markerIssue.setAttribute('id', 'wfAiDraftArrowIssue');
        markerIssue.setAttribute('markerWidth', '10');
        markerIssue.setAttribute('markerHeight', '7');
        markerIssue.setAttribute('refX', '9');
        markerIssue.setAttribute('refY', '3.5');
        markerIssue.setAttribute('orient', 'auto');
        var polyIssue = document.createElementNS(svgNs, 'polygon');
        polyIssue.setAttribute('points', '0 0, 10 3.5, 0 7');
        markerIssue.appendChild(polyIssue);
        defs.appendChild(markerIssue);
        svg.appendChild(defs);

        model.edges.forEach(function (e) {
            var path = document.createElementNS(svgNs, 'path');
            path.setAttribute('d', visualDraftEdgePath(e.from, e.to));
            path.setAttribute('class', 'wf-ai-draft-edge' + (e.issue ? ' issue' : ''));
            path.setAttribute('marker-end', e.issue ? 'url(#wfAiDraftArrowIssue)' : 'url(#wfAiDraftArrow)');
            svg.appendChild(path);
            if (e.condition && normalizePhraseForSearch(e.condition) !== 'always') {
                var text = document.createElementNS(svgNs, 'text');
                text.setAttribute('class', 'wf-ai-draft-edge-label' + (e.issue ? ' issue' : ''));
                text.setAttribute('x', ((e.from.x + e.from.w + e.to.x) / 2));
                text.setAttribute('y', ((e.from.y + e.from.h / 2 + e.to.y + e.to.h / 2) / 2) - 8);
                text.textContent = e.displayCondition || e.condition;
                svg.appendChild(text);
            }
        });
        host.appendChild(svg);

        model.nodes.forEach(function (n) {
            var issueKey = n.issueKeys && n.issueKeys.length ? n.issueKeys[0] : '';
            var el = document.createElement('button');
            el.type = 'button';
            el.className = 'wf-ai-draft-node' + (n.isIssue || issueKey ? ' issue' : '') + (issueKey && issueKey === aiVisualDraftSelectedIssueKey ? ' selected' : '');
            el.style.left = n.x + 'px';
            el.style.top = n.y + 'px';
            el.style.width = n.w + 'px';
            el.style.minHeight = n.h + 'px';
            if (issueKey) el.setAttribute('data-wf-ai-visual-issue', issueKey);

            var head = document.createElement('span');
            head.className = 'wf-ai-draft-node-head';
            var icon = document.createElement('span');
            icon.className = 'wf-ai-draft-node-icon';
            icon.textContent = issueKey ? '!' : '✓';
            var title = document.createElement('span');
            title.className = 'wf-ai-draft-node-title';
            title.textContent = n.label;
            head.appendChild(icon);
            head.appendChild(title);
            el.appendChild(head);

            var body = document.createElement('span');
            body.className = 'wf-ai-draft-node-body';
            body.textContent = issueKey ? (n.detail || 'Hacé clic para resolver') : visualDraftHumanType(n.nodeType);
            el.appendChild(body);

            if (issueKey) {
                var pending = document.createElement('span');
                pending.className = 'wf-ai-draft-node-pending';
                pending.textContent = 'Falta definir';
                el.appendChild(pending);
                el.addEventListener('click', function (ev) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    focusVisualDraftIssue(issueKey);
                });
            } else {
                el.tabIndex = -1;
            }
            host.appendChild(el);
        });

        world.appendChild(host);

        var banner = document.createElement('div');
        banner.id = 'wfAiVisualDraftBanner';
        banner.className = 'wf-ai-visual-draft-banner ' + (questions.length ? 'pending' : 'complete');
        banner.innerHTML = questions.length
            ? '<strong>Borrador IA</strong><span>' + questions.length + ' duda' + (questions.length === 1 ? '' : 's') + ' pendiente' + (questions.length === 1 ? '' : 's') + '. Hacé clic en rojo.</span>'
            : '<strong>Propuesta visual completa</strong><span>Todavía no está guardada ni puede ejecutarse.</span>';
        (canvas.parentNode || canvas).appendChild(banner);
        aiVisualDraftActive = true;
    }

    function renderFallbackHelp(res, userText) {
        syncFallbackSession(userText);
        if (!fallbackNeedsHelp(res, userText)) return '';
        var questions = buildFallbackQuestions(res, userText);
        var rewrite = buildSuggestedRewrite(userText);
        aiClarificationOptions = [];
        aiRewriteOptions = [];
        var selectedKey = ensureVisualDraftIssueSelection(questions);
        var selectedQuestion = visualDraftQuestionByKey(questions, selectedKey);

        var html = '';
        html += '<div class="wf-ai-fallback wf-ai-fallback-visual">';
        html += '  <div class="wf-ai-fallback-head">';
        html += '    <div><div class="wf-ai-fallback-kicker">Resolver sobre el canvas</div><div class="wf-ai-fallback-title">Elegí el punto rojo que querés completar</div></div>';
        html += '    <span class="wf-ai-fallback-badge">No se guarda todavía</span>';
        html += '  </div>';
        html += '  <div class="wf-ai-fallback-text">El canvas muestra el borrador. Al resolver esta duda se redibujará automáticamente y avanzará a la siguiente.</div>';
        html += renderFallbackResolvedSummary();

        if (selectedQuestion) {
            var currentIndex = 0;
            questions.forEach(function (q, idx) { if (q && q.key === selectedQuestion.key) currentIndex = idx; });
            html += '  <div class="wf-ai-fallback-progress">Duda ' + (currentIndex + 1) + ' de ' + questions.length + '</div>';
            html += '  <div class="wf-ai-fallback-question" data-wf-ai-question-key="' + htmlEncode(selectedQuestion.key || '') + '">';
            html += '    <div class="wf-ai-fallback-q-title">' + htmlEncode(selectedQuestion.title) + '</div>';
            html += '    <div class="wf-ai-fallback-q-detail">' + htmlEncode(selectedQuestion.detail) + '</div>';
            html += '    <div class="wf-ai-fallback-options">';
            (selectedQuestion.options || []).forEach(function (opt) {
                var idx = aiClarificationOptions.push({
                    clarification: opt.clarification || opt.label || '',
                    questionKey: selectedQuestion.key || '',
                    questionTitle: selectedQuestion.title || '',
                    label: opt.label || 'Usar opción'
                }) - 1;
                html += '<button type="button" class="btn wf-ai-fallback-btn ' + htmlEncode(opt.css || '') + '" data-wf-ai-clarify-index="' + idx + '">' + htmlEncode(opt.label || 'Usar opción') + '</button>';
            });
            html += '    </div>';
            html += '  </div>';
        } else if (canBuildGuidedResolution(userText)) {
            html += renderGuidedResolutionAction(res, userText);
            if (rewrite) {
                html += '  <details class="wf-ai-fallback-consolidated"><summary>Ver frase consolidada</summary>';
                html += '    <div>' + htmlEncode(rewrite) + '</div>';
                html += '  </details>';
            }
        } else if (rewrite && !sameFallbackPhrase(userText, rewrite) && aiFallbackAcceptedRewriteKey !== fallbackRewriteKey(rewrite)) {
            var rewriteIdx = aiRewriteOptions.push(rewrite) - 1;
            html += '  <div class="wf-ai-fallback-rewrite">';
            html += '    <div class="wf-ai-fallback-label">Frase clara sugerida</div>';
            html += '    <div class="wf-ai-fallback-rewrite-text">' + htmlEncode(rewrite) + '</div>';
            html += '    <button type="button" class="btn wf-ai-fallback-btn primary" data-wf-ai-rewrite-index="' + rewriteIdx + '">Usar esta frase y redibujar</button>';
            html += '  </div>';
        } else {
            html += '  <div class="wf-ai-fallback-pending actionable">';
            html += '    <div class="wf-ai-fallback-label">Alternativa segura</div>';
            html += '    <div>' + htmlEncode(fallbackResolutionText(res, userText)) + '</div>';
            html += '    <div class="wf-ai-fallback-next-actions">';
            html += '      <button type="button" class="btn wf-ai-fallback-btn" data-wf-ai-open-guide="1">Armarlo paso a paso</button>';
            html += '    </div>';
            html += '  </div>';
        }

        html += '</div>';
        return html;
    }

    function applyClarificationToPrompt(option) {
        var txt = $('wfAiPrompt');
        if (!txt) return;

        var info = (typeof option === 'object' && option)
            ? option
            : { clarification: String(option || ''), questionKey: '', questionTitle: '', label: '' };
        var clarification = String(info.clarification || '').trim();
        if (!clarification) return;

        markFallbackResolved(info.questionKey || '', info.questionTitle || '', info.label || '', clarification);
        aiVisualDraftSelectedIssueKey = '';

        // No acumulamos aclaraciones viejas indefinidamente en el texto visible.
        // Se conserva la frase original + las decisiones activas de esta sesión.
        var base = stripFallbackClarifications(cleanGuidePhraseText(txt.value || '')).trim();
        var adds = [];
        activeFallbackResolvedItems().forEach(function (x) {
            var c = String(x && x.clarification || '').trim();
            if (c) adds.push('Aclaración: ' + c);
        });
        txt.value = [base].concat(adds).filter(Boolean).join(' ');
        txt.focus();
        setStatus('Decisión tomada. Verificando nuevamente...', 'busy');
        setTimeout(verificarFrase, 80);
    }

    function replacePromptAndVerify(newPhrase) {
        var txt = $('wfAiPrompt');
        if (!txt) return;
        aiFallbackResolved = {};
        aiFallbackResolvedItems = [];
        aiFallbackBaseKey = '';
        aiFallbackAcceptedRewriteKey = fallbackRewriteKey(newPhrase || '');
        txt.value = cleanGuidePhraseText(newPhrase || '').trim();
        txt.focus();
        setStatus('Frase clara cargada. Verificando...', 'busy');
        setTimeout(verificarFrase, 80);
    }

    function openStepByStepGuide() {
        clearVisualDraft();
        var refreshGuidedResult = !!(lastAssistantResult && lastAssistantResult.requiresFinalConfirmation);
        resetGuidedPlanConfirmation();
        var txt = $('wfAiPrompt');
        if (txt) {
            var clean = buildSuggestedRewrite(txt.value || '') || stripFallbackClarifications(txt.value || '');
            txt.value = cleanGuidePhraseText(clean || '').trim();
        }
        var body = $('wfAiGuideBody');
        var toggle = $('wfAiGuideToggle');
        if (body && body.style.display === 'none' && toggle) toggle.click();
        setWideMode(true);
        if (refreshGuidedResult) renderResult(lastAssistantResult);
        setStatus('Modo paso a paso abierto. Cualquier confirmación anterior quedó invalidada hasta volver a revisar el plan.', 'ok');
    }

    function bindFallbackActions(container) {
        container = container || $('wfAiResult');
        if (!container) return;

        Array.prototype.forEach.call(container.querySelectorAll('[data-wf-ai-clarify-index]'), function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(btn.getAttribute('data-wf-ai-clarify-index') || '-1', 10);
                if (idx >= 0 && idx < aiClarificationOptions.length) {
                    removeFallbackQuestionImmediately(btn);
                    applyClarificationToPrompt(aiClarificationOptions[idx]);
                }
            });
        });

        Array.prototype.forEach.call(container.querySelectorAll('[data-wf-ai-rewrite-index]'), function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(btn.getAttribute('data-wf-ai-rewrite-index') || '-1', 10);
                if (idx >= 0 && idx < aiRewriteOptions.length) replacePromptAndVerify(aiRewriteOptions[idx]);
            });
        });

        Array.prototype.forEach.call(container.querySelectorAll('[data-wf-ai-open-guide]'), function (btn) {
            btn.addEventListener('click', openStepByStepGuide);
        });

        Array.prototype.forEach.call(container.querySelectorAll('[data-wf-ai-guided-resolution]'), function (btn) {
            btn.addEventListener('click', resolveWithGuidedPlan);
        });
    }

    // ------------------------------------------------------------
    // FIX84B: diálogo contractual universal (primera muestra)
    // ------------------------------------------------------------
    function fix84bPhraseKey(userText) {
        return normalizePhraseForSearch(cleanGuidePhraseText(userText || ''));
    }

    function resetFix84bDialogue() {
        aiFix84bAnswers = {};
        aiFix84bFingerprint = '';
        aiFix84bBasePhraseKey = '';
    }

    function syncFix84bSession(userText) {
        var key = fix84bPhraseKey(userText);
        if (!aiFix84bBasePhraseKey) {
            aiFix84bBasePhraseKey = key;
            return;
        }
        if (key !== aiFix84bBasePhraseKey) {
            aiFix84bAnswers = {};
            aiFix84bFingerprint = '';
            aiFix84bBasePhraseKey = key;
            aiVisualDraftSelectedIssueKey = '';
        }
    }

    function fix84bInfo(res) {
        return res && res.fix84b && res.fix84b.active ? res.fix84b : null;
    }

    function fix84bDraft(res) {
        var info = fix84bInfo(res);
        return info && info.interpretationDraft || null;
    }

    function fix84bNaturalInferences(res) {
        var info = fix84bInfo(res);
        var ctx = info && info.naturalContext;
        return (ctx && ctx.inferences) || [];
    }

    function fix84bRejectedAnswers(res) {
        var info = fix84bInfo(res);
        return (info && info.rejectedAnswers) || [];
    }

    function fix84bBlockingClarifications(res) {
        var draft = fix84bDraft(res);
        return ((draft && draft.clarifications) || []).filter(function (c) { return c && c.blocking; });
    }

    function fix84bCanResolveClarification(c) {
        if (!c) return false;
        var source = String(c.source || '');
        var kind = String(c.controlKind || 'text');
        // FIX84C2Be: las alternativas exclusivas (p.ej. Rol + Usuario en human.task)
        // también pertenecen al diálogo contractual y tienen resolvedor server-side.
        if (source !== 'contract_parameter' && source !== 'contract_requirement' && source !== 'contract_requirement_exclusive' && source !== 'contract_ambiguity') return false;
        return ['text', 'number', 'select', 'boolean', 'role_or_user', 'available_data', 'payload_editor', 'condition_builder'].indexOf(kind) >= 0;
    }

    function fix84bOwnsDialogue(res) {
        var blocking = fix84bBlockingClarifications(res);
        if (!blocking.length) return false;
        return blocking.every(fix84bCanResolveClarification);
    }

    function fix84bHasErrors(res) {
        var info = fix84bInfo(res);
        return !!(info && info.errors && info.errors.length);
    }

    function captureFix84bState(res, userText) {
        var info = fix84bInfo(res);
        if (!info) return;
        syncFix84bSession(userText || '');
        if (info.stale) {
            aiFix84bAnswers = {};
            aiFix84bFingerprint = info.fingerprint ? String(info.fingerprint) : '';
            return;
        }
        if (info.fingerprint) aiFix84bFingerprint = String(info.fingerprint);
    }

    function fix84bStatusLabel(status) {
        var map = {
            resolved: 'Resuelto',
            inferred: 'Inferido',
            ambiguous: 'Ambiguo',
            missing: 'Falta dato',
            unrecognized: 'No reconocido'
        };
        return map[String(status || '')] || String(status || '');
    }

    function fix84bVisualQuestions(res) {
        return fix84bBlockingClarifications(res).filter(fix84bCanResolveClarification).map(function (c) {
            return {
                key: String(c.id || ''),
                title: String(c.question || 'Necesito confirmar un dato'),
                detail: (c.nodeLabel ? String(c.nodeLabel) + ' · ' : '') + fix84bStatusLabel(c.status),
                nodeType: String(c.nodeType || ''),
                nodeLabel: String(c.nodeLabel || ''),
                actionIndex: typeof c.actionIndex === 'number' ? c.actionIndex : -1,
                options: []
            };
        });
    }

    function fix84bRoleUserOptionsHtml() {
        var html = '<option value="">— elegí destino —</option>';
        if (guideCatalog.roles && guideCatalog.roles.length) {
            html += '<optgroup label="Roles">';
            guideCatalog.roles.forEach(function (r) {
                var key = roleKey(r);
                if (key) html += '<option value="role::' + htmlEncode(key) + '">' + htmlEncode(roleLabel(r)) + '</option>';
            });
            html += '</optgroup>';
        }
        if (guideCatalog.users && guideCatalog.users.length) {
            html += '<optgroup label="Usuarios">';
            guideCatalog.users.forEach(function (u) {
                var key = userKey(u);
                if (key) html += '<option value="user::' + htmlEncode(key) + '">' + htmlEncode(userLabel(u)) + '</option>';
            });
            html += '</optgroup>';
        }
        return html;
    }

    function fix84bPayloadRowsHtml(currentValue) {
        var rows = [];
        if (currentValue && typeof currentValue === 'object' && !Array.isArray(currentValue)) {
            Object.keys(currentValue).forEach(function (k) { rows.push({ name: k, value: currentValue[k] }); });
        }
        if (!rows.length) rows.push({ name: '', value: '' });
        return rows.map(function (row) {
            return '<div class="wf-ai-queue-field-row" data-fix84b-payload-row="1">' +
                '<div class="wf-ai-queue-field-head"><strong>Campo del mensaje</strong><button type="button" class="btn wf-ai-queue-remove" data-fix84b-payload-remove="1">Quitar</button></div>' +
                '<label class="wf-ai-queue-mini-label">Nombre</label>' +
                '<input class="wf-ai-input" data-fix84b-payload-name="1" value="' + htmlEncode(row.name || '') + '" placeholder="Ej.: origen, instanceId" />' +
                '<label class="wf-ai-queue-mini-label">Valor</label>' +
                '<input class="wf-ai-input" data-fix84b-payload-value="1" value="' + htmlEncode(row.value == null ? '' : row.value) + '" placeholder="Texto fijo o dato disponible" />' +
                '<select class="wf-ai-select" data-fix84b-payload-source="1">' + availableFieldOptionsWithBlank('', '— elegir un dato disponible —') + '</select>' +
                '</div>';
        }).join('');
    }

    // FIX84C2C1: el mismo ConditionBuilder contractual puede resolver una condición
    // simple, compuesta ALL/ANY o una expresión avanzada. Reutiliza Datos disponibles y
    // los operadores del editor Paso a paso; no obliga a escribir propiedades internas.
    function fix84c2c1ConditionRuleRowHtml(fieldPath, selectedOp, value) {
        var path = String(fieldPath || defaultConditionField() || '').trim();
        var meta = findAvailableField(path) || { type: 'texto' };
        var op = String(selectedOp || '').trim();
        return '<div class="wf-ai-queue-field-row" data-fix84b-condition-rule="1">' +
            '<div class="wf-ai-queue-field-head"><strong>Regla</strong><button type="button" class="btn wf-ai-queue-remove" data-fix84b-condition-rule-remove="1">Quitar</button></div>' +
            '<label class="wf-ai-queue-mini-label">Dato</label><select class="wf-ai-select" data-fix84b-condition-rule-field="1">' + availableFieldOptions(path) + '</select>' +
            '<label class="wf-ai-queue-mini-label">Comparación</label><select class="wf-ai-select" data-fix84b-condition-rule-op="1">' + conditionOperatorOptions(meta.type, op) + '</select>' +
            '<div data-fix84b-condition-rule-value-row="1"><label class="wf-ai-queue-mini-label">Valor</label><input class="wf-ai-input" data-fix84b-condition-rule-value="1" value="' + htmlEncode(value == null ? '' : value) + '" placeholder="Valor de comparación" /></div>' +
            '</div>';
    }

    function fix84c2c1SyncConditionRule(row) {
        if (!row) return;
        var field = row.querySelector('[data-fix84b-condition-rule-field]');
        var op = row.querySelector('[data-fix84b-condition-rule-op]');
        var valueRow = row.querySelector('[data-fix84b-condition-rule-value-row]');
        if (!field || !op) return;
        var current = String(op.value || '');
        var meta = findAvailableField(String(field.value || '')) || { type: 'texto' };
        op.innerHTML = conditionOperatorOptions(meta.type, current);
        if (valueRow) valueRow.style.display = operatorNeedsValue(op.value) ? '' : 'none';
    }

    function fix84c2c1SyncConditionBuilder(control) {
        if (!control) return;
        var mode = control.querySelector('[data-fix84b-condition-mode]');
        var selected = String(mode && mode.value || 'simple');
        var simple = control.querySelector('[data-fix84b-condition-simple]');
        var compound = control.querySelector('[data-fix84b-condition-compound]');
        var expression = control.querySelector('[data-fix84b-condition-expression-box]');
        if (simple) simple.style.display = selected === 'simple' ? '' : 'none';
        if (compound) compound.style.display = selected === 'compound' ? '' : 'none';
        if (expression) expression.style.display = selected === 'expression' ? '' : 'none';

        var simpleOp = control.querySelector('[data-fix84b-condition-op]');
        var simpleValueRow = control.querySelector('[data-fix84b-condition-value-row]');
        if (simpleValueRow && simpleOp) simpleValueRow.style.display = operatorNeedsValue(simpleOp.value) ? '' : 'none';

        Array.prototype.forEach.call(control.querySelectorAll('[data-fix84b-condition-rule]'), fix84c2c1SyncConditionRule);
    }

    function fix84bControlHtml(c) {
        c = c || {};
        var id = String(c.id || '');
        var kind = String(c.controlKind || 'text');
        var current = c.currentValue == null ? '' : c.currentValue;
        var html = '<div class="wf-ai-fix84b-control" data-fix84b-control="' + htmlEncode(kind) + '" data-fix84b-id="' + htmlEncode(id) + '">';

        if (kind === 'select' && c.options && c.options.length) {
            html += '<div class="wf-ai-fix84b-choice-list">';
            c.options.forEach(function (opt, idx) {
                html += '<button type="button" class="btn wf-ai-fix84b-choice' + (idx === 0 ? ' primary' : '') + '" data-fix84b-option="' + htmlEncode(opt) + '">' + htmlEncode(opt) + '</button>';
            });
            html += '</div></div>';
            return html;
        }

        if (kind === 'role_or_user') {
            html += '<label class="wf-ai-fix84b-label">Destino real</label><select class="wf-ai-select" data-fix84b-role-user="1">' + fix84bRoleUserOptionsHtml() + '</select>';
        } else if (kind === 'available_data') {
            html += '<label class="wf-ai-fix84b-label">Dato disponible</label><select class="wf-ai-select" data-fix84b-available="1">' + availableFieldOptionsWithBlank('', '— elegir dato —') + '</select>';
            html += '<div class="wf-ai-fix84b-or">o escribirlo</div><input class="wf-ai-input" data-fix84b-manual="1" value="' + htmlEncode(current || '') + '" placeholder="Nombre del dato" />';
        } else if (kind === 'payload_editor') {
            html += '<label class="wf-ai-fix84b-label">Mensaje simple</label><input class="wf-ai-input" data-fix84b-payload-text="1" value="" placeholder="Ej.: Solicitud recibida" />';
            html += '<div class="wf-ai-fix84b-or">o armá campos sin escribir JSON</div>';
            html += '<div class="wf-ai-queue-fields" data-fix84b-payload-rows="1">' + fix84bPayloadRowsHtml(current) + '</div>';
            html += '<button type="button" class="btn wf-ai-queue-add" data-fix84b-payload-add="1">Agregar campo</button>';
        } else if (kind === 'condition_builder') {
            var initialField = defaultConditionField();
            var initialMeta = findAvailableField(initialField) || { type: 'texto' };
            html += '<label class="wf-ai-fix84b-label">Forma de la condición</label><select class="wf-ai-select" data-fix84b-condition-mode="1"><option value="simple">Una condición</option><option value="compound">Varias reglas</option><option value="expression">Expresión avanzada</option></select>';
            html += '<div data-fix84b-condition-simple="1">';
            html += '<label class="wf-ai-fix84b-label">Dato a evaluar</label><select class="wf-ai-select" data-fix84b-condition-field="1">' + availableFieldOptions(initialField) + '</select>';
            html += '<label class="wf-ai-fix84b-label">Comparación</label><select class="wf-ai-select" data-fix84b-condition-op="1">' + conditionOperatorOptions(initialMeta.type, '') + '</select>';
            html += '<div data-fix84b-condition-value-row="1"><label class="wf-ai-fix84b-label">Valor</label><input class="wf-ai-input" data-fix84b-condition-value="1" placeholder="Valor de comparación" /></div>';
            html += '</div>';
            html += '<div data-fix84b-condition-compound="1" style="display:none">';
            html += '<label class="wf-ai-fix84b-label">Cómo combinar las reglas</label><select class="wf-ai-select" data-fix84b-condition-rules-mode="1"><option value="all">Todas deben cumplirse / Y</option><option value="any">Cualquiera puede cumplirse / O</option></select>';
            html += '<div class="wf-ai-queue-fields" data-fix84b-condition-rules="1">' + fix84c2c1ConditionRuleRowHtml(initialField, '', '') + '</div>';
            html += '<button type="button" class="btn wf-ai-queue-add" data-fix84b-condition-rule-add="1">Agregar regla</button>';
            html += '</div>';
            html += '<div data-fix84b-condition-expression-box="1" style="display:none"><label class="wf-ai-fix84b-label">Expresión avanzada</label><textarea class="wf-ai-input" data-fix84b-condition-expression="1" rows="3" placeholder="Ej.: contains(lower(${input.text}), \'urgente\')"></textarea></div>';
        } else if (kind === 'boolean') {
            html += '<select class="wf-ai-select" data-fix84b-value="1"><option value="true">Sí</option><option value="false">No</option></select>';
        } else if (kind === 'number') {
            html += '<input type="number" class="wf-ai-input" data-fix84b-value="1" value="' + htmlEncode(current || '') + '" />';
        } else if (kind === 'select') {
            html += '<select class="wf-ai-select" data-fix84b-value="1">';
            (c.options || []).forEach(function (opt) { html += '<option value="' + htmlEncode(opt) + '">' + htmlEncode(opt) + '</option>'; });
            html += '</select>';
        } else {
            html += '<input class="wf-ai-input" data-fix84b-value="1" value="' + htmlEncode(current || '') + '" placeholder="Escribí la respuesta" />';
        }

        html += '<div class="wf-ai-fix84b-actions"><button type="button" class="btn wf-ai-fix84b-resolve" data-fix84b-submit="1">Usar esta respuesta</button><button type="button" class="btn" data-wf-ai-open-guide="1">Editar paso a paso</button></div>';
        html += '</div>';
        return html;
    }

    function renderFix84bDialogue(res) {
        var info = fix84bInfo(res) || {};
        var draft = fix84bDraft(res) || {};
        var blocking = fix84bBlockingClarifications(res).filter(fix84bCanResolveClarification);
        var selectedKey = ensureVisualDraftIssueSelection(fix84bVisualQuestions(res));
        var selected = null;
        blocking.forEach(function (c) { if (!selected && String(c.id || '') === selectedKey) selected = c; });
        if (!selected) selected = blocking[0] || null;

        var html = '<div class="wf-ai-fix84b">';
        html += '<div class="wf-ai-fix84b-head"><div><div class="wf-ai-fix84b-kicker">FIX84B · Interpretación natural</div><div class="wf-ai-fix84b-title">Esto entendí y esto necesito confirmar</div></div><span class="wf-ai-fix84b-badge">No se aplica todavía</span></div>';

        if (draft.nodes && draft.nodes.length) {
            html += '<div class="wf-ai-fix84b-understood"><div class="wf-ai-fix84b-section-title">Entendido</div><ul>';
            draft.nodes.forEach(function (n) {
                html += '<li><strong>' + htmlEncode(n.summary || n.label || n.nodeType || 'Paso') + '</strong> <span class="wf-ai-fix84b-status ' + htmlEncode(n.status || '') + '">' + htmlEncode(fix84bStatusLabel(n.status)) + '</span></li>';
            });
            html += '</ul></div>';
        }

        if (info.appliedAnswers && info.appliedAnswers.length) {
            html += '<div class="wf-ai-fix84b-resolved"><div class="wf-ai-fix84b-section-title">Decisiones tomadas</div><ul>';
            info.appliedAnswers.forEach(function (a) { html += '<li>' + htmlEncode(a.question || 'Decisión') + ' → <strong>' + htmlEncode(a.answer || '') + '</strong></li>'; });
            html += '</ul></div>';
        }

        var rejected = fix84bRejectedAnswers(res);
        if (rejected.length) {
            html += '<div class="wf-ai-fix84b-unsupported"><div class="wf-ai-fix84b-section-title">Entendí tu elección, pero no puedo aplicarla dentro de este workflow</div>';
            rejected.forEach(function (a) {
                html += '<div class="wf-ai-fix84b-unsupported-answer"><strong>Elegiste:</strong> ' + htmlEncode(a.answer || '') + '</div>';
                if (a.reason) html += '<div>' + htmlEncode(a.reason) + '</div>';
                if (a.suggestedAnswer) html += '<div class="wf-ai-fix84b-unsupported-next">Si esa infraestructura se prepara por otro medio, elegí <strong>' + htmlEncode(a.suggestedAnswer) + '</strong>.</div>';
            });
            html += '</div>';
        } else if (info.errors && info.errors.length) {
            html += renderList('Necesito corregir esta respuesta', info.errors, 'wf-ai-error-list');
        }

        if (selected) {
            var idx = blocking.indexOf(selected);
            html += '<div class="wf-ai-fix84b-progress">Pregunta ' + (idx + 1) + ' de ' + blocking.length + '</div>';
            html += '<div class="wf-ai-fix84b-question" data-wf-ai-question-key="' + htmlEncode(selected.id || '') + '">';
            html += '<div class="wf-ai-fix84b-q-node">' + htmlEncode(selected.nodeLabel || selected.nodeType || 'Paso') + '</div>';
            html += '<div class="wf-ai-fix84b-q-title">' + htmlEncode(selected.question || 'Necesito confirmar un dato') + '</div>';
            html += fix84bControlHtml(selected);
            html += '</div>';
        }
        html += '</div>';
        return html;
    }

    function renderFix84bResolvedSummary(res) {
        var info = fix84bInfo(res);
        if (!info || fix84bBlockingClarifications(res).length) return '';
        var answers = info.appliedAnswers || [];
        var inferences = fix84bNaturalInferences(res);
        if (!answers.length && !inferences.length) return '';

        var html = '<div class="wf-ai-fix84b wf-ai-fix84b-complete"><div class="wf-ai-fix84b-head"><div><div class="wf-ai-fix84b-kicker">Interpretación resuelta</div><div class="wf-ai-fix84b-title">Esto entendí sin pedirte nombres técnicos</div></div><span class="wf-ai-fix84b-badge complete">Listo para revisar</span></div>';
        if (answers.length) {
            html += '<div class="wf-ai-fix84b-section-title">Decisiones confirmadas</div><ul>';
            answers.forEach(function (a) { html += '<li>' + htmlEncode(a.question || 'Decisión') + ' → <strong>' + htmlEncode(a.answer || '') + '</strong></li>'; });
            html += '</ul>';
        }
        if (inferences.length) {
            html += '<div class="wf-ai-fix84b-section-title wf-ai-fix84b-inference-title">Inferido por contexto</div><ul>';
            inferences.forEach(function (x) {
                html += '<li><strong>' + htmlEncode(x.label || 'Dato') + ':</strong> ' + htmlEncode(x.value || '');
                if (x.explanation) html += '<div class="wf-ai-fix84b-inference-explanation">' + htmlEncode(x.explanation) + '</div>';
                html += '</li>';
            });
            html += '</ul>';
        }
        html += '<div class="wf-ai-fix84b-implicit-note">Inicio y Fin normales se agregan automáticamente al workflow; no hace falta escribirlos en la frase.</div>';
        html += '</div>';
        return html;
    }

    function fix84bNewPayloadRowHtml() {
        return fix84bPayloadRowsHtml(null);
    }

    function collectFix84bAnswer(control) {
        if (!control) return null;
        var kind = String(control.getAttribute('data-fix84b-control') || 'text');
        if (kind === 'role_or_user') {
            var ru = control.querySelector('[data-fix84b-role-user]');
            var raw = String(ru && ru.value || '');
            var sep = raw.indexOf('::');
            if (sep <= 0) return null;
            return { kind: raw.substring(0, sep), value: raw.substring(sep + 2) };
        }
        if (kind === 'available_data') {
            var available = control.querySelector('[data-fix84b-available]');
            var manual = control.querySelector('[data-fix84b-manual]');
            return String(available && available.value || manual && manual.value || '').trim();
        }
        if (kind === 'payload_editor') {
            var payload = {};
            var hasFields = false;
            Array.prototype.forEach.call(control.querySelectorAll('[data-fix84b-payload-row]'), function (row) {
                var name = String((row.querySelector('[data-fix84b-payload-name]') || {}).value || '').trim();
                var value = String((row.querySelector('[data-fix84b-payload-value]') || {}).value || '').trim();
                if (!name) return;
                hasFields = true;
                payload[name] = value;
            });
            if (hasFields) return payload;
            var text = control.querySelector('[data-fix84b-payload-text]');
            return String(text && text.value || '').trim();
        }
        if (kind === 'condition_builder') {
            var modeControl = control.querySelector('[data-fix84b-condition-mode]');
            var mode = String(modeControl && modeControl.value || 'simple');
            if (mode === 'compound') {
                var rulesMode = control.querySelector('[data-fix84b-condition-rules-mode]');
                var rules = [];
                Array.prototype.forEach.call(control.querySelectorAll('[data-fix84b-condition-rule]'), function (row) {
                    var fieldRule = row.querySelector('[data-fix84b-condition-rule-field]');
                    var opRule = row.querySelector('[data-fix84b-condition-rule-op]');
                    var valueRule = row.querySelector('[data-fix84b-condition-rule-value]');
                    var fieldValue = String(fieldRule && fieldRule.value || '').trim();
                    var opValue = String(opRule && opRule.value || '').trim();
                    if (!fieldValue || !opValue) return;
                    var rule = { field: fieldValue, op: opValue };
                    if (operatorNeedsValue(opValue)) rule.value = String(valueRule && valueRule.value || '').trim();
                    rules.push(rule);
                });
                return { mode: 'compound', rulesMode: String(rulesMode && rulesMode.value || 'all'), rules: rules };
            }
            if (mode === 'expression') {
                var expression = control.querySelector('[data-fix84b-condition-expression]');
                return { mode: 'expression', expression: String(expression && expression.value || '').trim() };
            }
            var field = control.querySelector('[data-fix84b-condition-field]');
            var op = control.querySelector('[data-fix84b-condition-op]');
            var value = control.querySelector('[data-fix84b-condition-value]');
            return { mode: 'simple', field: String(field && field.value || ''), op: String(op && op.value || ''), value: String(value && value.value || '') };
        }
        var input = control.querySelector('[data-fix84b-value]');
        if (!input) return null;
        if (kind === 'boolean') return String(input.value) === 'true';
        if (kind === 'number') return String(input.value || '').trim();
        return String(input.value || '').trim();
    }

    function submitFix84bAnswer(id, value) {
        id = String(id || '');
        if (!id) return;
        if (value == null || (typeof value === 'string' && !value.trim())) {
            setStatus('Completá la respuesta antes de continuar.', 'warn');
            return;
        }
        aiFix84bAnswers[id] = value;
        resetGuidedPlanConfirmation();
        var userText = getCurrentAiPhrase();
        setStatus('Aplicando la decisión y regenerando el plan...', 'busy');
        callAiAssistant(userText, function (res) {
            captureFix84bState(res, userText);
            renderPhraseVerification(res, userText);
            if (fix84bBlockingClarifications(res).length) setStatus('Decisión aplicada. Quedan aclaraciones pendientes.', 'warn');
            else if (res && res.validation && res.validation.ok) setStatus('Interpretación resuelta. Revisá el plan antes de crear el workflow.', 'ok');
            else setStatus('La respuesta se aplicó, pero todavía hay algo para revisar.', 'warn');
        }, function (err) {
            setStatus('No se pudo aplicar la aclaración: ' + (err.message || String(err)), 'error');
        });
    }

    function bindFix84bActions(container) {
        container = container || $('wfAiResult');
        if (!container) return;
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-option]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var control = btn.closest ? btn.closest('[data-fix84b-id]') : null;
                submitFix84bAnswer(control && control.getAttribute('data-fix84b-id'), btn.getAttribute('data-fix84b-option'));
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-submit]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var control = btn.closest ? btn.closest('[data-fix84b-id]') : null;
                if (!control) return;
                submitFix84bAnswer(control.getAttribute('data-fix84b-id'), collectFix84bAnswer(control));
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-mode]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var control = sel.closest ? sel.closest('[data-fix84b-id]') : null;
                fix84c2c1SyncConditionBuilder(control);
            });
            var control = sel.closest ? sel.closest('[data-fix84b-id]') : null;
            fix84c2c1SyncConditionBuilder(control);
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-field]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var control = sel.closest ? sel.closest('[data-fix84b-id]') : null;
                var op = control && control.querySelector('[data-fix84b-condition-op]');
                if (op) {
                    var meta = findAvailableField(String(sel.value || '')) || { type: 'texto' };
                    op.innerHTML = conditionOperatorOptions(meta.type, op.value);
                }
                fix84c2c1SyncConditionBuilder(control);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-op]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var control = sel.closest ? sel.closest('[data-fix84b-id]') : null;
                fix84c2c1SyncConditionBuilder(control);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-rule-add]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var control = btn.closest ? btn.closest('[data-fix84b-id]') : null;
                var rows = control && control.querySelector('[data-fix84b-condition-rules]');
                if (rows) rows.insertAdjacentHTML('beforeend', fix84c2c1ConditionRuleRowHtml(defaultConditionField(), '', ''));
                bindFix84bActions(container);
                fix84c2c1SyncConditionBuilder(control);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-rule-remove]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var row = btn.closest ? btn.closest('[data-fix84b-condition-rule]') : null;
                var rows = row && row.parentNode;
                if (row && rows && rows.querySelectorAll('[data-fix84b-condition-rule]').length > 1) rows.removeChild(row);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-rule-field]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var row = sel.closest ? sel.closest('[data-fix84b-condition-rule]') : null;
                fix84c2c1SyncConditionRule(row);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-condition-rule-op]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var row = sel.closest ? sel.closest('[data-fix84b-condition-rule]') : null;
                var valueRow = row && row.querySelector('[data-fix84b-condition-rule-value-row]');
                if (valueRow) valueRow.style.display = operatorNeedsValue(sel.value) ? '' : 'none';
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-payload-add]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var control = btn.closest ? btn.closest('[data-fix84b-id]') : null;
                var rows = control && control.querySelector('[data-fix84b-payload-rows]');
                if (rows) rows.insertAdjacentHTML('beforeend', fix84bNewPayloadRowHtml());
                bindFix84bActions(container);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-payload-remove]'), function (btn) {
            if (btn.getAttribute('data-fix84b-bound') === '1') return;
            btn.setAttribute('data-fix84b-bound', '1');
            btn.addEventListener('click', function () {
                var row = btn.closest ? btn.closest('[data-fix84b-payload-row]') : null;
                if (row && row.parentNode) row.parentNode.removeChild(row);
            });
        });
        Array.prototype.forEach.call(container.querySelectorAll('[data-fix84b-payload-source]'), function (sel) {
            if (sel.getAttribute('data-fix84b-bound') === '1') return;
            sel.setAttribute('data-fix84b-bound', '1');
            sel.addEventListener('change', function () {
                var row = sel.closest ? sel.closest('[data-fix84b-payload-row]') : null;
                var value = row && row.querySelector('[data-fix84b-payload-value]');
                if (value && sel.value) value.value = '${' + sel.value + '}';
            });
        });
    }

    function verificationStatus(res, userText) {
        if (!res || !res.ok) return { css: 'error', label: 'FALLA', title: 'No se pudo interpretar la frase' };
        if (fix84bRejectedAnswers(res).length) return { css: 'warn', label: 'REVISAR', title: 'Entendí tu elección, pero no puede aplicarse dentro de este workflow' };
        if (fix84bHasErrors(res)) return { css: 'warn', label: 'REVISAR', title: 'La respuesta necesita una corrección' };
        if (fix84bOwnsDialogue(res)) return { css: 'warn', label: 'ACLARAR', title: 'Entendí el flujo, pero necesito confirmar una decisión' };
        var validation = res.validation || {};
        var plan = res.plan || {};
        var missing = plan.missingData || [];
        var errors = validation.errors || [];
        var warningGroups = collectPhraseWarnings(res, validation, plan);

        if (shouldLetFallbackOwnErrors(res, userText)) return { css: 'warn', label: 'GUIADO', title: 'La frase necesita resolución guiada, no otro reintento libre' };
        if (errors.length || missing.length) return { css: 'error', label: 'REVISAR', title: 'La frase tiene errores o datos faltantes' };
        if (warningGroups.functional.length) return { css: 'warn', label: 'OK CON AVISOS', title: 'La frase se entiende, pero conviene revisar advertencias funcionales' };
        return { css: 'ok', label: 'OK', title: 'La frase se entiende y no tiene advertencias funcionales' };
    }

    function copyToClipboard(text, okMessage) {
        text = String(text == null ? '' : text);
        if (!text) {
            setStatus('No hay texto para copiar.', 'warn');
            return;
        }
        function fallback() {
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', 'readonly');
            ta.style.position = 'fixed';
            ta.style.left = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); setStatus(okMessage || 'Copiado.', 'ok'); }
            catch (e) { setStatus('No se pudo copiar automáticamente.', 'warn'); }
            document.body.removeChild(ta);
        }
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function () {
                setStatus(okMessage || 'Copiado.', 'ok');
            }).catch(fallback);
        } else {
            fallback();
        }
    }

    function prepareVerifiedPlanContract(res, userText) {
        if (!res || !res.ok) return;
        var plan = res.plan || {};
        var actions = plan.actions || [];
        if (!actions.length) return;

        var validation = res.validation || (res.validation = {});
        validation.errors = validation.errors || [];
        validation.warnings = validation.warnings || [];

        // Los planes del proveedor ML.NET llegan sin el contrato cliente fix74b.
        // Debe existir y validarse antes de habilitar "Crear workflow".
        if (!plan.contract) plan.contract = buildFix74PlanContract(plan, userText || '');

        var contractErrors = validateFix74PlanContract(plan, userText || '', true);
        contractErrors.forEach(function (e) { pushUnique(validation.errors, e); });

        var selfTest = runFix74ContractSelfTest();
        if (!selfTest.ok) pushUnique(validation.errors, 'La matriz interna del contrato fix74b no pasó completa.');

        validation.contract = {
            ok: contractErrors.length === 0 && selfTest.ok,
            fingerprint: plan.contract && plan.contract.fingerprint || '',
            graphFingerprint: plan.contract && plan.contract.graphFingerprint || '',
            matrixPassed: selfTest.passed,
            matrixTotal: selfTest.total
        };
        validation.ok = validation.errors.length === 0;
    }

    function renderPhraseVerification(res, userText) {
        var out = $('wfAiResult');
        if (!out) return;
        lastPlan = null;
        lastAssistantResult = res || null;

        // Al cambiar la frase no deben sobrevivir decisiones de mecanismos anteriores.
        syncFallbackSession(userText || '');
        captureFix84bState(res, userText || '');

        var contractDialogueActive = fix84bOwnsDialogue(res);
        var contractErrorsActive = fix84bHasErrors(res);
        if (!contractDialogueActive && !contractErrorsActive) prepareVerifiedPlanContract(res, userText || '');

        var status = verificationStatus(res, userText);
        var plan = (res && res.plan) || {};
        var validation = (res && res.validation) || {};
        var missing = plan.missingData || [];
        var warningGroups = collectPhraseWarnings(res, validation, plan);
        var functionalWarnings = warningGroups.functional || [];
        var technicalWarnings = warningGroups.technical || [];
        var fallbackActive = !contractDialogueActive && fallbackNeedsHelp(res, userText);

        var technical = res || { ok: false, error: 'Sin respuesta de verificación.' };
        var technicalJson = JSON.stringify(technical, null, 2);
        var nodeTypes = planNodeTypes(plan);
        var understood = phraseVerificationText(plan);

        var html = '';
        html += '<div class="wf-ai-verify-card ' + htmlEncode(status.css) + '">';
        html += '  <div class="wf-ai-verify-head">';
        html += '    <div><div class="wf-ai-verify-kicker">Paso 1 de 5 · Verificar frase</div><div class="wf-ai-verify-title">' + htmlEncode(status.title) + '</div></div>';
        html += '    <span class="wf-ai-verify-badge ' + htmlEncode(status.css) + '">' + htmlEncode(status.label) + '</span>';
        html += '  </div>';
        html += '  <div class="wf-ai-verify-phrase">' + htmlEncode(userText || '') + '</div>';
        html += '</div>';

        if (contractDialogueActive) {
            html += renderFix84bDialogue(res);
        } else if (fallbackActive) {
            html += renderFallbackHelp(res, userText);
        } else if (!res || !res.ok) {
            html += '<div class="wf-ai-error">' + htmlEncode((res && (res.messageToUser || res.error)) || 'No se pudo verificar la frase.') + '</div>';
        } else {
            html += renderFix84bResolvedSummary(res);
            html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Lectura funcional verificada</div>';
            if (understood.length) {
                html += '<ul>';
                understood.forEach(function (x) { html += '<li>' + htmlEncode(x) + '</li>'; });
                html += '</ul>';
            } else {
                html += '<div class="wf-ai-meta">No se detectaron nodos para crear.</div>';
            }
            html += '</div>';

            if (nodeTypes.length) {
                html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Nodos que se crearían</div><div class="wf-ai-node-chip-list">';
                nodeTypes.forEach(function (x) { html += '<span class="wf-ai-node-chip">' + htmlEncode(x) + '</span>'; });
                html += '</div></div>';
            }

            if (plan.branchPlan && plan.branchPlan.branches && plan.branchPlan.branches.length) {
                html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Ramas detectadas</div><ul>';
                plan.branchPlan.branches.forEach(function (b) {
                    html += '<li><strong>' + htmlEncode(b.condition || '') + '</strong><br />SI: ' + htmlEncode(b.truePath || '') + '<br />NO: ' + htmlEncode(b.falsePath || '') + '</li>';
                });
                html += '</ul></div>';
            }

            html += renderList('Datos faltantes', missing, 'wf-ai-warning-list');
            html += renderList('Errores', validation.errors || [], 'wf-ai-error-list');
            html += renderList('Advertencias funcionales', functionalWarnings, 'wf-ai-warning-list');
        }

        if (technicalWarnings.length) {
            html += '<details class="wf-ai-tech-warnings"><summary>Advertencias técnicas ocultas (' + technicalWarnings.length + ')</summary>';
            html += renderList('Solo diagnóstico técnico', technicalWarnings, 'wf-ai-tech-warning-list');
            html += '</details>';
        }

        var hasValidationErrors = ((validation.errors || []).length > 0) || (missing.length > 0);
        var hasContractBlocking = fix84bBlockingClarifications(res).length > 0;
        var canInterpretSafely = !!(res && res.ok && !hasValidationErrors && !fallbackActive && !hasContractBlocking && !contractErrorsActive);

        html += '<div class="wf-ai-actions wf-ai-verify-actions">';
        html += '<button type="button" class="btn" id="wfAiVerifyCopyPhrase">Copiar frase</button>';
        html += '<button type="button" class="btn" id="wfAiVerifyCopyJson">Copiar JSON técnico</button>';
        if (canInterpretSafely) {
            html += '<button type="button" class="btn wf-ai-create-workflow" id="wfAiCreateVerifiedWorkflow">Crear workflow</button>';
            html += '<button type="button" class="btn" id="wfAiVerifyInterpret">Ver resumen detallado</button>';
        }
        html += '</div>';
        html += '<details class="wf-ai-json"><summary>Ver JSON técnico de verificación</summary><pre id="wfAiVerifyJsonPre">' + htmlEncode(technicalJson) + '</pre></details>';

        out.innerHTML = html;
        bindFix84bActions(out);
        bindFallbackActions(out);

        var copyPhrase = $('wfAiVerifyCopyPhrase');
        if (copyPhrase) copyPhrase.addEventListener('click', function () { copyToClipboard(userText || '', 'Frase copiada.'); });

        var copyJson = $('wfAiVerifyCopyJson');
        if (copyJson) copyJson.addEventListener('click', function () { copyToClipboard(technicalJson, 'JSON técnico copiado.'); });

        var createVerified = $('wfAiCreateVerifiedWorkflow');
        if (createVerified) createVerified.addEventListener('click', function () {
            prepareVerifiedPlanContract(res, userText || '');
            lastPlan = plan;
            lastAssistantResult = res || null;
            confirmGuidedPlan(lastPlan);
            applyLastPlan();
        });

        var interpretBtn = $('wfAiVerifyInterpret');
        if (interpretBtn) interpretBtn.addEventListener('click', function () { interpretar(); });

        var visualQuestions = contractDialogueActive
            ? fix84bVisualQuestions(res)
            : (fallbackActive ? buildFallbackQuestions(res, userText) : []);
        renderVisualDraft(res, userText, {
            questions: visualQuestions,
            complete: !visualQuestions.length && canInterpretSafely
        });

        if (visualQuestions.length && aiVisualDraftSelectedIssueKey) {
            setTimeout(function () {
                var card = document.querySelector('[data-wf-ai-question-key="' + aiVisualDraftSelectedIssueKey.replace(/"/g, '') + '"]');
                if (card && card.scrollIntoView) card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }, 0);
        }
    }

    function getCurrentAiPhrase() {
        var txt = $('wfAiPrompt');
        var userText = cleanGuidePhraseText(txt ? (txt.value || '').trim() : '');
        if (txt && txt.value !== userText) txt.value = userText;
        return userText;
    }

    function callAiAssistant(userText, done, fail, always) {
        syncFix84bSession(userText || '');
        fetch('Api/WF_AiAssistant.ashx', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({
                userText: userText,
                workflowJson: currentWorkflowJson(),
                interpretationFingerprint: aiFix84bFingerprint || '',
                clarificationAnswers: aiFix84bAnswers || {}
            })
        })
            .then(function (r) { return r.json(); })
            .then(done)
            .catch(function (err) {
                if (fail) fail(err);
            })
            .finally(function () {
                if (always) always();
            });
    }

    // FIX84C1/FIX84C2A/FIX84C2B: el Paso a paso no interpreta lenguaje natural, pero sí entrega su
    // plan candidato al mismo resolved-node builder C# que usa la ruta por frase.
    function callAiPlanNormalizer(plan, userText, done, fail, always) {
        fetch('Api/WF_AiAssistant.ashx', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({
                mode: 'normalize_plan',
                sourceKind: 'step_by_step',
                userText: userText || '',
                plan: plan || {}
            })
        })
            .then(function (r) { return r.json(); })
            .then(done)
            .catch(function (err) { if (fail) fail(err); })
            .finally(function () { if (always) always(); });
    }

    function finalizeStructuredGuideResult(plan, userText, commonResponse) {
        if (!plan) return null;
        plan.assistantVersion = 'constructor-structured-fix84c2b';
        plan.contract = buildFix74PlanContract(plan, userText || '');

        var validation = buildFunctionalValidation();

        // FIX84C2Bg2c: APTO/NO APTO ya vienen definidos por el editor estructurado.
        // No volvemos a inferir la rama leyendo la frase descriptiva generada por Paso a paso;
        // esa validación legacy confundía el rol dueño de la tarea con el destinatario/contenido
        // de una acción de rama (por ejemplo Logger Warn) y fabricaba un SI/NO faltante.
        if (!hasHumanTaskResultBranches()) {
            var consistencyErrors = guidedDecisionConsistencyErrors(plan, userText || '');
            consistencyErrors.forEach(function (e) { pushUnique(validation.errors, e); });
        }

        var serverValidation = commonResponse && commonResponse.validation || {};
        (serverValidation.errors || []).forEach(function (e) { pushUnique(validation.errors, e); });
        (serverValidation.warnings || []).forEach(function (w) { pushUnique(validation.warnings, w); });

        var contractErrors = validateFix74PlanContract(plan, userText || '', true);
        contractErrors.forEach(function (e) { pushUnique(validation.errors, e); });

        var selfTest = runFix74ContractSelfTest();
        if (!selfTest.ok) pushUnique(validation.errors, 'La matriz interna del contrato fix74b no pasó completa.');
        validation.contract = {
            ok: contractErrors.length === 0 && selfTest.ok,
            fingerprint: plan.contract.fingerprint,
            graphFingerprint: plan.contract.graphFingerprint,
            matrixPassed: selfTest.passed,
            matrixTotal: selfTest.total
        };
        validation.ok = validation.errors.length === 0;
        aiFix74LastContractCheck = validation.contract;

        return {
            ok: true,
            provider: 'constructor-local + contract-common',
            sourceKind: 'step_by_step',
            structuredSource: true,
            model: 'Constructor IA estructurado FIX84C2B',
            messageToUser: plan.messageToUser,
            plan: plan,
            validation: validation,
            fix84c1: commonResponse && commonResponse.fix84c1 || null,
            fix84c2a: commonResponse && (commonResponse.fix84c2a || commonResponse.fix84c1) || null,
            fix84c2b: commonResponse && (commonResponse.fix84c2b || commonResponse.fix84c2ab || commonResponse.fix84c2a || commonResponse.fix84c1) || null,
            rawText: userText || ''
        };
    }

    function planHasFix84C1CoveredNode(plan) {
        var actions = plan && plan.actions || [];
        for (var i = 0; i < actions.length; i++) {
            var nodeType = String(actions[i] && actions[i].nodeType || '').toLowerCase();
            if (nodeType === 'util.logger' || nodeType === 'queue.consume' || nodeType === 'queue.publish' || nodeType === 'human.task') return true;
        }
        return false;
    }

    function normalizeStructuredGuideResult(localResult, userText, done, fail, always) {
        if (!localResult || !localResult.plan || !planHasFix84C1CoveredNode(localResult.plan)) {
            if (done) done(localResult);
            if (always) always();
            return;
        }

        callAiPlanNormalizer(localResult.plan, userText || '', function (res) {
            if (!res || !res.ok || !res.plan) {
                if (fail) fail(new Error((res && res.error) || 'No se pudo normalizar el plan por FIX84C2A.'));
                return;
            }
            if (done) done(finalizeStructuredGuideResult(res.plan, userText || '', res));
        }, fail, always);
    }

    function verificarFrase() {
        resetGuidedPlanConfirmation();
        var userText = getCurrentAiPhrase();
        if (!userText) {
            setStatus('Escribí primero una frase para verificar.', 'warn');
            return;
        }

        var structuredResult = buildStructuredGuideResult(userText);
        if (structuredResult) {
            setStatus('Verificando el plan con la capa común FIX84C2A...', 'busy');
            var structuredVerifyBtn = $('wfAiVerify');
            if (structuredVerifyBtn) structuredVerifyBtn.disabled = true;
            normalizeStructuredGuideResult(structuredResult, userText, function (resolved) {
                renderPhraseVerification(resolved, userText);
                setStatus('Verificación generada. No se aplicó nada al canvas.', 'ok');
            }, function (err) {
                renderPhraseVerification({ ok: false, error: err.message || String(err) }, userText);
                setStatus('No se pudo verificar el plan por FIX84C2A.', 'error');
            }, function () {
                if (structuredVerifyBtn) structuredVerifyBtn.disabled = false;
            });
            return;
        }

        setStatus('Verificando frase con ML.NET local...', 'busy');
        var btn = $('wfAiVerify');
        if (btn) btn.disabled = true;

        callAiAssistant(userText, function (res) {
            renderPhraseVerification(res, userText);
            if (res && res.ok) setStatus('Verificación generada. No se aplicó nada al canvas.', 'ok');
            else setStatus('La frase no pasó la verificación.', 'error');
        }, function (err) {
            renderPhraseVerification({ ok: false, error: err.message || String(err) }, userText);
            setStatus('Error verificando la frase.', 'error');
        }, function () {
            if (btn) btn.disabled = false;
        });
    }

    function canApplyPlan(plan, validation, missing) {
        if (!plan || !plan.actions || !plan.actions.length) return false;
        if (missing && missing.length) return false;
        if (validation && validation.errors && validation.errors.length) return false;
        if (validateFix74PlanContract(plan, getCurrentAiPhrase(), true).length) return false;
        return !!(window.__WF_UI && typeof window.__WF_UI.applyAiPlan === 'function');
    }

    function applyLastPlan() {
        if (!lastPlan) {
            setStatus('No hay una propuesta para aplicar.', 'warn');
            return;
        }
        if (!window.__WF_UI || typeof window.__WF_UI.applyAiPlan !== 'function') {
            setStatus('No está disponible la API del canvas para aplicar la propuesta.', 'error');
            return;
        }
        if (lastAssistantResult && lastAssistantResult.requiresFinalConfirmation && !guidedPlanIsConfirmed(lastPlan)) {
            setStatus('El plan cambió o todavía no fue confirmado. Revisalo y confirmalo antes de aplicar.', 'warn');
            renderResult(lastAssistantResult);
            return;
        }
        var lastValidationErrors = lastAssistantResult && lastAssistantResult.validation && lastAssistantResult.validation.errors || [];
        var preflightErrors = validateFix74PlanContract(lastPlan, getCurrentAiPhrase(), true);
        preflightErrors.forEach(function (e) { pushUnique(lastValidationErrors, e); });
        if (lastValidationErrors.length) {
            setStatus('La propuesta no se aplicó porque el contrato detectó una diferencia entre decisiones, borrador o grafo.', 'error');
            renderResult(lastAssistantResult);
            return;
        }

        var result = window.__WF_UI.applyAiPlan(lastPlan, {});
        if (result && result.ok) {
            var canvasErrors = fix74CanvasContractErrors(lastPlan);
            if (canvasErrors.length) {
                setStatus(canvasErrors[0], 'error');
                return;
            }
            aiAppliedDecisionSummary = buildAppliedDecisionSummary(lastPlan);
            if (aiAppliedDecisionSummary) aiAppliedDecisionSummary.contractFingerprint = lastPlan.contract && lastPlan.contract.fingerprint || '';
            hideAssistantAfterApply(result.message || 'Propuesta aplicada al canvas y validada contra el contrato.');
        } else if (result && result.cancelled) {
            setStatus(result.message || 'Aplicación cancelada.', 'warn');
        } else {
            setStatus((result && result.message) || 'No se pudo aplicar la propuesta al canvas.', 'error');
        }
    }


    // ------------------------------------------------------------
    // fix71e: confirmación explícita antes de aplicar un plan guiado
    // ------------------------------------------------------------
    function notifyDestinationText(step) {
        step = step || {};
        if (step.destType === 'usuario' || step.user) return step.user || 'usuario';
        return step.role || 'rol';
    }

    function guidedStepHumanText(step) {
        step = step || {};
        if (step.type === 'doc_load') return 'Cargar documento: ' + docPhrase(step.docTipo || '');
        if (step.type === 'condition') return 'Validar condición: ' + conditionInfo(step).text;
        if (step.type === 'notify') {
            var level = normalizeKey(step.level || step.nivel || 'info') === 'WARN' ? 'advertencia' : 'informativa';
            return 'Notificar a ' + notifyDestinationText(step) + ' (' + level + '): ' + (step.title || 'Aviso interno');
        }
        if (step.type === 'human_task') return 'Crear tarea humana para ' + taskDestinationForPlan(step) + ': ' + (step.title || step.purpose || humanTaskTitleForPlan(step));
        if (step.type === 'logger') return 'Registrar log ' + (step.level || 'Info') + ': ' + (step.message || '');
        if (step.type === 'end') return 'Finalizar flujo';
        return createStepTitle(step);
    }

    function guidedResolutionReviewHtml(plan, validation) {
        var contract = plan && plan.contract;
        var review = fix74ReviewModel(contract || { nodes: [], edges: [] });
        var errors = (validation && validation.errors) || [];
        var disabled = errors.length ? ' disabled' : '';

        var html = '';
        html += '<div class="wf-ai-guided-review wf-ai-guided-visual-ready' + (errors.length ? ' contract-invalid' : '') + '">';
        html += '  <div class="wf-ai-guided-review-head">';
        html += '    <div><div class="wf-ai-guided-review-kicker">Propuesta visual completa</div><div class="wf-ai-guided-review-title">Revisá el flujo directamente en el canvas</div></div>';
        html += '    <span class="wf-ai-guided-review-badge">Borrador</span>';
        html += '  </div>';
        html += fix74ContractStatusHtml(plan, validation);
        html += '  <div class="wf-ai-guided-review-text">El borrador, el resumen y el workflow real se generan desde el mismo contrato. Si algo no coincide, Crear workflow queda bloqueado.</div>';
        html += '  <details class="wf-ai-guided-summary"><summary>Ver resumen textual</summary>';

        if (review.main.length) {
            html += '  <div class="wf-ai-guided-flow"><div class="wf-ai-guided-flow-title">Flujo principal</div><ol>';
            review.main.forEach(function (n) { var note = fix74bHumanNodeDataText(n, 'main', contract); html += '<li><div>' + htmlEncode(fix74HumanNodeText(n)) + '</div>' + (note ? '<div class="wf-ai-contract-data-note">' + htmlEncode(note) + '</div>' : '') + '</li>'; });
            html += '</ol></div>';
        }
        if (review.condition) html += '  <div class="wf-ai-guided-flow"><div class="wf-ai-guided-flow-title">Condición</div><div>' + htmlEncode(fix74HumanNodeText(review.condition)) + '</div></div>';

        if (review.condition) {
            html += '  <div class="wf-ai-guided-branches">';
            html += '    <div class="wf-ai-guided-branch"><div class="wf-ai-guided-flow-title">SI / cumple</div><ol>';
            review.truePath.forEach(function (n) { var note = fix74bHumanNodeDataText(n, 'true', contract); html += '<li><div>' + htmlEncode(fix74HumanNodeText(n)) + '</div>' + (note ? '<div class="wf-ai-contract-data-note">' + htmlEncode(note) + '</div>' : '') + '</li>'; });
            html += '    </ol></div>';
            html += '    <div class="wf-ai-guided-branch"><div class="wf-ai-guided-flow-title">NO / no cumple</div><ol>';
            review.falsePath.forEach(function (n) { var note = fix74bHumanNodeDataText(n, 'false', contract); html += '<li><div>' + htmlEncode(fix74HumanNodeText(n)) + '</div>' + (note ? '<div class="wf-ai-contract-data-note">' + htmlEncode(note) + '</div>' : '') + '</li>'; });
            html += '    </ol></div>';
            html += '  </div>';
        }
        html += '  </details>';
        html += '  <div class="wf-ai-guided-review-actions">';
        html += '    <button type="button" class="btn wf-ai-create-workflow" id="wfAiCreateGuidedWorkflow"' + disabled + '>Crear workflow</button>';
        html += '    <button type="button" class="btn" data-wf-ai-open-guide="1">Cambiarlo paso a paso</button>';
        html += '  </div>';
        html += '</div>';
        return html;
    }

    function renderResult(res) {
        var out = $('wfAiResult');
        if (!out) return;
        lastAssistantResult = res || null;

        if (!res || !res.ok) {
            clearVisualDraft();
            lastPlan = null;
            var msg = (res && (res.messageToUser || res.error)) || 'Error del Asistente IA.';
            var detail = res && res.error ? res.error : '';
            var htmlErr = '<div class="wf-ai-error">' + htmlEncode(msg) + '</div>';
            if (detail && detail !== msg) {
                htmlErr += '<details class="wf-ai-json"><summary>Error técnico</summary><pre>' + htmlEncode(detail) + '</pre></details>';
            }
            if (res && (res.provider || res.model)) {
                htmlErr += '<div class="wf-ai-meta">Proveedor: ' + htmlEncode(res.provider || '') + ' · Modelo: ' + htmlEncode(res.model || '') + '</div>';
            }
            out.innerHTML = htmlErr;
            return;
        }

        var plan = res.plan || {};
        lastPlan = plan;
        var validation = res.validation || {};
        validation.errors = validation.errors || [];
        validation.warnings = validation.warnings || [];
        var actions = plan.actions || [];
        var missing = plan.missingData || [];
        captureFix84bState(res, getCurrentAiPhrase());
        var detailBlocking = fix84bBlockingClarifications(res);
        if (detailBlocking.length) pushUnique(validation.errors, 'Hay ' + detailBlocking.length + ' aclaración(es) pendientes de FIX84B. Volvé a Verificar frase para resolverlas.');
        if (fix84bHasErrors(res)) pushUnique(validation.errors, 'La última respuesta de aclaración necesita corrección.');

        // fix74 también protege planes provenientes del proveedor ML.NET/legacy.
        if (actions.length) {
            if (!plan.contract) plan.contract = buildFix74PlanContract(plan, getCurrentAiPhrase());
            var renderContractErrors = validateFix74PlanContract(plan, getCurrentAiPhrase(), true);
            renderContractErrors.forEach(function (e) { pushUnique(validation.errors, e); });
            var renderSelfTest = runFix74ContractSelfTest();
            if (!renderSelfTest.ok) pushUnique(validation.errors, 'La matriz interna del contrato fix74b no pasó completa.');
            validation.contract = {
                ok: renderContractErrors.length === 0 && renderSelfTest.ok,
                fingerprint: plan.contract && plan.contract.fingerprint || '',
                graphFingerprint: plan.contract && plan.contract.graphFingerprint || '',
                matrixPassed: renderSelfTest.passed,
                matrixTotal: renderSelfTest.total
            };
            validation.ok = validation.errors.length === 0;
        }
        var warningGroups = collectPhraseWarnings(res, validation, plan);
        var warnings = warningGroups.functional || [];
        var technicalWarnings = warningGroups.technical || [];
        var requiresGuidedConfirmation = !!res.requiresFinalConfirmation;
        var guidedConfirmed = requiresGuidedConfirmation && guidedPlanIsConfirmed(plan);

        var html = '';
        html += '<div class="wf-ai-message">' + htmlEncode(res.messageToUser || plan.messageToUser || '') + '</div>';
        html += '<div class="wf-ai-meta">Modelo: ' + htmlEncode(res.model || '') + ' · Validación: ' + (validation.ok ? 'OK' : 'con errores') + '</div>';

        if (requiresGuidedConfirmation) {
            html += guidedResolutionReviewHtml(plan, validation);
        } else {
            if (actions.length) {
                html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Acciones propuestas</div><ol>';
                actions.forEach(function (a) {
                    html += '<li><strong>' + htmlEncode(a.action || '') + '</strong>';
                    if (a.nodeType) html += ' · ' + htmlEncode(a.nodeType);
                    if (a.label) html += ' · ' + htmlEncode(a.label);
                    html += '</li>';
                });
                html += '</ol></div>';
            }

            if (plan.branchPlan && plan.branchPlan.branches && plan.branchPlan.branches.length) {
                html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Plan de ramas</div><ul>';
                plan.branchPlan.branches.forEach(function (b) {
                    html += '<li><strong>' + htmlEncode(b.condition || '') + '</strong>';
                    html += '<br>SI: ' + htmlEncode(b.truePath || '');
                    html += '<br>NO: ' + htmlEncode(b.falsePath || '');
                    html += '</li>';
                });
                html += '</ul></div>';
            }

            if (plan.proposedConnections && plan.proposedConnections.length) {
                html += '<div class="wf-ai-block"><div class="wf-ai-block-title">Conexiones propuestas</div><ol>';
                plan.proposedConnections.forEach(function (c) {
                    var cond = c.condition ? ' [' + c.condition + ']' : '';
                    html += '<li>' + htmlEncode(c.from || '') + htmlEncode(cond) + ' → ' + htmlEncode(c.to || '') + '</li>';
                });
                html += '</ol></div>';
            }

            html += renderFallbackHelp(res, getCurrentAiPhrase());
        }

        if (actions.length && !requiresGuidedConfirmation) {
            var canApply = canApplyPlan(plan, validation, missing);
            var disabled = canApply ? '' : ' disabled';
            var guidedCss = requiresGuidedConfirmation ? ' guided-ready' : '';
            html += '<div class="wf-ai-apply-zone' + guidedCss + '">';
            html += '<button type="button" class="btn wf-ai-create-workflow" id="wfAiApply"' + disabled + '>Crear workflow</button>';
            html += '<div class="wf-ai-meta">' + htmlEncode(canApply ? 'Convierte exactamente el borrador visible en el workflow real.' : 'La propuesta todavía tiene errores o datos faltantes.') + '</div>';
            html += '</div>';
        }

        if (!requiresGuidedConfirmation) {
            var resultFallbackOwnsErrors = shouldLetFallbackOwnErrors(res, getCurrentAiPhrase());
            if (!resultFallbackOwnsErrors) {
                html += renderList('Datos faltantes', missing, '');
                html += renderList('Errores de validación', validation.errors || [], 'wf-ai-error-list');
                html += renderList('Advertencias funcionales', warnings, 'wf-ai-warning-list');
            } else if (warnings.length) {
                html += renderList('Advertencias funcionales', warnings, 'wf-ai-warning-list');
            }
        }
        if (technicalWarnings.length) {
            html += '<details class="wf-ai-tech-warnings"><summary>Advertencias técnicas ocultas (' + technicalWarnings.length + ')</summary>';
            html += renderList('Solo diagnóstico técnico', technicalWarnings, 'wf-ai-tech-warning-list');
            html += '</details>';
        }
        html += '<details class="wf-ai-json"><summary>Ver JSON técnico</summary><pre>' + htmlEncode(JSON.stringify(plan, null, 2)) + '</pre></details>';

        out.innerHTML = html;
        bindFallbackActions(out);

        var applyBtn = $('wfAiApply');
        if (applyBtn) applyBtn.addEventListener('click', applyLastPlan);

        var createGuidedBtn = $('wfAiCreateGuidedWorkflow');
        if (createGuidedBtn) createGuidedBtn.addEventListener('click', function () {
            if (!confirmGuidedPlan(lastPlan)) {
                setStatus('No se pudo crear porque el plan visual ya no está disponible.', 'error');
                return;
            }
            applyLastPlan();
        });

        renderVisualDraft(res, getCurrentAiPhrase(), { questions: [], complete: true });
    }

    // ------------------------------------------------------------
    // fix25/fix26: propuesta estructurada y validación funcional del constructor
    // ------------------------------------------------------------
    function isBranchChildStep(step) {
        if (!step) return false;
        return step.branch === 'if_cond_true'
            || step.branch === 'if_cond_false'
            || step.branch === 'if_task_ok'
            || step.branch === 'if_task_reject';
    }

    function makeUniqueLabelFactory() {
        var used = {};
        return function (base) {
            base = String(base || 'Paso').trim() || 'Paso';
            var key = normalizeKey(base);
            if (!used[key]) {
                used[key] = 1;
                return base;
            }
            used[key]++;
            return base + ' ' + used[key];
        };
    }

    function makePlanAction(nodeType, label, params) {
        return {
            action: 'ADD_NODE',
            nodeType: nodeType,
            label: label,
            params: params || {}
        };
    }

    function docLoadLabelForPlan(docTipo) {
        var doc = normalizeKey(docTipo);
        if (doc === 'NOTA_CREDITO_ELECTRONICA_AR') return 'Cargar nota de crédito';
        if (doc === 'FACTURA_ELECTRONICA_AR') return 'Cargar factura';
        return 'Cargar documento';
    }

    function conditionLabelForPlan(step) {
        step = step || {};
        var field = normalizePhraseForSearch(step.fieldPath || '');
        var op = String(step.operator || 'not_empty').trim();
        if (field.indexOf('notaCredito.cae'.toLowerCase()) >= 0 && op === 'not_empty') return 'Validar CAE informado';
        return 'Condición: ' + conditionInfo(step).text;
    }

    function humanTaskTitleForPlan(step) {
        step = step || {};
        if (step.destType === 'usuario' || step.user) return 'Enviar a ' + (step.user || 'usuario');
        var role = step.role || '';
        if (normalizeKey(role) === 'DIR_GENERAL') return 'Aprobación Dirección';
        if (normalizeKey(role) === 'ADM_FIN') return 'Enviar a Administración';
        if (normalizeKey(role) === 'COMPRAS') return 'Enviar a Compras';
        if (normalizeKey(role) === 'OPERACIONES') return 'Enviar a Operaciones';
        if (normalizeKey(role) === 'IT') return 'Enviar a IT';
        return 'Enviar a ' + role;
    }

    function taskDestinationForPlan(step) {
        step = step || {};
        if (step.destType === 'usuario' || step.user) return step.user || 'usuario';
        return step.role || 'rol';
    }

    function normalizePlanOperator(op, valueBox) {
        var o = String(op || 'not_empty').trim();
        if (o === '=') return '==';
        if (o === 'true') {
            valueBox.value = 'true';
            return '==';
        }
        if (o === 'false') {
            valueBox.value = 'false';
            return '==';
        }
        return o;
    }

    function actionForGuideStep(step, labelFor) {
        if (!step) return null;

        if (step.type === 'doc_load') {
            return makePlanAction('doc.load', labelFor(docLoadLabelForPlan(step.docTipo)), {
                docTipoCodigo: step.docTipo || '',
                path: step.path || '${input.filePath}',
                mode: 'auto'
            });
        }

        if (step.type === 'condition') {
            var compoundRules = conditionRuleList(step);
            if (compoundRules.length) {
                var planRules = compoundRules.map(function (r) {
                    var valueBoxRule = { value: r.value || '' };
                    var ruleOp = normalizePlanOperator(r.operator || 'not_empty', valueBoxRule);
                    var item = {
                        field: r.fieldPath || defaultConditionField(),
                        op: ruleOp
                    };
                    if (operatorNeedsValue(ruleOp)) item.value = valueBoxRule.value || '';
                    if (r.transform) item.transform = r.transform;
                    return item;
                });
                return makePlanAction('control.if', labelFor(conditionLabelForPlan(step)), {
                    rulesMode: step.rulesMode || 'all',
                    rules: planRules
                });
            }

            var valueBox = { value: step.value || '' };
            var op = normalizePlanOperator(step.operator || 'not_empty', valueBox);
            var p = {
                field: step.fieldPath || defaultConditionField(),
                op: op
            };
            if (operatorNeedsValue(op)) p.value = valueBox.value || '';
            return makePlanAction('control.if', labelFor(conditionLabelForPlan(step)), p);
        }

        if (step.type === 'human_task') {
            var title = String(step.title || '').trim();
            var label = title || humanTaskTitleForPlan(step);
            var hp = {};
            if (step.destType === 'usuario' || step.user) hp.usuarioAsignado = step.user || '';
            else hp.rol = step.role || '';
            if (title) hp.titulo = title;
            if (String(step.description || '').trim()) hp.descripcion = String(step.description).trim();
            if ((parseInt(step.deadlineMinutes || 0, 10) || 0) > 0) hp.deadlineMinutes = parseInt(step.deadlineMinutes, 10);
            if (String(step.pendingState || '').trim()) hp.estadoNegocioPendiente = String(step.pendingState).trim();
            return makePlanAction('human.task', labelFor(label), hp);
        }

        if (step.type === 'email_send') {
            return makePlanAction('email.send', labelFor('Enviar correo'), {
                to: [step.to || 'destinatario@empresa.com'],
                subject: step.subject || 'Aviso Workflow Studio',
                body: step.body || 'Se generó un aviso desde Workflow Studio',
                modo: 'real',
                useWebConfig: true,
                isHtml: false
            });
        }

        if (step.type === 'notify') {
            var destinoTipo = step.destType === 'usuario' ? 'usuario' : 'rol';
            var usuarioDestino = destinoTipo === 'usuario' ? (step.user || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD') : '';
            var rolDestino = destinoTipo === 'rol' ? (step.role || firstRole(['COMPRAS'])) : '';
            var destinoLabel = usuarioDestino || rolDestino || 'destino';
            var notifyLabel = destinoLabel ? ('Notificar a ' + destinoLabel) : 'Notificar';
            return makePlanAction('util.notify', labelFor(notifyLabel), {
                tipo: 'sistema',
                canal: 'sistema',
                nivel: step.level || step.nivel || 'info',
                destinoTipo: destinoTipo,
                usuarioDestino: usuarioDestino,
                rolDestino: rolDestino,
                destino: usuarioDestino || rolDestino,
                prioridad: 'normal',
                asunto: step.title || 'Aviso interno',
                mensaje: step.message || 'Hay una novedad pendiente en el workflow'
            });
        }

        if (step.type === 'http_request') {
            var timeoutMs = parseInt(step.timeoutMs || '10000', 10);
            var failMin = parseInt(step.failStatusMin || '400', 10);
            var bodyText = String(step.body || '').trim();
            return makePlanAction('http.request', labelFor('Solicitud HTTP'), {
                method: (step.method || 'GET').toUpperCase(),
                url: step.url || '/Api/Ping.ashx',
                headers: {},
                query: {},
                body: bodyText ? bodyText : null,
                contentType: step.contentType || 'application/json',
                timeoutMs: isNaN(timeoutMs) || timeoutMs <= 0 ? 10000 : timeoutMs,
                failOnStatus: true,
                failStatusMin: isNaN(failMin) || failMin < 100 ? 400 : failMin
            });
        }

        if (step.type === 'sql_query') {
            var sqlParams = parseJsonObjectOrEmpty(step.paramsJson || '');
            return makePlanAction('data.sql', labelFor('Consulta SQL'), {
                connectionStringName: step.connectionStringName || 'DefaultConnection',
                query: step.query || 'SELECT 1',
                parameters: sqlParams || {},
                maxRows: parseInt(step.maxRows || '100', 10) || 100
            });
        }

        if (step.type === 'file_read') {
            return makePlanAction('file.read', labelFor('Archivo: Leer'), {
                path: step.path || '',
                salida: step.output || 'archivo',
                output: step.output || 'archivo',
                asJson: !!step.asJson,
                encoding: step.encoding || 'utf-8',
                zipMode: step.zipMode || 'auto',
                zipEntry: step.zipEntry || '',
                useCache: true
            });
        }

        if (step.type === 'file_write') {
            var params = {
                path: step.path || '',
                encoding: step.encoding || 'utf-8',
                overwrite: step.overwrite !== false,
                zipMode: step.zipMode || 'none'
            };
            if (step.entryName) params.entryName = step.entryName;
            if (step.sourceMode === 'context') params.origen = step.origen || 'archivo';
            else params.content = step.content || '';
            return makePlanAction('file.write', labelFor('Archivo: Escribir'), params);
        }

        if (step.type === 'queue_publish') {
            var payloadValue = queuePayloadValueFromStep(step);
            var publishParams = {
                queue: step.queue || '',
                payload: payloadValue
            };
            if (step.broker && normalizeKey(step.broker) !== 'SQL') publishParams.broker = step.broker;
            if (step.connectionStringName && step.connectionStringName !== 'DefaultConnection') publishParams.connectionStringName = step.connectionStringName;
            return makePlanAction('queue.publish', labelFor('Publicar en cola ' + (step.queue || '')), publishParams);
        }

        if (step.type === 'queue_consume') {
            var queueParams = {
                queue: step.queue || '',
                take: step.take || ''
            };
            if (step.debug) queueParams.debug = true;
            if (step.broker && normalizeKey(step.broker) !== 'SQL') queueParams.broker = step.broker;
            if (step.connectionStringName && step.connectionStringName !== 'DefaultConnection') queueParams.connectionStringName = step.connectionStringName;
            return makePlanAction('queue.consume', labelFor('Consumir cola ' + (step.queue || '')), queueParams);
        }

        if (step.type === 'subflow') {
            var input = parseJsonObjectOrEmpty(step.inputJson || '{}') || {};
            var maxDepth = parseInt(step.maxDepth || '10', 10);
            var sp = {
                ref: step.ref || '',
                input: input,
                maxDepth: isNaN(maxDepth) || maxDepth < 1 ? 10 : Math.min(maxDepth, 50)
            };
            if (step.alias) sp.as = step.alias;
            return makePlanAction('util.subflow', labelFor('Ejecutar workflow'), sp);
        }

        if (step.type === 'state_set') {
            var set = stateSetObjectFromStep(step);
            return makePlanAction('state.vars', labelFor('Guardar variable'), { set: set });
        }

        if (step.type === 'state_remove') {
            return makePlanAction('state.vars', labelFor('Quitar variable'), { remove: [step.key || ''] });
        }

        if (step.type === 'delay') {
            var ms = parseInt(step.ms || '1000', 10);
            return makePlanAction('control.delay', labelFor('Demora'), {
                message: 'Demora agregada por Constructor IA',
                ms: isNaN(ms) || ms <= 0 ? 1000 : ms
            });
        }

        if (step.type === 'retry') {
            var reintentos = parseInt(step.reintentos || '3', 10);
            var backoffMs = parseInt(step.backoffMs || '500', 10);
            return makePlanAction('control.retry', labelFor('Reintentar'), {
                reintentos: isNaN(reintentos) || reintentos < 0 ? 3 : Math.min(reintentos, 50),
                backoffMs: isNaN(backoffMs) || backoffMs < 0 ? 500 : Math.min(backoffMs, 600000),
                message: step.message || 'Reintento agregado por Constructor IA'
            });
        }

        if (step.type === 'error_handler') {
            var cap = step.capture !== false;
            var retry = !!step.retry;
            return makePlanAction('util.error', labelFor('Manejador de Error'), {
                mensaje: step.message || 'Error controlado por el workflow',
                capturar: cap,
                capturarErrores: cap,
                notificar: !!step.notify,
                volverAIntentar: retry,
                reintentar: retry
            });
        }

        if (step.type === 'logger') {
            var loggerParams = { message: step.message || '' };
            if (step.level && step.level !== 'Info') loggerParams.level = step.level;
            return makePlanAction('util.logger', labelFor('Registrar evento'), loggerParams);
        }

        if (step.type === 'end') {
            return makePlanAction('util.end', labelFor('Fin'), {});
        }

        return null;
    }

    function taskResultIfActionForPlan(taskStep, labelFor) {
        var destino = taskDestinationForPlan(taskStep);
        return makePlanAction('control.if', labelFor('Resultado de ' + destino + ' aprobado'), {
            field: 'wf.tarea.resultado',
            op: '==',
            value: 'apto'
        });
    }

    function actionNodeTypeForPlan(action) {
        return String(action && action.nodeType || '').trim();
    }

    function actionLabelForPlan(action) {
        return String(action && action.label || '').trim();
    }

    function addStructuredConnection(list, fromAction, toAction, condition) {
        if (!list || !fromAction || !toAction) return;
        if (fromAction === toAction) return;

        var from = actionLabelForPlan(fromAction);
        var to = actionLabelForPlan(toAction);
        var fromType = actionNodeTypeForPlan(fromAction);
        var toType = actionNodeTypeForPlan(toAction);
        var cond = String(condition || '').trim();
        if (!from || !to) return;

        for (var i = 0; i < list.length; i++) {
            var x = list[i];
            if (x.from === from && x.to === to && String(x.condition || '') === cond) return;
        }

        var item = {
            action: 'CONNECT_NODES',
            from: from,
            to: to,
            fromNodeType: fromType,
            toNodeType: toType
        };
        if (cond) item.condition = cond;
        list.push(item);
    }

    function buildStructuredBranchMaps() {
        var conditionMap = {};
        var taskMap = {};

        guideSteps.forEach(function (step, idx) {
            if (!step) return;
            if (step.branch === 'if_cond_true' || step.branch === 'if_cond_false') {
                var ownerIndex = findBranchOwnerIndex(step, idx, 'condition');
                if (ownerIndex >= 0) {
                    if (!conditionMap[ownerIndex]) conditionMap[ownerIndex] = { si: [], no: [] };
                    if (step.branch === 'if_cond_true') conditionMap[ownerIndex].si.push(idx);
                    else conditionMap[ownerIndex].no.push(idx);
                }
            }

            if (step.branch === 'if_task_ok' || step.branch === 'if_task_reject') {
                var taskOwnerIndex = findBranchOwnerIndex(step, idx, 'human_task');
                if (taskOwnerIndex >= 0) {
                    if (!taskMap[taskOwnerIndex]) taskMap[taskOwnerIndex] = { ok: [], reject: [] };
                    if (step.branch === 'if_task_ok') taskMap[taskOwnerIndex].ok.push(idx);
                    else taskMap[taskOwnerIndex].reject.push(idx);
                }
            }
        });

        return { conditions: conditionMap, tasks: taskMap };
    }

    function branchFirstAction(indexes, actionByStepId) {
        indexes = indexes || [];
        for (var i = 0; i < indexes.length; i++) {
            var st = guideSteps[indexes[i]];
            if (st && actionByStepId[st.id]) return actionByStepId[st.id];
        }
        return null;
    }

    function branchLastAction(indexes, actionByStepId) {
        indexes = indexes || [];
        for (var i = indexes.length - 1; i >= 0; i--) {
            var st = guideSteps[indexes[i]];
            if (st && actionByStepId[st.id]) return actionByStepId[st.id];
        }
        return null;
    }

    function connectBranchSteps(connections, fromAction, indexes, edgeCondition, mergeAction, actionByStepId) {
        indexes = indexes || [];
        var first = branchFirstAction(indexes, actionByStepId);
        if (!first) {
            if (mergeAction) addStructuredConnection(connections, fromAction, mergeAction, edgeCondition);
            return;
        }

        addStructuredConnection(connections, fromAction, first, edgeCondition);
        for (var i = 0; i < indexes.length - 1; i++) {
            var a = actionByStepId[guideSteps[indexes[i]].id];
            var b = actionByStepId[guideSteps[indexes[i + 1]].id];
            addStructuredConnection(connections, a, b, '');
        }

        var last = branchLastAction(indexes, actionByStepId);
        if (last && mergeAction && actionNodeTypeForPlan(last) !== 'util.end') {
            addStructuredConnection(connections, last, mergeAction, '');
        }
    }

    function buildStructuredGuidePlan() {
        if (!guideSteps.length) return null;

        var maps = buildStructuredBranchMaps();
        var labelFor = makeUniqueLabelFactory();
        var actions = [];
        var actionByStepId = {};
        var taskResultIfByStepId = {};

        var startAction = makePlanAction('util.start', labelFor('Inicio'), {});
        actions.push(startAction);

        function mapForConditionIndex(idx) {
            return maps.conditions[idx] || { si: [], no: [] };
        }

        function mapForTaskIndex(idx) {
            return maps.tasks[idx] || { ok: [], reject: [] };
        }

        function hasConditionBranches(idx) {
            var m = mapForConditionIndex(idx);
            return !!(m.si.length || m.no.length);
        }

        function hasTaskBranches(idx) {
            var m = mapForTaskIndex(idx);
            return !!(m.ok.length || m.reject.length);
        }

        function ensureActionForIndex(idx) {
            var step = guideSteps[idx];
            if (!step) return null;

            // FIX84C2Bg2c: un Fin dentro de APTO/NO APTO que desemboca inmediatamente
            // en el Fin común no crea un segundo nodo técnico. La rama conecta al mismo Fin.
            if (isRedundantBranchEndStep(step, idx)) return null;
            if (actionByStepId[step.id]) return actionByStepId[step.id];

            var action = actionForGuideStep(step, labelFor);
            if (action) {
                actions.push(action);
                actionByStepId[step.id] = action;
            }
            return action || null;
        }

        function ensureTaskResultIfForIndex(idx) {
            var step = guideSteps[idx];
            if (!step || step.type !== 'human_task' || !hasTaskBranches(idx)) return null;
            if (taskResultIfByStepId[step.id]) return taskResultIfByStepId[step.id];

            var resultIf = taskResultIfActionForPlan(step, labelFor);
            actions.push(resultIf);
            taskResultIfByStepId[step.id] = resultIf;
            return resultIf;
        }

        function childIndexesForIndex(idx) {
            var step = guideSteps[idx];
            if (!step) return [];
            if (step.type === 'condition') {
                var cm = mapForConditionIndex(idx);
                return cm.si.concat(cm.no);
            }
            if (step.type === 'human_task') {
                var tm = mapForTaskIndex(idx);
                return tm.ok.concat(tm.reject);
            }
            return [];
        }

        function collectActionsRecursive(idx, path) {
            path = path || {};
            if (idx == null || idx < 0 || idx >= guideSteps.length) return;
            if (path[idx]) return;

            var nextPath = {};
            Object.keys(path).forEach(function (k) { nextPath[k] = path[k]; });
            nextPath[idx] = true;

            ensureActionForIndex(idx);
            ensureTaskResultIfForIndex(idx);

            childIndexesForIndex(idx).forEach(function (childIdx) {
                collectActionsRecursive(childIdx, nextPath);
            });
        }

        var mainIndexes = [];
        guideSteps.forEach(function (step, idx) {
            if (!step || isBranchChildStep(step)) return;
            mainIndexes.push(idx);
        });

        mainIndexes.forEach(function (idx) {
            collectActionsRecursive(idx, {});
        });

        var endAction = null;
        actions.forEach(function (a) {
            if (!endAction && actionNodeTypeForPlan(a) === 'util.end') endAction = a;
        });
        if (!endAction) {
            endAction = makePlanAction('util.end', labelFor('Fin'), {});
            actions.push(endAction);
        }

        function firstActionForIndexes(indexes) {
            indexes = indexes || [];
            for (var i = 0; i < indexes.length; i++) {
                var st = guideSteps[indexes[i]];
                if (st && actionByStepId[st.id]) return actionByStepId[st.id];
            }
            return null;
        }

        function nextActionAfter(indexes, currentPos, mergeAction) {
            for (var i = currentPos + 1; i < indexes.length; i++) {
                var st = guideSteps[indexes[i]];
                if (st && actionByStepId[st.id]) return actionByStepId[st.id];
            }
            return mergeAction || endAction;
        }

        var connections = [];

        function connectSequence(fromAction, indexes, edgeCondition, mergeAction, path) {
            indexes = indexes || [];
            mergeAction = mergeAction || endAction;
            path = path || {};

            var first = firstActionForIndexes(indexes);
            if (!first) {
                if (mergeAction) addStructuredConnection(connections, fromAction, mergeAction, edgeCondition);
                return;
            }

            addStructuredConnection(connections, fromAction, first, edgeCondition);

            for (var pos = 0; pos < indexes.length; pos++) {
                var idx = indexes[pos];
                if (idx == null || idx < 0 || idx >= guideSteps.length) continue;
                if (path[idx]) continue;

                var nextPath = {};
                Object.keys(path).forEach(function (k) { nextPath[k] = path[k]; });
                nextPath[idx] = true;

                var step = guideSteps[idx];
                var current = step && actionByStepId[step.id];
                if (!step || !current) continue;
                if (actionNodeTypeForPlan(current) === 'util.end') continue;

                var nextAction = nextActionAfter(indexes, pos, mergeAction);
                if (nextAction === current) nextAction = mergeAction || endAction;

                if (step.type === 'condition' && hasConditionBranches(idx)) {
                    var cm = mapForConditionIndex(idx);
                    connectSequence(current, cm.si, 'SI', nextAction, nextPath);
                    connectSequence(current, cm.no, 'NO', nextAction, nextPath);
                    continue;
                }

                if (step.type === 'human_task' && hasTaskBranches(idx)) {
                    var resultIf = ensureTaskResultIfForIndex(idx);
                    if (resultIf) {
                        addStructuredConnection(connections, current, resultIf, '');
                        var tm = mapForTaskIndex(idx);
                        connectSequence(resultIf, tm.ok, 'SI', nextAction, nextPath);
                        connectSequence(resultIf, tm.reject, 'NO', nextAction, nextPath);
                        continue;
                    }
                }

                if (nextAction) addStructuredConnection(connections, current, nextAction, '');
            }
        }

        if (mainIndexes.length) {
            connectSequence(startAction, mainIndexes, '', endAction, {});
        } else {
            addStructuredConnection(connections, startAction, endAction, '');
        }

        var branchItems = [];
        Object.keys(maps.tasks).forEach(function (k) {
            var idx = parseInt(k, 10);
            var step = guideSteps[idx];
            var resultIf = step && taskResultIfByStepId[step.id];
            if (!step || !resultIf) return;
            branchItems.push({
                condition: 'Resultado humano de ' + taskDestinationForPlan(step) + ' aprobado',
                fieldKind: 'humanTaskResult',
                truePath: branchFirstAction(maps.tasks[idx].ok, actionByStepId) ? actionLabelForPlan(branchFirstAction(maps.tasks[idx].ok, actionByStepId)) : actionLabelForPlan(endAction),
                falsePath: branchFirstAction(maps.tasks[idx].reject, actionByStepId) ? actionLabelForPlan(branchFirstAction(maps.tasks[idx].reject, actionByStepId)) : actionLabelForPlan(endAction)
            });
        });

        return {
            assistantVersion: 'constructor-structured-fix74',
            intent: 'build_workflow',
            confidence: 1,
            messageToUser: 'Propuesta generada desde el Constructor IA con plan estructurado local.',
            actions: actions,
            missingData: [],
            warnings: hasEndStep()
                ? []
                : ['Se agregó un nodo Fin técnico en la propuesta para evitar un grafo abierto.'],
            branchPlan: {
                planner: 'constructor-local-fix74',
                hasBranches: branchItems.length > 0,
                branches: branchItems
            },
            proposedConnections: connections
        };
    }

    function planActionMatchesRole(action, nodeType, role) {
        if (!action || String(action.nodeType || '') !== nodeType) return false;
        var params = action.params || {};
        var wanted = normalizeKey(role || '');
        if (nodeType === 'human.task') return normalizeKey(params.rol || params.RolDestino || '') === wanted;
        if (nodeType === 'util.notify') return normalizeKey(params.rolDestino || params.destino || '') === wanted;
        if (nodeType === 'util.logger') {
            return normalizeKey((action.label || '') + ' ' + (params.message || '')).indexOf(wanted) >= 0;
        }
        return false;
    }

    function planHasExpectedRoleAction(plan, mode, role) {
        var nodeType = guidedActionNodeType(mode);
        if (!nodeType) return true;
        var actions = (plan && plan.actions) || [];
        for (var i = 0; i < actions.length; i++) {
            if (planActionMatchesRole(actions[i], nodeType, role)) return true;
        }
        return false;
    }

    function guidedDecisionConsistencyErrors(plan, userText) {
        var errors = [];
        var notifyMode = guidedNotifyActionMode(userText);
        var noMode = guidedNoBranchActionMode(userText);
        var data = guidedMessageData(userText);

        if (textHasAny(userText, ['dir_general', 'direccion', 'dirección', 'dir general']) && !planHasExpectedRoleAction(plan, notifyMode, 'DIR_GENERAL')) {
            errors.push('La decisión sobre notificar/avisar no coincide con el nodo generado para DIR_GENERAL.');
        }
        if (textHasAny(userText, ['it']) && !planHasExpectedRoleAction(plan, notifyMode, 'IT')) {
            errors.push('La decisión sobre notificar/avisar no coincide con el nodo generado para IT.');
        }
        if (textHasAny(userText, ['compras']) && noMode !== 'end' && !planHasExpectedRoleAction(plan, noMode, 'COMPRAS')) {
            errors.push('La decisión de la rama NO no coincide con el nodo generado para COMPRAS.');
        }

        var allParams = JSON.stringify(((plan && plan.actions) || []).map(function (a) { return a && a.params || {}; }));
        if (data.cae && allParams.indexOf('{{biz.notaCredito.cae}}') < 0) {
            errors.push('Se eligió incluir CAE, pero el plan no lo incorporó en mensajes o descripciones.');
        }
        if (data.proveedor && allParams.indexOf('{{biz.notaCredito.proveedor}}') < 0) {
            errors.push('Se eligió incluir proveedor, pero el plan no lo incorporó en mensajes o descripciones.');
        }
        return errors;
    }


    // ------------------------------------------------------------
    // fix74: contrato determinístico entre decisiones, plan, borrador y canvas
    // ------------------------------------------------------------
    function fix74NormalizeCondition(value) {
        var v = normalizePhraseForSearch(value || 'always').trim();
        if (!v || v === 'always') return 'always';
        if (v === 'si' || v === 'sí' || v === 'true') return 'true';
        if (v === 'no' || v === 'false') return 'false';
        return v;
    }

    function fix74StableValue(value, keyName) {
        if (value == null) return value;
        if (Array.isArray(value)) return value.map(function (x) { return fix74StableValue(x, ''); });
        if (typeof value === 'object') {
            var out = {};
            Object.keys(value).sort().forEach(function (k) {
                if (k === 'position' || k === 'contract') return;
                out[k] = fix74StableValue(value[k], k);
            });
            return out;
        }
        if (typeof value === 'string') return String(value).trim();
        return value;
    }

    function fix74StableStringify(value) {
        try { return JSON.stringify(fix74StableValue(value, '')); }
        catch (e) { return ''; }
    }

    function fix74Hash(text) {
        text = String(text || '');
        var h = 2166136261;
        for (var i = 0; i < text.length; i++) {
            h ^= text.charCodeAt(i);
            h += (h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24);
        }
        return ('00000000' + (h >>> 0).toString(16)).slice(-8);
    }

    function fix74LabelKey(label) {
        return normalizePhraseForSearch(label || '').replace(/\s+/g, ' ').trim();
    }

    function fix74RoleFromAction(action) {
        action = action || {};
        var p = action.params || {};
        var type = String(action.nodeType || '');
        if (type === 'human.task') return normalizeKey(p.rol || p.RolDestino || p.role || '');
        if (type === 'util.notify') return normalizeKey(p.rolDestino || p.destino || p.role || '');
        if (type === 'util.logger') {
            var txt = normalizeKey((action.label || '') + ' ' + (p.message || ''));
            var roles = ['DIR_GENERAL', 'COMPRAS', 'ADM_FIN', 'OPERACIONES', 'IT'];
            for (var i = 0; i < roles.length; i++) if (txt.indexOf(roles[i]) >= 0) return roles[i];
        }
        return '';
    }

    function fix74DataRefs(value, out) {
        out = out || [];
        if (value == null) return out;
        if (Array.isArray(value)) {
            value.forEach(function (x) { fix74DataRefs(x, out); });
            return out;
        }
        if (typeof value === 'object') {
            Object.keys(value).forEach(function (k) {
                if (k !== 'position' && k !== 'contract') fix74DataRefs(value[k], out);
            });
            return out;
        }
        if (typeof value === 'string') {
            var re = /\{\{\s*([^}]+?)\s*\}\}/g;
            var m;
            while ((m = re.exec(value))) pushUnique(out, String(m[1] || '').trim());
        }
        return out;
    }

    function fix74CanonicalNodesFromPlan(plan) {
        var nodes = [];
        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || String(a.action || '').toUpperCase() !== 'ADD_NODE' || !a.nodeType) return;
            var params = fix74StableValue(a.params || {}, 'params');
            nodes.push({
                key: fix74LabelKey(a.label),
                label: String(a.label || '').trim(),
                nodeType: String(a.nodeType || '').trim(),
                role: fix74RoleFromAction(a),
                params: params,
                dataRefs: fix74DataRefs(params, []).sort()
            });
        });
        nodes.sort(function (a, b) {
            var ak = a.key + '|' + a.nodeType;
            var bk = b.key + '|' + b.nodeType;
            return ak < bk ? -1 : (ak > bk ? 1 : 0);
        });
        return nodes;
    }

    function fix74CanonicalEdgesFromPlan(plan) {
        var edges = [];
        var typeByLabel = {};
        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || String(a.action || '').toUpperCase() !== 'ADD_NODE') return;
            typeByLabel[fix74LabelKey(a.label)] = String(a.nodeType || '').trim();
        });
        ((plan && plan.proposedConnections) || []).forEach(function (c) {
            if (!c) return;
            var fromKey = fix74LabelKey(c.from);
            var toKey = fix74LabelKey(c.to);
            edges.push({
                from: fromKey,
                to: toKey,
                condition: fix74NormalizeCondition(c.condition),
                fromNodeType: String(c.fromNodeType || typeByLabel[fromKey] || '').trim(),
                toNodeType: String(c.toNodeType || typeByLabel[toKey] || '').trim()
            });
        });
        if (!edges.length) {
            var ordered = ((plan && plan.actions) || []).filter(function (a) {
                return a && String(a.action || '').toUpperCase() === 'ADD_NODE' && a.nodeType;
            });
            for (var oi = 0; oi < ordered.length - 1; oi++) {
                edges.push({
                    from: fix74LabelKey(ordered[oi].label),
                    to: fix74LabelKey(ordered[oi + 1].label),
                    condition: 'always',
                    fromNodeType: String(ordered[oi].nodeType || '').trim(),
                    toNodeType: String(ordered[oi + 1].nodeType || '').trim()
                });
            }
        }
        edges.sort(function (a, b) {
            var ak = a.from + '|' + a.condition + '|' + a.to;
            var bk = b.from + '|' + b.condition + '|' + b.to;
            return ak < bk ? -1 : (ak > bk ? 1 : 0);
        });
        return edges;
    }

    function fix74ActiveDecisionSnapshot(items) {
        var source = items || activeFallbackResolvedItems();
        var decisions = [];
        (source || []).forEach(function (x) {
            if (!x || x.superseded) return;
            decisions.push({
                key: String(x.key || '').trim(),
                choice: String(x.label || x.clarification || '').trim()
            });
        });
        decisions.sort(function (a, b) { return a.key < b.key ? -1 : (a.key > b.key ? 1 : 0); });
        return decisions;
    }

    function fix74GraphSnapshotFromPlan(plan) {
        return {
            nodes: fix74CanonicalNodesFromPlan(plan),
            edges: fix74CanonicalEdgesFromPlan(plan)
        };
    }

    function fix74GraphFingerprint(snapshot) {
        return fix74Hash(fix74StableStringify(snapshot || { nodes: [], edges: [] }));
    }

    function buildFix74PlanContract(plan, userText, decisionItems) {
        var graph = fix74GraphSnapshotFromPlan(plan);
        var decisions = fix74ActiveDecisionSnapshot(decisionItems);
        var phraseKey = fallbackBaseKeyFor(userText || '');
        var graphFingerprint = fix74GraphFingerprint(graph);
        var decisionFingerprint = fix74Hash(fix74StableStringify(decisions));
        var contract = {
            version: 'fix74b-v1',
            phraseKey: phraseKey,
            decisions: decisions,
            nodes: graph.nodes,
            edges: graph.edges,
            graphFingerprint: graphFingerprint,
            decisionFingerprint: decisionFingerprint
        };
        contract.textProfile = fix74bBuildTextProfile(contract);
        contract.textFingerprint = fix74Hash(fix74StableStringify(contract.textProfile));
        contract.fingerprint = fix74Hash(graphFingerprint + '|' + decisionFingerprint + '|' + contract.textFingerprint + '|' + phraseKey);
        return contract;
    }

    function fix74NodeByKey(contract, key) {
        var nodes = (contract && contract.nodes) || [];
        for (var i = 0; i < nodes.length; i++) if (nodes[i].key === key) return nodes[i];
        return null;
    }

    function fix74Outgoing(contract, key) {
        return ((contract && contract.edges) || []).filter(function (e) { return e.from === key; });
    }

    // FIX84C2Be: destino humano común para el contrato visual legacy fix74.
    // C2B usa usuarioAsignado (camelCase); conservamos aliases históricos sólo para compatibilidad.
    function fix74HumanTaskUser(params) {
        params = params || {};
        return String(params.usuarioAsignado || params.UsuarioAsignado || params.AsignadoA || params.usuario || '').trim();
    }

    function fix74CoreContractErrors(contract) {
        var errors = [];
        if (!contract || contract.version !== 'fix74b-v1') return ['El plan no contiene un contrato fix74b válido.'];
        var nodes = contract.nodes || [];
        var edges = contract.edges || [];
        var byKey = {};
        var starts = [];
        var ends = [];

        nodes.forEach(function (n) {
            if (!n.key) errors.push('Existe un nodo sin etiqueta identificable.');
            if (byKey[n.key]) errors.push('Hay etiquetas duplicadas: "' + n.label + '". El canvas no puede resolverlas de forma determinística.');
            byKey[n.key] = n;
            if (n.nodeType === 'util.start') starts.push(n);
            if (n.nodeType === 'util.end') ends.push(n);

            var p = n.params || {};
            if (n.nodeType === 'doc.load' && (!p.docTipoCodigo || !p.path)) errors.push('El nodo "' + n.label + '" no tiene tipo documental o ruta.');
            if (n.nodeType === 'control.if' && (!p.field || !p.op)) errors.push('La condición "' + n.label + '" no tiene campo u operador.');
            if (n.nodeType === 'human.task' && !n.role && !fix74HumanTaskUser(p)) errors.push('La tarea "' + n.label + '" no tiene rol ni usuario destino.');
            if (n.nodeType === 'util.notify' && !n.role && !p.usuarioDestino && !p.destino) errors.push('La notificación "' + n.label + '" no tiene destinatario.');
            if (n.nodeType === 'util.logger' && !p.message) errors.push('El logger "' + n.label + '" no tiene mensaje.');
        });

        if (starts.length !== 1) errors.push('El workflow debe tener exactamente un nodo Inicio.');
        if (!ends.length) errors.push('El workflow debe tener al menos un nodo Fin.');

        var edgeSeen = {};
        edges.forEach(function (e) {
            if (!byKey[e.from] || !byKey[e.to]) errors.push('Hay una conexión cuyo origen o destino no existe: ' + e.from + ' → ' + e.to + '.');
            if (e.from === e.to) errors.push('No se permite una conexión de un nodo hacia sí mismo: ' + e.from + '.');
            var ek = e.from + '|' + e.condition + '|' + e.to;
            if (edgeSeen[ek]) errors.push('Hay una conexión duplicada: ' + e.from + ' [' + e.condition + '] → ' + e.to + '.');
            edgeSeen[ek] = true;
        });

        nodes.forEach(function (n) {
            var outgoing = fix74Outgoing(contract, n.key);
            if (n.nodeType === 'util.end' && outgoing.length) errors.push('El nodo Fin "' + n.label + '" no puede tener salidas.');
            if (n.nodeType !== 'util.end' && !outgoing.length) errors.push('El nodo "' + n.label + '" quedó sin salida.');
            if (n.nodeType === 'control.if') {
                var t = outgoing.filter(function (e) { return e.condition === 'true'; }).length;
                var f = outgoing.filter(function (e) { return e.condition === 'false'; }).length;
                if (t !== 1 || f !== 1) errors.push('La condición "' + n.label + '" debe tener exactamente una salida SI y una salida NO.');
            }
        });

        if (starts.length === 1) {
            var reachable = {};
            var queue = [starts[0].key];
            while (queue.length) {
                var k = queue.shift();
                if (reachable[k]) continue;
                reachable[k] = true;
                fix74Outgoing(contract, k).forEach(function (e) { if (!reachable[e.to]) queue.push(e.to); });
            }
            nodes.forEach(function (n) {
                if (!reachable[n.key]) errors.push('El nodo "' + n.label + '" no es alcanzable desde Inicio.');
            });
        }

        if (ends.length) {
            var canReachEnd = {};
            var reverseQueue = ends.map(function (n) { return n.key; });
            while (reverseQueue.length) {
                var endKey = reverseQueue.shift();
                if (canReachEnd[endKey]) continue;
                canReachEnd[endKey] = true;
                edges.forEach(function (e) { if (e.to === endKey && !canReachEnd[e.from]) reverseQueue.push(e.from); });
            }
            nodes.forEach(function (n) {
                if (!canReachEnd[n.key]) errors.push('El nodo "' + n.label + '" no conduce a ningún Fin.');
            });
        }

        return uniqueArray(errors);
    }

    function fix74GraphSnapshotFromVisualModel(model) {
        var nodes = [];
        var edges = [];
        ((model && model.nodes) || []).forEach(function (n) {
            if (!n || n.isIssue || !n.action) return;
            var a = n.action;
            var params = fix74StableValue(a.params || {}, 'params');
            nodes.push({
                key: fix74LabelKey(a.label),
                label: String(a.label || '').trim(),
                nodeType: String(a.nodeType || '').trim(),
                role: fix74RoleFromAction(a),
                params: params,
                dataRefs: fix74DataRefs(params, []).sort()
            });
        });
        ((model && model.edges) || []).forEach(function (e) {
            if (!e || e.issue || !e.from || !e.to) return;
            // El texto visible puede ser más humano que el label técnico (ej. "Registrar advertencia").
            // Para validar identidad usamos siempre el action real asociado al nodo visual.
            var fromAction = e.from.action || {};
            var toAction = e.to.action || {};
            edges.push({
                from: fix74LabelKey(fromAction.label || e.from.label),
                to: fix74LabelKey(toAction.label || e.to.label),
                condition: fix74NormalizeCondition(e.condition),
                fromNodeType: String(fromAction.nodeType || e.from.nodeType || ''),
                toNodeType: String(toAction.nodeType || e.to.nodeType || '')
            });
        });
        nodes.sort(function (a, b) { var ak = a.key + '|' + a.nodeType, bk = b.key + '|' + b.nodeType; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        edges.sort(function (a, b) { var ak = a.from + '|' + a.condition + '|' + a.to, bk = b.from + '|' + b.condition + '|' + b.to; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        return { nodes: nodes, edges: edges };
    }

    // FIX84C2Bg2b: el contrato del canvas sigue siendo técnico y conserva el IF
    // wf.tarea.resultado. El borrador funcional, en cambio, lo oculta y dibuja APTO/NO APTO
    // directamente desde human.task. Para comprobar que el dibujo representa el mismo flujo
    // necesitamos comparar contra una proyección funcional equivalente, no contra la huella
    // técnica que luego recibirá el canvas.
    function fix74FunctionalGraphSnapshotFromPlan(plan) {
        var allNodes = fix74CanonicalNodesFromPlan(plan);
        var allEdges = fix74CanonicalEdgesFromPlan(plan);
        var actionByKey = {};
        var hidden = {};
        var parentByHidden = {};

        ((plan && plan.actions) || []).forEach(function (a) {
            if (!a || String(a.action || '').toUpperCase() !== 'ADD_NODE' || !a.nodeType) return;
            var key = fix74LabelKey(a.label);
            actionByKey[key] = a;
            if (isHumanTaskResultAction(a)) hidden[key] = true;
        });

        allEdges.forEach(function (e) {
            if (!e || !hidden[e.to]) return;
            var parentAction = actionByKey[e.from];
            if (parentAction && String(parentAction.nodeType || '') === 'human.task') parentByHidden[e.to] = e.from;
        });

        var nodes = allNodes.filter(function (n) { return n && !hidden[n.key]; });
        var edges = [];
        allEdges.forEach(function (e) {
            if (!e) return;
            if (hidden[e.to]) return; // human.task -> IF técnico queda colapsado visualmente.

            var copy = {
                from: e.from,
                to: e.to,
                condition: e.condition,
                fromNodeType: e.fromNodeType,
                toNodeType: e.toNodeType
            };

            if (hidden[e.from]) {
                var parentKey = parentByHidden[e.from];
                if (!parentKey) return;
                copy.from = parentKey;
                copy.fromNodeType = String((actionByKey[parentKey] && actionByKey[parentKey].nodeType) || 'human.task');
            }

            // No deberían quedar aristas hacia un nodo técnico oculto.
            if (hidden[copy.from] || hidden[copy.to] || copy.from === copy.to) return;
            edges.push(copy);
        });

        nodes.sort(function (a, b) { var ak = a.key + '|' + a.nodeType, bk = b.key + '|' + b.nodeType; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        edges.sort(function (a, b) { var ak = a.from + '|' + a.condition + '|' + a.to, bk = b.from + '|' + b.condition + '|' + b.to; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        return { nodes: nodes, edges: edges };
    }

    function fix74ProjectedCanvasSnapshot(plan) {
        var actions = ((plan && plan.actions) || []).filter(function (a) {
            return a && String(a.action || '').toUpperCase() === 'ADD_NODE' && a.nodeType;
        });
        var labels = {};
        var nodes = [];
        actions.forEach(function (a) {
            var key = fix74LabelKey(a.label);
            labels[key] = a;
            var params = fix74StableValue(a.params || {}, 'params');
            nodes.push({
                key: key,
                label: String(a.label || '').trim(),
                nodeType: String(a.nodeType || '').trim(),
                role: fix74RoleFromAction(a),
                params: params,
                dataRefs: fix74DataRefs(params, []).sort()
            });
        });
        var edges = [];
        ((plan && plan.proposedConnections) || []).forEach(function (c) {
            if (!c) return;
            var fk = fix74LabelKey(c.from), tk = fix74LabelKey(c.to);
            if (!labels[fk] || !labels[tk] || fk === tk) return;
            edges.push({
                from: fk,
                to: tk,
                condition: fix74NormalizeCondition(c.condition),
                fromNodeType: String(c.fromNodeType || labels[fk].nodeType || ''),
                toNodeType: String(c.toNodeType || labels[tk].nodeType || '')
            });
        });
        if (!edges.length) {
            for (var i = 0; i < nodes.length - 1; i++) {
                edges.push({ from: nodes[i].key, to: nodes[i + 1].key, condition: 'always', fromNodeType: nodes[i].nodeType, toNodeType: nodes[i + 1].nodeType });
            }
        }
        nodes.sort(function (a, b) { var ak = a.key + '|' + a.nodeType, bk = b.key + '|' + b.nodeType; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        edges.sort(function (a, b) { var ak = a.from + '|' + a.condition + '|' + a.to, bk = b.from + '|' + b.condition + '|' + b.to; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        return { nodes: nodes, edges: edges };
    }

    function fix74CanvasSnapshot() {
        if (!window.__WF_UI) return null;
        var uiNodes = window.__WF_UI.nodes || [];
        var uiEdges = window.__WF_UI.edges || [];
        var idMap = {};
        var nodes = [];
        uiNodes.forEach(function (n) {
            if (!n) return;
            var params = fix74StableValue(n.params || {}, 'params');
            var actionLike = { nodeType: n.key, label: n.label, params: params };
            idMap[n.id] = { key: fix74LabelKey(n.label), nodeType: String(n.key || '') };
            nodes.push({
                key: fix74LabelKey(n.label),
                label: String(n.label || '').trim(),
                nodeType: String(n.key || '').trim(),
                role: fix74RoleFromAction(actionLike),
                params: params,
                dataRefs: fix74DataRefs(params, []).sort()
            });
        });
        var edges = [];
        uiEdges.forEach(function (e) {
            var from = idMap[e && e.from], to = idMap[e && e.to];
            if (!from || !to) return;
            edges.push({
                from: from.key,
                to: to.key,
                condition: fix74NormalizeCondition(e.condition),
                fromNodeType: from.nodeType,
                toNodeType: to.nodeType
            });
        });
        nodes.sort(function (a, b) { var ak = a.key + '|' + a.nodeType, bk = b.key + '|' + b.nodeType; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        edges.sort(function (a, b) { var ak = a.from + '|' + a.condition + '|' + a.to, bk = b.from + '|' + b.condition + '|' + b.to; return ak < bk ? -1 : (ak > bk ? 1 : 0); });
        return { nodes: nodes, edges: edges };
    }

    function validateFix74PlanContract(plan, userText, includeVisual, decisionItems) {
        var errors = [];
        if (!plan || !plan.contract) return ['No se generó el contrato estructurado del workflow.'];
        var current = buildFix74PlanContract(plan, userText || '', decisionItems);
        var stored = plan.contract;
        if (current.graphFingerprint !== stored.graphFingerprint) errors.push('El plan cambió después de crear su contrato. Volvé a revisar la propuesta.');
        if (current.decisionFingerprint !== stored.decisionFingerprint) errors.push('Las decisiones cambiaron después de crear el plan. Volvé a generar la propuesta.');
        if (current.textFingerprint !== stored.textFingerprint) errors.push('La explicación textual cambió o no coincide con los parámetros reales del plan. Volvé a revisar la propuesta.');
        errors = errors.concat(fix74CoreContractErrors(current));
        errors = errors.concat(fix74bTextContractErrors(stored));

        var projection = fix74ProjectedCanvasSnapshot(plan);
        if (fix74GraphFingerprint(projection) !== stored.graphFingerprint) errors.push('La proyección que recibiría el canvas no coincide con el contrato del plan.');

        if (includeVisual !== false) {
            var visual = fix74GraphSnapshotFromVisualModel(buildVisualDraftModel(plan, []));
            var expectedVisual = fix74FunctionalGraphSnapshotFromPlan(plan);
            if (fix74GraphFingerprint(visual) !== fix74GraphFingerprint(expectedVisual))
                errors.push('El borrador visual funcional no coincide con la semántica del plan.');
        }
        return uniqueArray(errors);
    }

    function fix74CanvasContractErrors(plan) {
        if (!plan || !plan.contract) return ['No existe contrato para verificar el canvas.'];
        var canvas = fix74CanvasSnapshot();
        if (!canvas) return ['No se pudo leer el grafo aplicado desde la API pública del canvas.'];
        if (fix74GraphFingerprint(canvas) !== plan.contract.graphFingerprint) return ['El grafo aplicado no coincide con el contrato confirmado. No lo guardes hasta revisar.'];
        return [];
    }

    function fix74HumanNodeText(node) {
        node = node || {};
        var p = node.params || {};
        if (node.nodeType === 'doc.load') return 'Cargar documento: ' + (p.docTipoCodigo || node.label);
        if (node.nodeType === 'control.if') return 'Validar ' + (p.field || node.label) + ' (' + (p.op || 'condición') + ')';
        if (node.nodeType === 'human.task') return 'Crear tarea humana para ' + (node.role || fix74HumanTaskUser(p) || 'destino') + ': ' + (p.titulo || node.label);
        if (node.nodeType === 'util.notify') return 'Notificar a ' + (node.role || 'destino') + ': ' + (p.asunto || node.label);
        if (node.nodeType === 'util.logger') return 'Registrar log ' + (p.level || 'Info') + ': ' + (p.message || node.label);
        if (node.nodeType === 'util.end') return 'Finalizar flujo';
        if (node.nodeType === 'util.start') return 'Inicio';
        return node.label || node.nodeType;
    }

    function fix74bIsActionNode(node) {
        var t = String(node && node.nodeType || '');
        return t === 'human.task' || t === 'util.notify' || t === 'util.logger';
    }

    function fix74bKnownDataRefs(refs) {
        var known = [];
        (refs || []).forEach(function (ref) {
            ref = String(ref || '').trim();
            if (ref === 'biz.notaCredito.cae' || ref === 'biz.notaCredito.proveedor') pushUnique(known, ref);
        });
        known.sort();
        return known;
    }

    function fix74bDataRefLabel(ref) {
        if (ref === 'biz.notaCredito.cae') return 'CAE';
        if (ref === 'biz.notaCredito.proveedor') return 'proveedor';
        return ref;
    }

    function fix74bJoinLabels(refs) {
        var labels = (refs || []).map(fix74bDataRefLabel);
        if (!labels.length) return '';
        if (labels.length === 1) return labels[0];
        return labels.slice(0, -1).join(', ') + ' y ' + labels[labels.length - 1];
    }

    function fix74bRequestedDataRefs(contract) {
        var refs = [];
        ((contract && contract.decisions) || []).forEach(function (d) {
            if (String(d && d.key || '') !== 'data-message') return;
            var choice = normalizePhraseForSearch(d.choice || '');
            if (choice.indexOf('cae') >= 0) pushUnique(refs, 'biz.notaCredito.cae');
            if (choice.indexOf('proveedor') >= 0) pushUnique(refs, 'biz.notaCredito.proveedor');
        });
        refs.sort();
        return refs;
    }

    function fix74bConditionRepresentsMissingCae(contract, branch) {
        if (branch !== 'false') return false;
        var review = fix74ReviewModel(contract || { nodes: [], edges: [] });
        var cond = review.condition || {};
        var p = cond.params || {};
        return String(cond.nodeType || '') === 'control.if' &&
            String(p.field || '').trim() === 'biz.notaCredito.cae' &&
            normalizeKey(p.op || '') === 'NOT_EMPTY';
    }

    function fix74bTextItem(node, branch, contract) {
        var actual = fix74bKnownDataRefs(node && node.dataRefs || []);
        var requested = fix74bRequestedDataRefs(contract);
        var omitted = [];
        if (fix74bIsActionNode(node)) {
            requested.forEach(function (ref) { if (actual.indexOf(ref) < 0) omitted.push(ref); });
        }
        var reason = '';
        if (omitted.indexOf('biz.notaCredito.cae') >= 0 && fix74bConditionRepresentsMissingCae(contract, branch)) {
            reason = 'cae_missing_branch';
        }
        return {
            nodeKey: String(node && node.key || ''),
            branch: branch || 'main',
            dataRefs: actual,
            omittedRefs: omitted.sort(),
            omissionReason: reason
        };
    }

    function fix74bBuildTextProfile(contract) {
        var review = fix74ReviewModel(contract || { nodes: [], edges: [] });
        function mapPath(path, branch) {
            return (path || []).filter(fix74bIsActionNode).map(function (node) {
                return fix74bTextItem(node, branch, contract);
            });
        }
        return {
            version: 'fix74b-text-v1',
            requestedDataRefs: fix74bRequestedDataRefs(contract),
            main: mapPath(review.main, 'main'),
            truePath: mapPath(review.truePath, 'true'),
            falsePath: mapPath(review.falsePath, 'false')
        };
    }

    function fix74bTextContractErrors(contract) {
        var errors = [];
        if (!contract || !contract.textProfile || contract.textProfile.version !== 'fix74b-text-v1') {
            return ['El contrato no contiene una explicación textual fix74b válida.'];
        }
        var profile = contract.textProfile;
        var all = [].concat(profile.main || [], profile.truePath || [], profile.falsePath || []);
        all.forEach(function (item) {
            var node = fix74NodeByKey(contract, item.nodeKey);
            if (!node) {
                errors.push('La explicación textual referencia un nodo inexistente: ' + item.nodeKey + '.');
                return;
            }
            var expected = fix74bTextItem(node, item.branch, contract);
            if (fix74StableStringify(expected.dataRefs) !== fix74StableStringify(item.dataRefs || [])) {
                errors.push('La explicación de "' + node.label + '" anuncia datos distintos de sus parámetros reales.');
            }
            if (fix74StableStringify(expected.omittedRefs) !== fix74StableStringify(item.omittedRefs || [])) {
                errors.push('La explicación de "' + node.label + '" no informa correctamente los datos omitidos.');
            }
            if (expected.omissionReason !== String(item.omissionReason || '')) {
                errors.push('La explicación de "' + node.label + '" no justifica correctamente la omisión de datos por rama.');
            }
            var unexpected = (item.omittedRefs || []).filter(function (ref) {
                return !(ref === 'biz.notaCredito.cae' && item.omissionReason === 'cae_missing_branch');
            });
            if (unexpected.length) {
                errors.push('El nodo "' + node.label + '" omite ' + fix74bJoinLabels(unexpected) + ' sin una razón funcional permitida.');
            }
        });
        var expectedFingerprint = fix74Hash(fix74StableStringify(profile));
        if (String(contract.textFingerprint || '') !== expectedFingerprint) {
            errors.push('La huella de la explicación textual no coincide con su contenido.');
        }
        return uniqueArray(errors);
    }

    function fix74bHumanNodeDataText(node, branch, contract) {
        if (!fix74bIsActionNode(node)) return '';
        var item = fix74bTextItem(node, branch, contract);
        var parts = [];
        if (item.dataRefs.length) parts.push('Incluye ' + fix74bJoinLabels(item.dataRefs) + '.');
        else parts.push('No incorpora datos documentales en el contenido.');
        if (item.omissionReason === 'cae_missing_branch') {
            parts.push('El CAE no se incluye porque esta rama corresponde a CAE faltante.');
        }
        return parts.join(' ');
    }

    function fix74bBranchDataSummary(contract, path, branch, title) {
        var actions = (path || []).filter(fix74bIsActionNode);
        if (!actions.length) return '';
        var groups = {};
        actions.forEach(function (node) {
            var item = fix74bTextItem(node, branch, contract);
            var key = fix74StableStringify({ refs: item.dataRefs, omitted: item.omittedRefs, reason: item.omissionReason });
            if (!groups[key]) groups[key] = { roles: [], item: item };
            groups[key].roles.push(node.role || node.label || 'destino');
        });
        var chunks = [];
        Object.keys(groups).forEach(function (key) {
            var g = groups[key];
            var who = g.roles.join(' e ');
            var verb = g.roles.length > 1 ? ' incluyen ' : ' incluye ';
            var noVerb = g.roles.length > 1 ? ' no incluyen datos documentales' : ' no incluye datos documentales';
            var text = who + (g.item.dataRefs.length ? verb + fix74bJoinLabels(g.item.dataRefs) : noVerb);
            if (g.item.omissionReason === 'cae_missing_branch') text += '; el CAE no se incluye porque la rama representa CAE faltante';
            chunks.push(text);
        });
        return title + ': ' + chunks.join('. ') + '.';
    }

    function fix74bContractDataSummary(contract) {
        if (!contract) return '';
        var review = fix74ReviewModel(contract);
        var parts = [];
        var main = fix74bBranchDataSummary(contract, review.main, 'main', 'Flujo principal');
        var yes = fix74bBranchDataSummary(contract, review.truePath, 'true', 'Rama SI');
        var no = fix74bBranchDataSummary(contract, review.falsePath, 'false', 'Rama NO');
        if (main) parts.push(main);
        if (yes) parts.push(yes);
        if (no) parts.push(no);
        return parts.join(' ');
    }

    function fix74FollowPath(contract, fromKey, firstCondition) {
        var out = [];
        var seen = {};
        var current = fromKey;
        var condition = firstCondition;
        for (var i = 0; i < 40; i++) {
            var edges = fix74Outgoing(contract, current);
            var edge = null;
            for (var j = 0; j < edges.length; j++) {
                if (condition && edges[j].condition === condition) { edge = edges[j]; break; }
            }
            if (!edge && !condition) {
                for (var k = 0; k < edges.length; k++) if (edges[k].condition === 'always') { edge = edges[k]; break; }
            }
            if (!edge) break;
            var node = fix74NodeByKey(contract, edge.to);
            if (!node || seen[node.key]) break;
            seen[node.key] = true;
            out.push(node);
            if (node.nodeType === 'util.end' || node.nodeType === 'control.if') break;
            current = node.key;
            condition = '';
        }
        return out;
    }

    function fix74ReviewModel(contract) {
        var nodes = (contract && contract.nodes) || [];
        var start = null, cond = null;
        nodes.forEach(function (n) { if (n.nodeType === 'util.start') start = n; });
        var main = [];
        if (start) {
            var path = fix74FollowPath(contract, start.key, '');
            for (var i = 0; i < path.length; i++) {
                if (path[i].nodeType === 'control.if') { cond = path[i]; break; }
                if (path[i].nodeType !== 'util.end') main.push(path[i]);
            }
        }
        if (!cond) {
            for (var j = 0; j < nodes.length; j++) if (nodes[j].nodeType === 'control.if') { cond = nodes[j]; break; }
        }
        return {
            main: main,
            condition: cond,
            truePath: cond ? fix74FollowPath(contract, cond.key, 'true') : [],
            falsePath: cond ? fix74FollowPath(contract, cond.key, 'false') : []
        };
    }

    function fix74ContractStatusHtml(plan, validation) {
        var errors = (validation && validation.errors) || [];
        var contract = plan && plan.contract;
        var test = aiFix74SelfTestResult || { ok: false, total: 0, passed: 0 };
        if (errors.length || !contract) {
            var h = '<div class="wf-ai-contract-status invalid"><strong>Creación bloqueada por el contrato</strong>';
            if (errors.length) {
                h += '<ul>';
                errors.forEach(function (e) { h += '<li>' + htmlEncode(e) + '</li>'; });
                h += '</ul>';
            }
            return h + '</div>';
        }
        return '<div class="wf-ai-contract-status valid"><strong>Contrato estructural y textual validado</strong><span>' +
            htmlEncode(String((contract.nodes || []).length) + ' nodos · ' + String((contract.edges || []).length) + ' conexiones · matriz ' + String(test.passed || 0) + '/' + String(test.total || 0)) +
            '</span></div>';
    }

    function fix74SyntheticAction(mode, label, role, message) {
        var type = guidedActionNodeType(mode);
        if (type === 'human.task') return makePlanAction(type, label, { titulo: label, descripcion: message, rol: role });
        if (type === 'util.logger') return makePlanAction(type, label, { level: 'Warn', message: role + ': ' + message });
        return makePlanAction(type, label, { destinoTipo: 'rol', rolDestino: role, destino: role, asunto: label, mensaje: message });
    }

    function fix74SyntheticPlan(modeTrue, modeFalse, itScope, dataMode) {
        var cae = dataMode === 'cae' || dataMode === 'both';
        var proveedor = dataMode === 'proveedor' || dataMode === 'both';
        function msg(hasCae) {
            var parts = [hasCae ? 'Con CAE.' : 'Sin CAE.'];
            if (cae && hasCae) parts.push('CAE: {{biz.notaCredito.cae}}.');
            if (proveedor) parts.push('Proveedor: {{biz.notaCredito.proveedor}}.');
            return parts.join(' ');
        }
        var actions = [
            makePlanAction('util.start', 'Inicio', {}),
            makePlanAction('doc.load', 'Cargar NC', { docTipoCodigo: 'NOTA_CREDITO_ELECTRONICA_AR', path: 'input.filePath', mode: 'auto' }),
            makePlanAction('control.if', 'Validar CAE', { field: 'biz.notaCredito.cae', op: 'not_empty' }),
            fix74SyntheticAction(modeTrue, 'Acción Dirección', 'DIR_GENERAL', msg(true)),
            fix74SyntheticAction(modeFalse, 'Acción Compras', 'COMPRAS', msg(false))
        ];
        if (itScope === 'true' || itScope === 'both') actions.push(fix74SyntheticAction(modeTrue, 'Acción IT SI', 'IT', msg(true)));
        if (itScope === 'false' || itScope === 'both') actions.push(fix74SyntheticAction(modeTrue, 'Acción IT NO', 'IT', msg(false)));
        actions.push(makePlanAction('util.end', 'Fin', {}));

        function find(label) { for (var i = 0; i < actions.length; i++) if (actions[i].label === label) return actions[i]; return null; }
        var edges = [];
        addStructuredConnection(edges, find('Inicio'), find('Cargar NC'), '');
        addStructuredConnection(edges, find('Cargar NC'), find('Validar CAE'), '');
        var trueFirst = find(itScope === 'true' || itScope === 'both' ? 'Acción Dirección' : 'Acción Dirección');
        addStructuredConnection(edges, find('Validar CAE'), trueFirst, 'SI');
        if (itScope === 'true' || itScope === 'both') {
            addStructuredConnection(edges, find('Acción Dirección'), find('Acción IT SI'), '');
            addStructuredConnection(edges, find('Acción IT SI'), find('Fin'), '');
        } else addStructuredConnection(edges, find('Acción Dirección'), find('Fin'), '');
        addStructuredConnection(edges, find('Validar CAE'), find('Acción Compras'), 'NO');
        if (itScope === 'false' || itScope === 'both') {
            addStructuredConnection(edges, find('Acción Compras'), find('Acción IT NO'), '');
            addStructuredConnection(edges, find('Acción IT NO'), find('Fin'), '');
        } else addStructuredConnection(edges, find('Acción Compras'), find('Fin'), '');
        return { actions: actions, proposedConnections: edges, missingData: [], branchPlan: { branches: [] } };
    }

    function runFix74ContractSelfTest() {
        if (aiFix74SelfTestResult) return aiFix74SelfTestResult;
        var modes = ['notify', 'human_task', 'logger'];
        var scopes = ['none', 'true', 'false', 'both'];
        var dataModes = ['none', 'cae', 'proveedor', 'both'];
        var total = 0, passed = 0, errors = [];
        modes.forEach(function (trueMode) {
            modes.forEach(function (falseMode) {
                scopes.forEach(function (scope) {
                    dataModes.forEach(function (dataMode) {
                        total++;
                        var plan = fix74SyntheticPlan(trueMode, falseMode, scope, dataMode);
                        plan.contract = buildFix74PlanContract(plan, '', []);
                        var testErrors = validateFix74PlanContract(plan, '', true, []);
                        if (!testErrors.length) passed++;
                        else if (errors.length < 8) errors.push(trueMode + '/' + falseMode + '/' + scope + '/' + dataMode + ': ' + testErrors.join(' | '));
                    });
                });
            });
        });

        total += 7;

        // FIX84C2Be: caso positivo que antes faltaba en la matriz cliente.
        // Una human.task destinada sólo por usuarioAsignado es válida y no debe forzar fallback GUIADO.
        var userTaskPlan = {
            actions: [
                makePlanAction('util.start', 'Inicio', {}),
                makePlanAction('human.task', 'Revisar factura', { titulo: 'Revisar factura', usuarioAsignado: 'OMARD\\USUARIO1' }),
                makePlanAction('util.end', 'Fin', {})
            ],
            proposedConnections: [],
            missingData: [],
            branchPlan: { branches: [] }
        };
        addStructuredConnection(userTaskPlan.proposedConnections, userTaskPlan.actions[0], userTaskPlan.actions[1], '');
        addStructuredConnection(userTaskPlan.proposedConnections, userTaskPlan.actions[1], userTaskPlan.actions[2], '');
        userTaskPlan.contract = buildFix74PlanContract(userTaskPlan, '', []);
        if (!validateFix74PlanContract(userTaskPlan, '', true, []).length) passed++; else errors.push('Una tarea con usuarioAsignado válido fue rechazada por el contrato cliente.');

        var brokenBranch = fix74SyntheticPlan('notify', 'notify', 'false', 'both');
        brokenBranch.contract = buildFix74PlanContract(brokenBranch, '', []);
        brokenBranch.proposedConnections = brokenBranch.proposedConnections.filter(function (e) { return !(e.from === 'Validar CAE' && fix74NormalizeCondition(e.condition) === 'false'); });
        if (validateFix74PlanContract(brokenBranch, '', true, []).length) passed++; else errors.push('No se detectó una rama NO eliminada.');

        var changedType = fix74SyntheticPlan('notify', 'notify', 'false', 'both');
        changedType.contract = buildFix74PlanContract(changedType, '', []);
        changedType.actions[3].nodeType = 'human.task';
        if (validateFix74PlanContract(changedType, '', true, []).length) passed++; else errors.push('No se detectó un tipo de nodo modificado.');

        var duplicate = fix74SyntheticPlan('notify', 'notify', 'false', 'both');
        duplicate.actions[4].label = duplicate.actions[3].label;
        duplicate.contract = buildFix74PlanContract(duplicate, '', []);
        if (fix74CoreContractErrors(duplicate.contract).length) passed++; else errors.push('No se detectó una etiqueta duplicada.');

        var changedDecision = fix74SyntheticPlan('notify', 'notify', 'false', 'both');
        var initialDecision = [{ key: 'notify-mode', label: 'Usar notificación interna', clarification: '', superseded: false }];
        var laterDecision = [{ key: 'notify-mode', label: 'Crear tarea humana', clarification: '', superseded: false }];
        changedDecision.contract = buildFix74PlanContract(changedDecision, '', initialDecision);
        if (validateFix74PlanContract(changedDecision, '', true, laterDecision).length) passed++; else errors.push('No se detectó una decisión modificada después del contrato.');

        var changedTextRefs = fix74SyntheticPlan('human_task', 'human_task', 'false', 'both');
        changedTextRefs.contract = buildFix74PlanContract(changedTextRefs, '', [{ key: 'data-message', label: 'Incluir CAE y proveedor', superseded: false }]);
        if (changedTextRefs.contract.textProfile.falsePath.length) {
            changedTextRefs.contract.textProfile.falsePath[0].dataRefs.push('biz.notaCredito.cae');
            changedTextRefs.contract.textProfile.falsePath[0].dataRefs.sort();
            changedTextRefs.contract.textFingerprint = fix74Hash(fix74StableStringify(changedTextRefs.contract.textProfile));
        }
        if (fix74bTextContractErrors(changedTextRefs.contract).length) passed++; else errors.push('No se detectó un dato textual anunciado que no existe en el nodo.');

        var missingTextReason = fix74SyntheticPlan('human_task', 'human_task', 'false', 'both');
        missingTextReason.contract = buildFix74PlanContract(missingTextReason, '', [{ key: 'data-message', label: 'Incluir CAE y proveedor', superseded: false }]);
        if (missingTextReason.contract.textProfile.falsePath.length) {
            missingTextReason.contract.textProfile.falsePath[0].omissionReason = '';
            missingTextReason.contract.textFingerprint = fix74Hash(fix74StableStringify(missingTextReason.contract.textProfile));
        }
        if (fix74bTextContractErrors(missingTextReason.contract).length) passed++; else errors.push('No se detectó la falta de explicación para CAE omitido en la rama NO.');

        aiFix74SelfTestResult = { ok: passed === total, total: total, passed: passed, errors: errors };
        return aiFix74SelfTestResult;
    }

    function buildStructuredGuideResult(userText) {
        var plan = buildStructuredGuidePlan();
        if (!plan) return null;
        plan.assistantVersion = 'constructor-structured-fix74b';
        plan.contract = buildFix74PlanContract(plan, userText || '');

        var validation = buildFunctionalValidation();
        if (!hasHumanTaskResultBranches()) {
            var consistencyErrors = guidedDecisionConsistencyErrors(plan, userText || '');
            consistencyErrors.forEach(function (e) { pushUnique(validation.errors, e); });
        }

        var contractErrors = validateFix74PlanContract(plan, userText || '', true);
        contractErrors.forEach(function (e) { pushUnique(validation.errors, e); });

        var selfTest = runFix74ContractSelfTest();
        if (!selfTest.ok) pushUnique(validation.errors, 'La matriz interna del contrato fix74b no pasó completa.');
        validation.contract = {
            ok: contractErrors.length === 0 && selfTest.ok,
            fingerprint: plan.contract.fingerprint,
            graphFingerprint: plan.contract.graphFingerprint,
            matrixPassed: selfTest.passed,
            matrixTotal: selfTest.total
        };
        validation.ok = validation.errors.length === 0;
        aiFix74LastContractCheck = validation.contract;
        return {
            ok: true,
            provider: 'constructor-local',
            sourceKind: 'step_by_step',
            structuredSource: true,
            model: 'Constructor IA estructurado fix74b',
            messageToUser: plan.messageToUser,
            plan: plan,
            validation: validation,
            rawText: userText || ''
        };
    }


    function interpretar() {
        resetGuidedPlanConfirmation();
        var userText = getCurrentAiPhrase();
        if (!userText) {
            setStatus('Escribí primero qué querés construir.', 'warn');
            return;
        }

        var structuredResult = buildStructuredGuideResult(userText);
        if (structuredResult) {
            setStatus('Construyendo el plan con la capa común FIX84C2A...', 'busy');
            var structuredRunBtn = $('wfAiRun');
            if (structuredRunBtn) structuredRunBtn.disabled = true;
            normalizeStructuredGuideResult(structuredResult, userText, function (resolved) {
                renderResult(resolved);
                setStatus('Propuesta estructurada generada por la capa común. Revisá y aplicá al canvas cuando esté correcta.', 'ok');
            }, function (err) {
                renderResult({ ok: false, error: err.message || String(err) });
                setStatus('No se pudo construir el plan por FIX84C2A.', 'error');
            }, function () {
                if (structuredRunBtn) structuredRunBtn.disabled = false;
            });
            return;
        }

        setStatus('Interpretando con ML.NET local...', 'busy');
        var btn = $('wfAiRun');
        if (btn) btn.disabled = true;

        callAiAssistant(userText, function (res) {
            renderResult(res);
            if (res.ok) setStatus('Propuesta recibida. Revisá y aplicá al canvas cuando esté correcta.', 'ok');
            else setStatus('No se pudo obtener una propuesta válida.', 'error');
        }, function (err) {
            renderResult({ ok: false, error: err.message || String(err) });
            setStatus('Error llamando al Asistente IA.', 'error');
        }, function () {
            if (btn) btn.disabled = false;
        });
    }

    function init() {
        ensureCollapsedLauncher();
        ensureWideModeUi();
        ensureGuideUi();
        startAssistantInspectorWatcher();
        setTimeout(importRegressionPhraseIntoConstructor, 0);

        var btn = $('wfAiRun');
        if (btn) btn.addEventListener('click', openStepByStepGuide);

        var verify = $('wfAiVerify');
        if (verify) verify.addEventListener('click', verificarFrase);

        var clear = $('wfAiClear');
        if (clear) clear.addEventListener('click', function () {
            var t = $('wfAiPrompt');
            var r = $('wfAiResult');
            if (t) t.value = '';
            if (r) r.innerHTML = '';
            lastPlan = null;
            lastAssistantResult = null;
            aiFallbackResolved = {};
            aiFallbackResolvedItems = [];
            aiFallbackBaseKey = '';
            aiFallbackAcceptedRewriteKey = '';
            resetFix84bDialogue();
            resetGuidedPlanConfirmation();
            clearVisualDraft();
            aiVisualDraftSelectedIssueKey = '';
            setStatus('', '');
        });

        var canvasClear = $('btnClear');
        if (canvasClear) canvasClear.addEventListener('click', function () {
            // El resumen pertenece al workflow visible; al vaciar el canvas deja de corresponder.
            clearAppliedDecisionSummary();
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
