// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// fix39c: corrige render recursivo de ramas anidadas sobre fix39b.
// fix39b: Constructor IA en 3 columnas, rama activa visual y datos clickeables sin tocar motor.
(function () {
    var lastPlan = null;

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

    function userInputHtml(id, selectedValue) {
        var value = String(selectedValue || firstUser(['OMARD\\OMARD']) || '').trim();
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
        if (step.type === 'human_task') return 'Tarea humana: ' + ((step.destType === 'usuario' || step.user) ? (step.user || '(usuario)') : (step.role || '')) + ' / ' + (step.purpose || 'revisión');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
        if (step.type === 'http_request') return 'Solicitud HTTP: ' + ((step.method || 'GET') + ' ' + (step.url || '(sin URL)'));
        if (step.type === 'sql_query') return 'Consulta SQL: ' + sqlShortText(step.query || '');
        if (step.type === 'file_read') return 'Archivo: Leer ' + (step.path || '(sin ruta)');
        if (step.type === 'file_write') return 'Archivo: Escribir ' + (step.path || '(sin ruta)');
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
        return !(op === 'not_empty' || op === 'empty' || op === 'true' || op === 'false');
    }

    function operatorPhrase(op) {
        var map = {
            '>': 'mayor que',
            '>=': 'mayor o igual que',
            '<': 'menor que',
            '<=': 'menor o igual que',
            '=': 'igual a',
            '!=': 'distinto de',
            'contains': 'contiene',
            'not_contains': 'no contiene',
            'not_empty': 'no está vacío',
            'empty': 'está vacío',
            'true': 'es verdadero',
            'false': 'es falso'
        };
        return map[String(op || '')] || String(op || '');
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
        // Para cargar la rama RECHAZADO de la misma tarea original, no debe tomarse esa tarea hija como dueña.
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
        if (step.branchSourceId) {
            for (var i = 0; i < guideSteps.length; i++) {
                if (guideSteps[i] && guideSteps[i].id === step.branchSourceId && guideSteps[i].type === ownerType) return i;
            }
        }
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
            items.push(['if_task_ok', 'Resultado APROBADO/APTO de la última tarea humana']);
            items.push(['if_task_reject', 'Resultado RECHAZADO/NO APTO de la última tarea humana']);
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

        if (type === 'condition' && setSelectOrInputValue('wfAiStepField', path)) {
            syncConditionFields();
            setStatus('Campo cargado en la condición: ' + path, 'ok');
            return;
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
        if (step.type === 'human_task') return 'Tarea humana: ' + ((step.destType === 'usuario' || step.user) ? (step.user || '(usuario)') : (step.role || '')) + ' / ' + (step.purpose || 'revisión');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
        if (step.type === 'http_request') return 'Solicitud HTTP: ' + ((step.method || 'GET') + ' ' + (step.url || '(sin URL)'));
        if (step.type === 'sql_query') return 'Consulta SQL: ' + sqlShortText(step.query || '');
        if (step.type === 'file_read') return 'Archivo: Leer ' + (step.path || '(sin ruta)');
        if (step.type === 'file_write') return 'Archivo: Escribir ' + (step.path || '(sin ruta)');
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
            var role = step.role || 'COMPRAS';
            var user = step.user || '';
            var purpose = step.purpose || 'revisión';
            ctx.lastTaskRole = user || role;
            if (step.id) ctx.tasksById[step.id] = { role: user || role, purpose: purpose };
            if (step.destType === 'usuario' || user) return 'mandar la tarea al usuario ' + (user || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD') + ' para ' + purpose;
            return 'mandar la tarea al rol ' + role + ' para ' + purpose;
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

    function buildIncrementalPhrase() {
        var parts = ['Quiero iniciar un flujo'];
        var ctx = { lastCondition: null, lastTaskRole: null, conditionsById: {}, tasksById: {}, branchesUsed: false, commonAfterBranchExplained: false };

        guideSteps.forEach(function (step) {
            var prefix = branchPrefix(step.branch, ctx, step);
            var body = stepBody(step, ctx);

            // Separador importante para el intérprete ML.NET:
            // sin "luego", un cuerpo de email/notificación puede absorber el texto
            // del paso siguiente. Ej.: cuerpo ... notificar internamente ...
            if (!prefix && parts.length > 1) {
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

        return parts.join(', ') + '.';
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
        var mainIndexes = mainStepIndexesForValidation();

        if (!hasEndStep()) {
            pushUnique(result.warnings, 'Falta finalizar el flujo. Agregá “Finalizar flujo” como último paso para dejar la propuesta cerrada.');
        }

        mainIndexes.forEach(function (idx, pos) {
            var step = guideSteps[idx];
            if (!step) return;

            if (step.type === 'end' && pos < mainIndexes.length - 1) {
                pushUnique(result.warnings, 'Hay pasos comunes después de “Finalizar flujo”. Revisá el orden, porque esos pasos podrían quedar fuera del recorrido principal.');
            }

            if (step.type === 'doc_load' && isProbablyTestPath(step.path)) {
                pushUnique(result.warnings, 'La ruta del documento en ' + stepShortName(step, idx) + ' parece una ruta de prueba (“' + String(step.path || '').trim() + '”).');
            }

            if (step.type === 'condition') {
                if (operatorNeedsValue(step.operator) && !String(step.value || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el operador seleccionado necesita un valor.');
                }

                var condGroups = maps.conditions[idx] || { si: [], no: [] };
                if (!condGroups.si.length) pushUnique(result.warnings, stepShortName(step, idx) + ': la rama SI está vacía. Si se cumple, continuará directo al siguiente paso común.');
                if (!condGroups.no.length) pushUnique(result.warnings, stepShortName(step, idx) + ': la rama NO está vacía. Si no se cumple, continuará directo al siguiente paso común.');

                var nextIdx = nextMainIndexAfter(mainIndexes, idx);
                if ((condGroups.si.length || condGroups.no.length) && nextIdx >= 0 && guideSteps[nextIdx] && guideSteps[nextIdx].type !== 'end') {
                    pushUnique(result.warnings, 'Después de ' + stepShortName(step, idx) + ', el paso común “' + createStepTitle(guideSteps[nextIdx]) + '” se ejecutará luego de cualquiera de las ramas.');
                }
            }

            if (step.type === 'human_task') {
                if (step.destType === 'usuario' && !resolveUserSelection(step.user)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el usuario asignado no coincide con un usuario real del catálogo.');
                }

                var taskGroups = maps.tasks[idx] || null;
                if (taskGroups && (taskGroups.ok.length || taskGroups.reject.length)) {
                    if (!taskGroups.ok.length) pushUnique(result.warnings, stepShortName(step, idx) + ': falta definir qué pasa si la tarea queda APROBADA/APTA.');
                    if (!taskGroups.reject.length) pushUnique(result.warnings, stepShortName(step, idx) + ': falta definir qué pasa si la tarea queda RECHAZADA/NO APTA.');

                    var ownerDest = humanDestinationKey(step);
                    var okHuman = firstBranchHumanDestination(taskGroups.ok);
                    var rejectHuman = firstBranchHumanDestination(taskGroups.reject);
                    if (okHuman && ownerDest && ownerDest === humanDestinationKey(okHuman)) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': la rama APROBADO/APTO vuelve al mismo destino (' + humanDestinationText(step) + '). Es válido si representa otra etapa, pero conviene diferenciar claramente el objetivo.');
                    }
                    if (rejectHuman && ownerDest && ownerDest === humanDestinationKey(rejectHuman)) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': la rama RECHAZADO/NO APTO vuelve al mismo destino (' + humanDestinationText(step) + '). Revisá si corresponde.');
                    }

                    var nextTaskIdx = nextMainIndexAfter(mainIndexes, idx);
                    if (nextTaskIdx >= 0 && guideSteps[nextTaskIdx] && guideSteps[nextTaskIdx].type !== 'end') {
                        pushUnique(result.warnings, 'Después de ' + stepShortName(step, idx) + ', el paso común “' + createStepTitle(guideSteps[nextTaskIdx]) + '” se ejecutará luego de cualquiera de los resultados humanos.');
                    }
                }

                if (pos + 1 < mainIndexes.length) {
                    var nextStep = guideSteps[mainIndexes[pos + 1]];
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
                } else if (/^https?:\/\//i.test(String(step.url || '')) && !/localhost|127\.0\.0\.1|intranet/i.test(String(step.url || ''))) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': la URL parece externa. Para intranet conviene usar una URL relativa o del servidor interno.');
                }
            }

            if (step.type === 'sql_query') {
                var q = String(step.query || '').trim();
                if (!q) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la consulta/comando SQL.');
                }
                if (/\b(DROP|TRUNCATE|ALTER)\b/i.test(q)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el SQL contiene una instrucción peligrosa. Revisalo antes de ejecutar.');
                }
                if (/^\s*DELETE\b/i.test(q) && !/\bWHERE\b/i.test(q)) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': DELETE sin WHERE puede afectar demasiados registros.');
                }
                if (parseJsonObjectOrEmpty(step.paramsJson || '') === null) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': los parámetros SQL deben ser JSON válido, por ejemplo {"Id":150484}.');
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
                if (step.sourceMode === 'context') {
                    if (!String(step.origen || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar la variable origen para escribir.');
                    else if (!isValidStatePath(step.origen)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable origen tiene formato inválido.');
                } else if (!String(step.content || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el contenido a escribir.');
                }
            }

            if (step.type === 'subflow') {
                if (!String(step.ref || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': falta seleccionar el workflow a ejecutar.');
                var sj = parseJsonObjectOrEmpty(step.inputJson || '{}');
                if (sj === null) pushUnique(result.warnings, stepShortName(step, idx) + ': el Input JSON debe ser un objeto JSON válido.');
                if (step.alias && !subflowAliasIsValid(step.alias)) pushUnique(result.warnings, stepShortName(step, idx) + ': el alias debe usar letras/números/_ y no puede empezar con número.');
                var md = parseInt(step.maxDepth || '10', 10);
                if (isNaN(md) || md < 1 || md > 50) pushUnique(result.warnings, stepShortName(step, idx) + ': la profundidad máxima debe estar entre 1 y 50.');
                var aliasCount = {};
                guideSteps.forEach(function (x) {
                    var a = x && x.type === 'subflow' ? String(x.alias || '').trim() : '';
                    if (a) aliasCount[normalizeKey(a)] = (aliasCount[normalizeKey(a)] || 0) + 1;
                });
                if (step.alias && aliasCount[normalizeKey(step.alias)] > 1) pushUnique(result.warnings, stepShortName(step, idx) + ': hay otro subflujo con el mismo alias. Conviene usar alias únicos.');
                var subflowCount = guideSteps.filter(function (x) { return x && x.type === 'subflow'; }).length;
                if (subflowCount > 1 && !String(step.alias || '').trim()) pushUnique(result.warnings, stepShortName(step, idx) + ': hay más de un subflujo. Conviene definir alias para poder distinguir salidas subflows.<alias>.*.');
            }

            if (step.type === 'state_set') {
                if (isStateJsonMode(step)) {
                    var setObj = parseJsonObjectOrEmpty(step.setJson || '');
                    if (setObj === null) {
                        pushUnique(result.warnings, stepShortName(step, idx) + ': el JSON a guardar no es válido.');
                    } else {
                        var keys = Object.keys(setObj || {});
                        if (!keys.length) pushUnique(result.warnings, stepShortName(step, idx) + ': el JSON a guardar está vacío.');
                        keys.forEach(function (k) {
                            if (!isValidStatePath(k)) pushUnique(result.warnings, stepShortName(step, idx) + ': la variable destino "' + k + '" tiene formato inválido.');
                            if (isTechnicalOutputPath(k)) pushUnique(result.warnings, stepShortName(step, idx) + ': estás por guardar sobre una salida técnica (' + k + '). Conviene usar biz.* o wf.vars.*.');
                        });
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

                var next = guideSteps[idx + 1];
                if (!next) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': Reintentar debe ubicarse antes del nodo que querés reintentar. Ahora no tiene un paso siguiente.');
                } else if (next.type === 'end') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': Reintentar antes de Finalizar flujo no aporta valor. Ubicalo antes de HTTP, SQL, correo, documento o archivo.');
                } else if (next.type === 'human_task') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': no conviene reintentar una tarea humana porque podría crear tareas duplicadas.');
                } else if (next.type === 'logger' || next.type === 'state_set' || next.type === 'state_remove' || next.type === 'delay') {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': el siguiente paso normalmente no requiere reintento. Usalo principalmente antes de HTTP, SQL, correo, documento o archivo.');
                }
            }

            if (step.type === 'error_handler') {
                if (!String(step.message || '').trim()) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el mensaje de error.');
                }
                if (step.retry) {
                    pushUnique(result.warnings, stepShortName(step, idx) + ': “Volver a intentar” en util.error solo marca intención; para reintentos reales agregá el paso Reintentar antes del nodo que puede fallar.');
                }
            }

            if (step.type === 'logger' && !String(step.message || '').trim()) {
                pushUnique(result.warnings, stepShortName(step, idx) + ': falta indicar el mensaje del log.');
            }
        });

        result.ok = result.errors.length === 0;
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
                '<div class="wf-ai-guide-row"><label>Campo a validar</label><select id="wfAiStepField" class="wf-ai-select">' + availableFieldOptions(defaultField) + '</select></div>' +
                '<div class="wf-ai-guide-hint" id="wfAiStepFieldTypeHint"></div>' +
                '<div class="wf-ai-guide-row"><label>Operador</label><select id="wfAiStepOperator" class="wf-ai-select">' + conditionOperatorOptions(meta.type, '') + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepValueRow"><label>Valor</label><input id="wfAiStepConditionValue" class="wf-ai-input" placeholder="Ej.: 100000, texto, fecha o campo" /></div>' +
                '<div class="wf-ai-guide-note">Después hacé clic en SI CUMPLE o NO CUMPLE en la columna de pasos para agregar acciones dentro de cada rama.</div>';
        }
        if (type === 'human_task') {
            return '' +
                guideBranchRowHtml('then') +
                '<div class="wf-ai-guide-row"><label>Destino</label><select id="wfAiStepTaskDestType" class="wf-ai-select"><option value="rol">Rol</option><option value="usuario">Usuario</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskRoleRow"><label>Rol</label><select id="wfAiStepRole" class="wf-ai-select">' + roleOptions(firstRole(['COMPRAS'])) + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskUserRow" style="display:none"><label>Usuario</label>' + userInputHtml('wfAiStepTaskUser', firstUser(['OMARD\\OMARD'])) + '</div>' +
                '<div class="wf-ai-guide-row"><label>Qué hace</label><input id="wfAiStepPurpose" class="wf-ai-input" value="revisión" placeholder="Ej.: revisar, aprobar, corregir, cargar datos" /></div>' +
                '<div class="wf-ai-guide-note">Después hacé clic en APROBADO/APTO o RECHAZADO/NO APTO en la columna de pasos para agregar acciones según el resultado humano.</div>';
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
                '<div class="wf-ai-guide-row"><label>Mensaje</label><textarea id="wfAiStepMessage" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: Total=${sql.rowCount}, Estado=${payload.status}">Paso agregado por Asistente IA</textarea></div>' +
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
            if (step.fieldPath) {
                setControlValue('wfAiStepField', step.fieldPath);
                syncConditionFields();
                setControlValue('wfAiStepOperator', step.operator || 'not_empty');
                syncConditionFields();
                setControlValue('wfAiStepConditionValue', step.value || '');
            } else {
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
            setControlValue('wfAiStepRole', step.role || firstRole(['COMPRAS']));
            setControlValue('wfAiStepTaskUser', step.user || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD');
            setControlValue('wfAiStepPurpose', step.purpose || 'revisión');
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
            setControlValue('wfAiStepMessage', step.message || 'Paso agregado por Asistente IA');
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

        var condition = $('wfAiStepCondition');
        if (condition) condition.addEventListener('change', syncDynamicStepFields);

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

        syncDynamicStepFields();
        renderActiveTargetBox();
        updateGuideEditMode();
    }

    function validateCurrentStepForm() {
        var type = selectedText('wfAiStepType') || 'doc_load';

        if (type === 'notify' && selectedText('wfAiStepDestType') === 'usuario') {
            var notifyUser = resolveUserSelection(selectedText('wfAiStepUser'));
            if (!notifyUser) {
                var nf = $('wfAiStepUser');
                if (nf && nf.focus) nf.focus();
                return 'Seleccioná un usuario válido de la lista para la notificación interna.';
            }
            setControlValue('wfAiStepUser', notifyUser);
        }

        if (type === 'human_task' && selectedText('wfAiStepTaskDestType') === 'usuario') {
            var taskUser = resolveUserSelection(selectedText('wfAiStepTaskUser'));
            if (!taskUser) {
                var tf = $('wfAiStepTaskUser');
                if (tf && tf.focus) tf.focus();
                return 'Seleccioná un usuario válido de la lista para la tarea humana.';
            }
            setControlValue('wfAiStepTaskUser', taskUser);
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
        } else if (type === 'human_task') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.destType = selectedText('wfAiStepTaskDestType') || 'rol';
            step.role = selectedText('wfAiStepRole') || firstRole(['COMPRAS']);
            step.user = selectedText('wfAiStepTaskUser') || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD';
            step.purpose = selectedText('wfAiStepPurpose') || 'revisión';
            if (step.destType !== 'usuario') step.user = '';
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
            step.message = selectedText('wfAiStepMessage') || 'Paso agregado por Asistente IA';
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

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-ok' + okActive + '" data-guide-target-branch="if_task_ok" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Resultado APTO de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-task-branch-title">APROBADO / APTO</div>';
        if (groups.ok.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.ok.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-reject' + rejectActive + '" data-guide-target-branch="if_task_reject" data-guide-target-source-id="' + htmlEncode(ownerId) + '" data-guide-target-label="Resultado NO APTO de: ' + htmlEncode(ownerTitle) + '"><div class="wf-ai-task-branch-title">RECHAZADO / NO APTO</div>';
        if (groups.reject.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.reject.forEach(function (item) { html += renderGuideStepTree(item.step, item.idx, 'wf-ai-step-branch', branchMap, taskBranchMap, path); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch-help">Hacé clic en APROBADO/APTO o RECHAZADO/NO APTO para agregar el próximo paso en ese resultado humano. Operativamente se usa <strong>apto</strong> para aprobado y <strong>no_apto</strong> para no aprobado.</div>';
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
            '      <div class="wf-ai-guide-note">No agrega nodos directamente: primero arma la frase y luego usa la interpretación IA local/offline.</div>' +
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
            '  <button type="button" class="btn" id="wfAiShow">Mostrar Asistente IA</button>' +
            '</div>';

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

        return box;
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

    function canApplyPlan(plan, validation, missing) {
        if (!plan || !plan.actions || !plan.actions.length) return false;
        if (missing && missing.length) return false;
        if (validation && validation.errors && validation.errors.length) return false;
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

        var result = window.__WF_UI.applyAiPlan(lastPlan, {});
        if (result && result.ok) {
            hideAssistantAfterApply(result.message || 'Propuesta aplicada al canvas.');
        } else if (result && result.cancelled) {
            setStatus(result.message || 'Aplicación cancelada.', 'warn');
        } else {
            setStatus((result && result.message) || 'No se pudo aplicar la propuesta al canvas.', 'error');
        }
    }

    function renderResult(res) {
        var out = $('wfAiResult');
        if (!out) return;

        if (!res || !res.ok) {
            lastPlan = null;
            var msg = (res && (res.messageToUser || res.error)) || 'Error del Asistente IA.';
            var detail = res && res.error ? res.error : '';
            var htmlErr = '<div class="wf-ai-error">' + htmlEncode(msg) + '</div>';
            if (detail && detail !== msg) {
                htmlErr += '<details class="wf-ai-json" open><summary>Error técnico</summary><pre>' + htmlEncode(detail) + '</pre></details>';
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
        var actions = plan.actions || [];
        var missing = plan.missingData || [];
        var warnings = [];

        if (validation.warnings && validation.warnings.length) warnings = warnings.concat(validation.warnings);
        if (res.catalogWarnings && res.catalogWarnings.length) warnings = warnings.concat(res.catalogWarnings);
        if (plan.warnings && plan.warnings.length) warnings = warnings.concat(plan.warnings);

        var html = '';
        html += '<div class="wf-ai-message">' + htmlEncode(res.messageToUser || plan.messageToUser || '') + '</div>';
        html += '<div class="wf-ai-meta">Modelo: ' + htmlEncode(res.model || '') + ' · Validación: ' + (validation.ok ? 'OK' : 'con errores') + '</div>';

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

        if (actions.length) {
            var canApply = canApplyPlan(plan, validation, missing);
            var disabled = canApply ? '' : ' disabled';
            var hint = canApply
                ? 'Aplicará nodos y aristas en el canvas. Revisar antes de guardar.'
                : 'Para aplicar al canvas, la propuesta no debe tener datos faltantes ni errores de validación.';
            html += '<div class="wf-ai-actions" style="margin-top:8px">';
            html += '<button type="button" class="btn" id="wfAiApply"' + disabled + '>Aplicar al canvas</button>';
            html += '</div>';
            html += '<div class="wf-ai-meta">' + htmlEncode(hint) + '</div>';
        }

        html += renderList('Datos faltantes', missing, '');
        html += renderList('Errores de validación', validation.errors || [], 'wf-ai-error-list');
        html += renderList('Advertencias', warnings, 'wf-ai-warning-list');
        html += '<details class="wf-ai-json"><summary>Ver JSON técnico</summary><pre>' + htmlEncode(JSON.stringify(plan, null, 2)) + '</pre></details>';

        out.innerHTML = html;

        var applyBtn = $('wfAiApply');
        if (applyBtn) applyBtn.addEventListener('click', applyLastPlan);
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

    function humanTaskTitleForPlan(step) {
        step = step || {};
        if (step.destType === 'usuario' || step.user) return 'Enviar a ' + (step.user || 'usuario');
        var role = step.role || 'COMPRAS';
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
            var valueBox = { value: step.value || '' };
            var op = normalizePlanOperator(step.operator || 'not_empty', valueBox);
            var p = {
                field: step.fieldPath || defaultConditionField(),
                op: op
            };
            if (operatorNeedsValue(op)) p.value = valueBox.value || '';
            return makePlanAction('control.if', labelFor('Condición: ' + conditionInfo(step).text), p);
        }

        if (step.type === 'human_task') {
            var title = humanTaskTitleForPlan(step);
            var hp = {
                titulo: title,
                descripcion: 'Tarea generada por el Constructor IA: ' + (step.purpose || 'revisión')
            };
            if (step.destType === 'usuario' || step.user) hp.usuarioAsignado = step.user || firstUser(['OMARD\\OMARD']) || 'OMARD\\OMARD';
            else hp.rol = step.role || firstRole(['COMPRAS']);
            return makePlanAction('human.task', labelFor(title), hp);
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
            return makePlanAction('util.notify', labelFor('Notificar'), {
                tipo: 'sistema',
                canal: 'sistema',
                nivel: 'info',
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
            return makePlanAction('util.logger', labelFor('Registrar evento'), {
                level: step.level || 'Info',
                message: step.message || 'Paso agregado por Constructor IA'
            });
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
            assistantVersion: 'constructor-structured-fix39e',
            intent: 'build_workflow',
            confidence: 1,
            messageToUser: 'Propuesta generada desde el Constructor IA con plan estructurado local.',
            actions: actions,
            missingData: [],
            warnings: hasEndStep()
                ? []
                : ['Se agregó un nodo Fin técnico en la propuesta para evitar un grafo abierto.'],
            branchPlan: {
                planner: 'constructor-local-fix39e',
                hasBranches: branchItems.length > 0,
                branches: branchItems
            },
            proposedConnections: connections
        };
    }

    function buildStructuredGuideResult(userText) {
        var plan = buildStructuredGuidePlan();
        if (!plan) return null;
        return {
            ok: true,
            provider: 'constructor-local',
            model: 'Constructor IA estructurado fix36',
            messageToUser: plan.messageToUser,
            plan: plan,
            validation: buildFunctionalValidation(),
            rawText: userText || ''
        };
    }


    function interpretar() {
        var txt = $('wfAiPrompt');
        var userText = txt ? (txt.value || '').trim() : '';
        if (!userText) {
            setStatus('Escribí primero qué querés construir.', 'warn');
            return;
        }

        var structuredResult = buildStructuredGuideResult(userText);
        if (structuredResult) {
            renderResult(structuredResult);
            setStatus('Propuesta estructurada generada. Revisá y aplicá al canvas cuando esté correcta.', 'ok');
            return;
        }

        setStatus('Interpretando con ML.NET local...', 'busy');
        var btn = $('wfAiRun');
        if (btn) btn.disabled = true;

        fetch('Api/WF_AiAssistant.ashx', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json; charset=utf-8' },
            body: JSON.stringify({
                userText: userText,
                workflowJson: currentWorkflowJson()
            })
        })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                renderResult(res);
                if (res.ok) setStatus('Propuesta recibida. Revisá y aplicá al canvas cuando esté correcta.', 'ok');
                else setStatus('No se pudo obtener una propuesta válida.', 'error');
            })
            .catch(function (err) {
                renderResult({ ok: false, error: err.message || String(err) });
                setStatus('Error llamando al Asistente IA.', 'error');
            })
            .finally(function () {
                if (btn) btn.disabled = false;
            });
    }

    function init() {
        ensureCollapsedLauncher();
        ensureWideModeUi();
        ensureGuideUi();
        startAssistantInspectorWatcher();

        var btn = $('wfAiRun');
        if (btn) btn.addEventListener('click', interpretar);

        var clear = $('wfAiClear');
        if (clear) clear.addEventListener('click', function () {
            var t = $('wfAiPrompt');
            var r = $('wfAiResult');
            if (t) t.value = '';
            if (r) r.innerHTML = '';
            lastPlan = null;
            setStatus('', '');
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
