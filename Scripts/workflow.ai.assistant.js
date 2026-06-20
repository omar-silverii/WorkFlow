// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// fix28: constructor IA con nodo Consulta SQL sobre data.sql existente.
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
        fields: [],
        loaded: false
    };

    var guideSteps = [];
    var guideSeq = 1;
    var editingStepIndex = -1;

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

        Promise.all([pRoles, pDocs, pCatalog]).then(function () {
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
        if (step.type === 'state_set') return 'Guardar variable: ' + (step.key || '');
        if (step.type === 'state_remove') return 'Quitar variable: ' + (step.key || '');
        if (step.type === 'delay') return 'Demora: ' + (step.ms || '1000') + ' ms';
        if (step.type === 'logger') return 'Registrar log';
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
            } else if (step.type === 'state_set') {
                addAvailableField(fields, source, step.key || 'wf.variable', 'Variable guardada', inferFieldType(step.key, step.key));
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
        html += '<div class="wf-ai-fields-help">Se alimenta con la entrada del workflow y con las salidas de los pasos agregados. Se usa para elegir campos en el IF guiado.</div>';
        html += '<div class="wf-ai-fields-list">';
        var lastSource = null;
        fields.forEach(function (f) {
            if (f.source !== lastSource) {
                if (lastSource !== null) html += '</div>';
                lastSource = f.source;
                html += '<div class="wf-ai-field-group"><div class="wf-ai-field-source">' + htmlEncode(lastSource) + '</div>';
            }
            html += '<div class="wf-ai-field-row" title="' + htmlEncode(f.path) + '">';
            html += '<div><strong>' + htmlEncode(f.label || f.path) + '</strong><br><code>' + htmlEncode(f.path) + '</code></div>';
            html += '<span class="wf-ai-field-type">' + htmlEncode(f.type || '') + '</span>';
            html += '</div>';
        });
        if (lastSource !== null) html += '</div>';
        html += '</div>';
        box.innerHTML = html;
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

    function createStepTitle(step) {
        if (!step) return '';
        if (step.type === 'doc_load') return 'Cargar documento: ' + docPhrase(step.docTipo || '');
        if (step.type === 'condition') return 'Condición: ' + conditionInfo(step).text;
        if (step.type === 'human_task') return 'Tarea humana: ' + ((step.destType === 'usuario' || step.user) ? (step.user || '(usuario)') : (step.role || '')) + ' / ' + (step.purpose || 'revisión');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
        if (step.type === 'http_request') return 'Solicitud HTTP: ' + ((step.method || 'GET') + ' ' + (step.url || '(sin URL)'));
        if (step.type === 'sql_query') return 'Consulta SQL: ' + sqlShortText(step.query || '');
        if (step.type === 'state_set') return 'Guardar variable: ' + (step.key || '');
        if (step.type === 'state_remove') return 'Quitar variable: ' + (step.key || '');
        if (step.type === 'delay') return 'Demora: ' + (step.ms || '1000') + ' ms';
        if (step.type === 'logger') return 'Registrar log';
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
        if (step.type === 'state_set') {
            return 'guardar variable ' + (step.key || 'wf.variable') + ' con valor ' + (step.value || 'valor');
        }
        if (step.type === 'state_remove') {
            return 'quitar variable ' + (step.key || 'wf.variable');
        }
        if (step.type === 'delay') {
            return 'esperar ' + (step.ms || '1000') + ' ms';
        }
        if (step.type === 'logger') {
            return step.message ? 'registrar un log con mensaje ' + step.message : 'registrar un log';
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
                '<div class="wf-ai-guide-note">Después agregá pasos en la rama SI o NO usando el campo “Cuándo” de cada paso.</div>';
        }
        if (type === 'human_task') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Destino</label><select id="wfAiStepTaskDestType" class="wf-ai-select"><option value="rol">Rol</option><option value="usuario">Usuario</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskRoleRow"><label>Rol</label><select id="wfAiStepRole" class="wf-ai-select">' + roleOptions(firstRole(['COMPRAS'])) + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepTaskUserRow" style="display:none"><label>Usuario</label>' + userInputHtml('wfAiStepTaskUser', firstUser(['OMARD\\OMARD'])) + '</div>' +
                '<div class="wf-ai-guide-row"><label>Qué hace</label><input id="wfAiStepPurpose" class="wf-ai-input" value="revisión" placeholder="Ej.: revisar, aprobar, corregir, cargar datos" /></div>' +
                '<div class="wf-ai-guide-note">Después podés agregar pasos según el resultado humano usando “Cuándo”: Resultado APROBADO/APTO o RECHAZADO/NO APTO de la última tarea humana.</div>';
        }
        if (type === 'email_send') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Para</label><input id="wfAiStepTo" class="wf-ai-input" placeholder="usuario@empresa.com" /></div>' +
                '<div class="wf-ai-guide-row"><label>Asunto</label><input id="wfAiStepSubject" class="wf-ai-input" value="Aviso Workflow Studio" /></div>' +
                '<div class="wf-ai-guide-row"><label>Cuerpo</label><input id="wfAiStepBody" class="wf-ai-input" value="Se generó un aviso desde Workflow Studio" /></div>';
        }
        if (type === 'notify') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Destino</label><select id="wfAiStepDestType" class="wf-ai-select"><option value="rol">Rol</option><option value="usuario">Usuario</option></select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepRoleRow"><label>Rol</label><select id="wfAiStepRole" class="wf-ai-select">' + roleOptions(firstRole(['COMPRAS'])) + '</select></div>' +
                '<div class="wf-ai-guide-row" id="wfAiStepUserRow" style="display:none"><label>Usuario</label>' + userInputHtml('wfAiStepUser', firstUser(['OMARD\\OMARD'])) + '</div>' +
                '<div class="wf-ai-guide-row"><label>Asunto</label><input id="wfAiStepTitle" class="wf-ai-input" value="Aviso interno" /></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje</label><input id="wfAiStepMessage" class="wf-ai-input" value="Hay una novedad pendiente en el workflow" /></div>';
        }
        if (type === 'http_request') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
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
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Conexión</label><input id="wfAiStepSqlConnection" class="wf-ai-input" value="DefaultConnection" /></div>' +
                '<div class="wf-ai-guide-row"><label>SQL</label><textarea id="wfAiStepSqlQuery" class="wf-ai-input wf-ai-textarea" placeholder="Ej.: SELECT 1"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Parámetros JSON</label><textarea id="wfAiStepSqlParams" class="wf-ai-input wf-ai-textarea" placeholder="Opcional. Ej.: {&quot;Id&quot;:150484}"></textarea></div>' +
                '<div class="wf-ai-guide-row"><label>Máx. filas a guardar</label><input id="wfAiStepSqlMaxRows" class="wf-ai-input" value="100" /></div>' +
                '<div class="wf-ai-guide-note">Usa el nodo data.sql existente. En SELECT deja visibles sql.rows, sql.rowCount, sql.first y sql.scalar en Datos de la instancia.</div>';
        }
        if (type === 'state_set') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Variable</label><input id="wfAiStepKey" class="wf-ai-input" value="wf.variable" /></div>' +
                '<div class="wf-ai-guide-row"><label>Valor</label><input id="wfAiStepValue" class="wf-ai-input" value="valor" /></div>';
        }
        if (type === 'state_remove') {
            return '<div class="wf-ai-guide-row"><label>Variable</label><input id="wfAiStepKey" class="wf-ai-input" value="wf.variable" /></div>';
        }
        if (type === 'delay') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Milisegundos</label><input id="wfAiStepMs" class="wf-ai-input" value="1000" /></div>';
        }
        if (type === 'logger') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje</label><input id="wfAiStepMessage" class="wf-ai-input" value="Paso agregado por Asistente IA" /></div>';
        }
        if (type === 'end') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-note">Agrega el cierre del flujo. Normalmente conviene dejarlo como último paso.</div>';
        }
        return '';
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
        } else if (step.type === 'state_set') {
            setControlValue('wfAiStepKey', step.key || 'wf.variable');
            setControlValue('wfAiStepValue', step.value || 'valor');
        } else if (step.type === 'state_remove') {
            setControlValue('wfAiStepKey', step.key || 'wf.variable');
        } else if (step.type === 'delay') {
            setControlValue('wfAiStepMs', step.ms || '1000');
        } else if (step.type === 'logger') {
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

        syncDynamicStepFields();
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
        } else if (type === 'state_set') {
            step.key = selectedText('wfAiStepKey') || 'wf.variable';
            step.value = selectedText('wfAiStepValue') || 'valor';
        } else if (type === 'state_remove') {
            step.key = selectedText('wfAiStepKey') || 'wf.variable';
        } else if (type === 'delay') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.ms = selectedText('wfAiStepMs') || '1000';
        } else if (type === 'logger') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
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

    function renderConditionBranches(conditionIndex, branchMap) {
        var groups = branchMap[conditionIndex] || { si: [], no: [] };
        var html = '<div class="wf-ai-if-branches">';

        html += '<div class="wf-ai-if-branch wf-ai-if-branch-yes"><div class="wf-ai-if-branch-title">SI cumple</div>';
        if (groups.si.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.si.forEach(function (item) { html += renderSingleGuideStep(item.step, item.idx, 'wf-ai-step-branch'); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para esta rama.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-if-branch wf-ai-if-branch-no"><div class="wf-ai-if-branch-title">NO cumple</div>';
        if (groups.no.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.no.forEach(function (item) { html += renderSingleGuideStep(item.step, item.idx, 'wf-ai-step-branch'); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para esta rama.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-if-branch-help">Para agregar pasos acá, elegí una acción y en “Cuándo” seleccioná Rama SI o Rama NO de la última condición.</div>';
        html += '</div>';
        return html;
    }


    function renderTaskResultBranches(taskIndex, branchMap) {
        var groups = branchMap[taskIndex] || { ok: [], reject: [] };
        var html = '<div class="wf-ai-task-branches">';

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-ok"><div class="wf-ai-task-branch-title">APROBADO / APTO</div>';
        if (groups.ok.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.ok.forEach(function (item) { html += renderSingleGuideStep(item.step, item.idx, 'wf-ai-step-branch'); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch wf-ai-task-branch-reject"><div class="wf-ai-task-branch-title">RECHAZADO / NO APTO</div>';
        if (groups.reject.length) {
            html += '<ol class="wf-ai-branch-step-list">';
            groups.reject.forEach(function (item) { html += renderSingleGuideStep(item.step, item.idx, 'wf-ai-step-branch'); });
            html += '</ol>';
        } else {
            html += '<div class="wf-ai-if-branch-empty">Todavía no agregaste pasos para este resultado.</div>';
        }
        html += '</div>';

        html += '<div class="wf-ai-task-branch-help">Estas ramas son decisiones humanas: se evalúan después de cerrar la tarea. Operativamente se usa el resultado <strong>apto</strong> para aprobado y <strong>no_apto</strong> para no aprobado; el resultado técnico <strong>rechazado</strong> sigue reservado para volver a una etapa anterior.</div>';
        html += '</div>';
        return html;
    }

    function renderGuideSteps() {
        var box = $('wfAiGuideSteps');
        if (!box) return;

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
            html += renderSingleGuideStep(step, idx, '');
            if (step && step.type === 'condition') html += renderConditionBranches(idx, branchMap);
            if (step && step.type === 'human_task') html += renderTaskResultBranches(idx, taskBranchMap);
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

        renderAvailableFields();
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
        guideSteps.push(step);
        renderGuideSteps();
        updateGuideEditMode();
        updatePromptFromSteps(false);
    }

    function clearGuideSteps() {
        guideSteps = [];
        editingStepIndex = -1;
        renderGuideSteps();
        renderAvailableFields();
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
            '      <div class="wf-ai-guide-note">Elegí qué querés agregar ahora. El orden lo decidís vos; después el sistema arma una frase clara para el Asistente IA actual.</div>' +
            '      <div id="wfAiGuideSteps" class="wf-ai-guide-steps"></div>' +
            '      <div id="wfAiAvailableFields" class="wf-ai-available-fields"></div>' +
            '    </div>' +
            '    <div class="wf-ai-guide-right">' +
            '      <div class="wf-ai-guide-row">' +
            '        <label>Agregar paso</label>' +
            '        <select id="wfAiStepType" class="wf-ai-select">' +
            '          <option value="doc_load">Cargar documento</option>' +
            '          <option value="condition">Validar condición</option>' +
            '          <option value="human_task">Mandar tarea humana</option>' +
            '          <option value="http_request">Solicitud HTTP</option>' +
            '          <option value="sql_query">Consulta SQL</option>' +
            '          <option value="email_send">Enviar correo</option>' +
            '          <option value="notify">Notificación interna</option>' +
            '          <option value="state_set">Guardar variable</option>' +
            '          <option value="state_remove">Quitar variable</option>' +
            '          <option value="delay">Esperar / demora</option>' +
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
        updateGuideEditMode();
        loadGuideCatalogs();
    }

    // ------------------------------------------------------------
    // Resultado del Asistente IA y aplicación al canvas
    // ------------------------------------------------------------
    function ensureCollapsedLauncher() {
        var panel = $('wfAiPanel');
        if (!panel || !panel.parentNode) return null;

        var existing = $('wfAiCollapsed');
        if (existing) return existing;

        var box = document.createElement('div');
        box.id = 'wfAiCollapsed';
        box.className = 'wf-ai-block';
        box.style.display = 'none';
        box.style.margin = '10px 0';
        box.innerHTML =
            '<div id="wfAiCollapsedMsg" class="wf-ai-meta"></div>' +
            '<button type="button" class="btn" id="wfAiShow">Mostrar Asistente IA</button>';

        panel.parentNode.insertBefore(box, panel);

        var btn = $('wfAiShow');
        if (btn) {
            btn.addEventListener('click', function () {
                panel.style.display = '';
                box.style.display = 'none';
                var prompt = $('wfAiPrompt');
                if (prompt) prompt.focus();
            });
        }

        return box;
    }

    function hideAssistantAfterApply(message) {
        var panel = $('wfAiPanel');
        if (!panel) return;

        var result = $('wfAiResult');
        if (result) result.innerHTML = '';
        setStatus('', '');
        lastPlan = null;

        var collapsed = ensureCollapsedLauncher();
        var msg = $('wfAiCollapsedMsg');
        if (msg) msg.textContent = message || 'Propuesta aplicada al canvas. Revisá el grafo antes de guardar.';

        panel.style.display = 'none';
        if (collapsed) collapsed.style.display = '';
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

        if (step.type === 'state_set') {
            var set = {};
            set[step.key || 'wf.variable'] = step.value || 'valor';
            return makePlanAction('state.vars', labelFor('Definir variables'), { set: set });
        }

        if (step.type === 'state_remove') {
            return makePlanAction('state.vars', labelFor('Quitar variable'), { remove: [step.key || 'wf.variable'] });
        }

        if (step.type === 'delay') {
            var ms = parseInt(step.ms || '1000', 10);
            return makePlanAction('control.delay', labelFor('Demora'), {
                message: 'Demora agregada por Constructor IA',
                ms: isNaN(ms) || ms <= 0 ? 1000 : ms
            });
        }

        if (step.type === 'logger') {
            return makePlanAction('util.logger', labelFor('Registrar evento'), {
                level: 'Info',
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

        guideSteps.forEach(function (step, idx) {
            if (!step) return;
            if (isBranchChildStep(step)) return;

            var action = actionForGuideStep(step, labelFor);
            if (action) {
                actions.push(action);
                actionByStepId[step.id] = action;
            }

            if (step.type === 'human_task' && maps.tasks[idx] && (maps.tasks[idx].ok.length || maps.tasks[idx].reject.length)) {
                var resultIf = taskResultIfActionForPlan(step, labelFor);
                actions.push(resultIf);
                taskResultIfByStepId[step.id] = resultIf;
            }

            var childIndexes = [];
            if (step.type === 'condition' && maps.conditions[idx]) childIndexes = maps.conditions[idx].si.concat(maps.conditions[idx].no);
            if (step.type === 'human_task' && maps.tasks[idx]) childIndexes = maps.tasks[idx].ok.concat(maps.tasks[idx].reject);
            childIndexes.forEach(function (childIdx) {
                var child = guideSteps[childIdx];
                if (!child) return;
                var childAction = actionForGuideStep(child, labelFor);
                if (childAction) {
                    actions.push(childAction);
                    actionByStepId[child.id] = childAction;
                }
            });
        });

        var endAction = null;
        actions.forEach(function (a) {
            if (!endAction && actionNodeTypeForPlan(a) === 'util.end') endAction = a;
        });
        if (!endAction) {
            endAction = makePlanAction('util.end', labelFor('Fin'), {});
            actions.push(endAction);
        }

        var mainIndexes = [];
        guideSteps.forEach(function (step, idx) {
            if (!step || isBranchChildStep(step)) return;
            mainIndexes.push(idx);
        });

        function nextMainActionAfter(mainPos) {
            for (var i = mainPos + 1; i < mainIndexes.length; i++) {
                var st = guideSteps[mainIndexes[i]];
                if (st && actionByStepId[st.id]) return actionByStepId[st.id];
            }
            return endAction;
        }

        var connections = [];
        if (mainIndexes.length) {
            var firstMain = guideSteps[mainIndexes[0]];
            addStructuredConnection(connections, startAction, actionByStepId[firstMain.id], '');
        } else {
            addStructuredConnection(connections, startAction, endAction, '');
        }

        mainIndexes.forEach(function (idx, mainPos) {
            var step = guideSteps[idx];
            if (!step || !actionByStepId[step.id]) return;
            var current = actionByStepId[step.id];
            if (actionNodeTypeForPlan(current) === 'util.end') return;

            var merge = nextMainActionAfter(mainPos);
            if (merge === current) merge = endAction;

            if (step.type === 'condition' && maps.conditions[idx] && (maps.conditions[idx].si.length || maps.conditions[idx].no.length)) {
                connectBranchSteps(connections, current, maps.conditions[idx].si, 'SI', merge, actionByStepId);
                connectBranchSteps(connections, current, maps.conditions[idx].no, 'NO', merge, actionByStepId);
                return;
            }

            if (step.type === 'human_task' && taskResultIfByStepId[step.id]) {
                var resultIf = taskResultIfByStepId[step.id];
                addStructuredConnection(connections, current, resultIf, '');
                connectBranchSteps(connections, resultIf, maps.tasks[idx].ok, 'SI', merge, actionByStepId);
                connectBranchSteps(connections, resultIf, maps.tasks[idx].reject, 'NO', merge, actionByStepId);
                return;
            }

            addStructuredConnection(connections, current, merge, '');
        });

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
            assistantVersion: 'constructor-structured-fix27',
            intent: 'build_workflow',
            confidence: 1,
            messageToUser: 'Propuesta generada desde el Constructor IA con plan estructurado local.',
            actions: actions,
            missingData: [],
            warnings: hasEndStep()
                ? ['fix28: plan estructurado local activo. Consulta SQL usa data.sql/ManejadorSql existente y no toca el motor.']
                : ['Se agregó un nodo Fin técnico en la propuesta para evitar un grafo abierto.', 'fix28: plan estructurado local activo. Consulta SQL usa data.sql/ManejadorSql existente y no toca el motor.'],
            branchPlan: {
                planner: 'constructor-local-fix27',
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
            model: 'Constructor IA estructurado fix27',
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
