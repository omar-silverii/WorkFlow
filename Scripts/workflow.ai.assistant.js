// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// fix24c: corrige recursión al agregar IF; mantiene constructor guiado, catálogo de campos y operadores por tipo.
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
        var fallbackFields = [
            { path: 'wf.instanceId', label: 'ID de instancia' },
            { path: 'input.filePath', label: 'Ruta de archivo' },
            { path: 'input.text', label: 'Texto extraído' },
            { path: 'input.hasText', label: 'Tiene texto' }
        ];

        guideCatalog.roles = fallbackRoles;
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
        if (step.type === 'human_task') return 'Tarea humana: ' + (step.role || '') + ' / ' + (step.purpose || 'revisión');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
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

    function branchPrefix(branch, ctx) {
        branch = String(branch || 'then');
        if (branch === 'if_cond_true' && ctx.lastCondition) return ctx.lastCondition.trueLead + ' ';
        if (branch === 'if_cond_false' && ctx.lastCondition) return ctx.lastCondition.falseLead + ' ';
        if (branch === 'if_task_ok' && ctx.lastTaskRole) return 'si el rol ' + ctx.lastTaskRole + ' aprueba ';
        if (branch === 'if_task_reject' && ctx.lastTaskRole) return 'si el rol ' + ctx.lastTaskRole + ' rechaza ';
        return '';
    }

    function branchOptionsHtml(selectedValue) {
        var sel = selectedValue || 'then';
        var items = [
            ['then', 'Luego / paso normal'],
            ['if_cond_true', 'Si cumple la última condición'],
            ['if_cond_false', 'Si NO cumple la última condición'],
            ['if_task_ok', 'Si aprueba la última tarea'],
            ['if_task_reject', 'Si rechaza la última tarea']
        ];
        return items.map(function (x) {
            return '<option value="' + x[0] + '"' + (x[0] === sel ? ' selected' : '') + '>' + htmlEncode(x[1]) + '</option>';
        }).join('');
    }

    function createStepTitle(step) {
        if (!step) return '';
        if (step.type === 'doc_load') return 'Cargar documento: ' + docPhrase(step.docTipo || '');
        if (step.type === 'condition') return 'Condición: ' + conditionInfo(step).text;
        if (step.type === 'human_task') return 'Tarea humana: ' + (step.role || '') + ' / ' + (step.purpose || 'revisión');
        if (step.type === 'email_send') return 'Enviar correo: ' + (step.to || '(sin destinatario)');
        if (step.type === 'notify') return 'Notificar: ' + (step.destType === 'usuario' ? (step.user || '(usuario)') : (step.role || '(rol)'));
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
            if (step.fieldPath) {
                var phrase = 'validar el campo ' + step.fieldPath + ' con operador ' + (step.operator || 'not_empty');
                if (operatorNeedsValue(step.operator) && String(step.value || '').trim()) phrase += ' valor ' + String(step.value || '').trim();
                return phrase;
            }
            return info.text;
        }
        if (step.type === 'human_task') {
            var role = step.role || 'COMPRAS';
            var purpose = step.purpose || 'revisión';
            ctx.lastTaskRole = role;
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
        var ctx = { lastCondition: null, lastTaskRole: null };

        guideSteps.forEach(function (step) {
            var prefix = branchPrefix(step.branch, ctx);
            var body = stepBody(step, ctx);

            // Separador importante para el intérprete ML.NET:
            // sin "luego", un cuerpo de email/notificación puede absorber el texto
            // del paso siguiente. Ej.: cuerpo ... notificar internamente ...
            if (!prefix && parts.length > 1) prefix = 'luego ';

            if (body) parts.push(prefix + body);
        });

        return parts.join(', ') + '.';
    }

    function hasEndStep() {
        return guideSteps.some(function (x) { return x.type === 'end'; });
    }

    function updatePromptFromSteps(runAfter) {
        var prompt = $('wfAiPrompt');
        if (!prompt) return;
        if (!guideSteps.length) {
            setStatus('Agregá al menos un paso al constructor.', 'warn');
            return;
        }
        prompt.value = buildIncrementalPhrase();
        var msg = runAfter ? 'Frase generada. Interpretando...' : 'Frase generada. Revisala y presioná Interpretar.';
        if (!hasEndStep()) msg += ' Sugerencia: agregá un paso Finalizar.';
        setStatus(msg, hasEndStep() ? 'ok' : 'warn');
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
                '<div class="wf-ai-guide-row" id="wfAiStepValueRow"><label>Valor</label><input id="wfAiStepConditionValue" class="wf-ai-input" placeholder="Ej.: 100000, texto, fecha o campo" /></div>';
        }
        if (type === 'human_task') {
            return '' +
                '<div class="wf-ai-guide-row"><label>Cuándo</label><select id="wfAiStepBranch" class="wf-ai-select">' + branchOptionsHtml('then') + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Rol destino</label><select id="wfAiStepRole" class="wf-ai-select">' + roleOptions(firstRole(['COMPRAS'])) + '</select></div>' +
                '<div class="wf-ai-guide-row"><label>Qué hace</label><input id="wfAiStepPurpose" class="wf-ai-input" value="revisión" placeholder="Ej.: revisar, aprobar, corregir, cargar datos" /></div>' +
                '<div class="wf-ai-guide-note">Para definir qué pasa si aprueba o rechaza, agregá luego otro paso usando “Cuándo”: Si aprueba la última tarea / Si rechaza la última tarea.</div>';
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
                '<div class="wf-ai-guide-row" id="wfAiStepUserRow" style="display:none"><label>Usuario</label><input id="wfAiStepUser" class="wf-ai-input" value="OMARD\\OMARD" /></div>' +
                '<div class="wf-ai-guide-row"><label>Asunto</label><input id="wfAiStepTitle" class="wf-ai-input" value="Aviso interno" /></div>' +
                '<div class="wf-ai-guide-row"><label>Mensaje</label><input id="wfAiStepMessage" class="wf-ai-input" value="Hay una novedad pendiente en el workflow" /></div>';
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
            return '<div class="wf-ai-guide-row"><label>Milisegundos</label><input id="wfAiStepMs" class="wf-ai-input" value="1000" /></div>';
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
            setControlValue('wfAiStepRole', step.role || firstRole(['COMPRAS']));
            setControlValue('wfAiStepPurpose', step.purpose || 'revisión');
        } else if (step.type === 'email_send') {
            setControlValue('wfAiStepTo', step.to || 'destinatario@empresa.com');
            setControlValue('wfAiStepSubject', step.subject || 'Aviso Workflow Studio');
            setControlValue('wfAiStepBody', step.body || 'Se generó un aviso desde Workflow Studio');
        } else if (step.type === 'notify') {
            setControlValue('wfAiStepDestType', step.destType || 'rol');
            setControlValue('wfAiStepRole', step.role || firstRole(['COMPRAS']));
            setControlValue('wfAiStepUser', step.user || 'OMARD\OMARD');
            setControlValue('wfAiStepTitle', step.title || 'Aviso interno');
            setControlValue('wfAiStepMessage', step.message || 'Hay una novedad pendiente en el workflow');
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

        syncDynamicStepFields();
        updateGuideEditMode();
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
            step.role = selectedText('wfAiStepRole') || firstRole(['COMPRAS']);
            step.purpose = selectedText('wfAiStepPurpose') || 'revisión';
        } else if (type === 'email_send') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.to = selectedText('wfAiStepTo') || 'destinatario@empresa.com';
            step.subject = selectedText('wfAiStepSubject') || 'Aviso Workflow Studio';
            step.body = selectedText('wfAiStepBody') || 'Se generó un aviso desde Workflow Studio';
        } else if (type === 'notify') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.destType = selectedText('wfAiStepDestType') || 'rol';
            step.role = selectedText('wfAiStepRole') || firstRole(['COMPRAS']);
            step.user = selectedText('wfAiStepUser') || 'OMARD\\OMARD';
            step.title = selectedText('wfAiStepTitle') || 'Aviso interno';
            step.message = selectedText('wfAiStepMessage') || 'Hay una novedad pendiente en el workflow';
        } else if (type === 'state_set') {
            step.key = selectedText('wfAiStepKey') || 'wf.variable';
            step.value = selectedText('wfAiStepValue') || 'valor';
        } else if (type === 'state_remove') {
            step.key = selectedText('wfAiStepKey') || 'wf.variable';
        } else if (type === 'delay') {
            step.ms = selectedText('wfAiStepMs') || '1000';
        } else if (type === 'logger') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
            step.message = selectedText('wfAiStepMessage') || 'Paso agregado por Asistente IA';
        } else if (type === 'end') {
            step.branch = selectedText('wfAiStepBranch') || 'then';
        }

        return step;
    }

    function renderGuideSteps() {
        var box = $('wfAiGuideSteps');
        if (!box) return;

        if (!guideSteps.length) {
            box.innerHTML = '<div class="wf-ai-guide-empty">Todavía no agregaste pasos. Elegí una acción y presioná “Agregar paso”.</div>';
            renderAvailableFields();
            return;
        }

        var html = '<ol class="wf-ai-step-list">';
        guideSteps.forEach(function (step, idx) {
            var isEditing = idx === editingStepIndex;
            html += '<li class="wf-ai-step-item' + (isEditing ? ' is-editing' : '') + '" data-guide-action="edit" data-step-index="' + idx + '">';
            html += '<div class="wf-ai-step-title">' + htmlEncode(createStepTitle(step)) + (isEditing ? ' <span class="wf-ai-step-editing">editando</span>' : '') + '</div>';
            html += '<div class="wf-ai-step-tools">';
            html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="up" data-step-index="' + idx + '">↑</button>';
            html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="down" data-step-index="' + idx + '">↓</button>';
            html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="edit" data-step-index="' + idx + '">Modificar</button>';
            html += '<button type="button" class="btn wf-ai-mini-btn" data-guide-action="delete" data-step-index="' + idx + '">Quitar</button>';
            html += '</div>';
            html += '</li>';
        });
        html += '</ol>';
        if (!hasEndStep()) html += '<div class="wf-ai-guide-warn">Sugerencia: agregá “Finalizar flujo” como último paso.</div>';
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

    function interpretar() {
        var txt = $('wfAiPrompt');
        var userText = txt ? (txt.value || '').trim() : '';
        if (!userText) {
            setStatus('Escribí primero qué querés construir.', 'warn');
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
