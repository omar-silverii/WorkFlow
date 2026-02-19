/* Scripts/workflow.templates.js
  Plantillas mínimas para nodos del catálogo.
  - No inventa runtime: solo provee defaults para el inspector.
*/
(function (global) {
    function merge(key, obj) {
        global.PARAM_TEMPLATES = global.PARAM_TEMPLATES || {};
        global.PARAM_TEMPLATES[key] = Object.assign({}, global.PARAM_TEMPLATES[key] || {}, obj || {});
    }

    function mergeGroup(groupKey, obj) {
        global.PARAM_TEMPLATES = global.PARAM_TEMPLATES || {};
        global.PARAM_TEMPLATES[groupKey] = Object.assign({}, global.PARAM_TEMPLATES[groupKey] || {}, obj || {});
    }

    // --- START/END/LOGGER
    merge('util.start', {});
    merge('util.end', {});
    merge('util.logger', { message: 'Hola! ${wf.instanceId}', level: 'Info' });

    // --- HTTP
    merge('http.request', {
        method: 'GET',
        url: 'https://api.example.com/status',
        headers: { 'Accept': 'application/json' },
        outputPrefix: 'payload'
    });

    // --- SQL
    merge('data.sql', {
        connectionStringName: 'DefaultConnection',
        query: 'SELECT 1 AS ok;',
        outputPrefix: 'sql'
    });

    // --- FILE READ/WRITE
    merge('file.read', { path: 'C:\\\\temp\\\\input.json', asJson: true, output: 'input' });
    merge('file.write', { path: 'C:\\\\temp\\\\out.txt', text: 'Hola', append: false });

    // --- IF ---
    merge('control.if', { field: 'payload.status', op: '==', value: '200' });

    mergeGroup('control.if.templates', {
        // =========================
        // HTTP / INTEGRACIONES
        // =========================
        http_status_200: {
            label: 'HTTP: status == 200',
            field: 'payload.status',
            op: '==',
            value: '200'
        },

        http_status_2xx: {
            label: 'HTTP: status entre 200 y 299',
            field: 'payload.status',
            op: '>=',
            value: '200'
            // Nota: para 2xx completo se usa otro IF: status <= 299
            // (preferimos mantener plantillas simples)
        },

        http_status_le_299: {
            label: 'HTTP: status <= 299',
            field: 'payload.status',
            op: '<=',
            value: '299'
        },

        // =========================
        // SQL / CONSULTAS
        // =========================
        sql_rows_gt_0: {
            label: 'SQL: hay registros (rows > 0)',
            field: 'sql.rows',
            op: '>',
            value: '0'
        },

        // =========================
        // EXISTENCIA / VACÍO
        // =========================
        campo_existe: {
            label: 'Campo existe',
            field: 'payload.campo',
            op: 'exists'
        },

        campo_no_existe: {
            label: 'Campo NO existe',
            field: 'payload.campo',
            op: 'not_exists'
        },

        campo_vacio: {
            label: 'Campo vacío',
            field: 'payload.campo',
            op: 'empty'
        },

        campo_no_vacio: {
            label: 'Campo NO vacío',
            field: 'payload.campo',
            op: 'not_empty'
        },

        // =========================
        // TEXTO (CON TRANSFORM)
        // =========================
        texto_igual_normalizado: {
            label: 'Texto igual (trim)',
            field: 'payload.texto',
            transform: 'trim',
            op: '==',
            value: 'VALOR'
        },

        texto_contiene_ci: {
            label: 'Texto contiene (sin distinguir mayúsculas)',
            field: 'payload.texto',
            transform: 'lower',
            op: 'contains',
            value: 'valor'
        },

        texto_no_contiene_ci: {
            label: 'Texto NO contiene (sin distinguir mayúsculas)',
            field: 'payload.texto',
            transform: 'lower',
            op: 'not_contains',
            value: 'valor'
        },

        texto_empieza_con_ci: {
            label: 'Texto empieza con (sin distinguir mayúsculas)',
            field: 'payload.texto',
            transform: 'lower',
            op: 'starts_with',
            value: 'prefijo'
        },

        texto_termina_con_ci: {
            label: 'Texto termina con (sin distinguir mayúsculas)',
            field: 'payload.texto',
            transform: 'lower',
            op: 'ends_with',
            value: 'sufijo'
        },

        // =========================
        // NUMÉRICOS (GENÉRICOS)
        // =========================
        numero_mayor_igual: {
            label: 'Número mayor o igual (>=)',
            field: 'payload.numero',
            op: '>=',
            value: '0'
        },

        numero_menor_igual: {
            label: 'Número menor o igual (<=)',
            field: 'payload.numero',
            op: '<=',
            value: '0'
        },

        numero_mayor: {
            label: 'Número mayor (>)',
            field: 'payload.numero',
            op: '>',
            value: '0'
        },

        numero_menor: {
            label: 'Número menor (<)',
            field: 'payload.numero',
            op: '<',
            value: '0'
        }
    });



    // --- SWITCH
    merge('control.switch', { expr: '${payload.tipo}', cases: ['A', 'B'], defaultLabel: 'Otro' });

    // --- DELAY
    merge('control.delay', { ms: 2000, label: 'Pausa' });

    // --- RETRY
    merge('control.retry', { maxAttempts: 3, delayMs: 1000 });

    // --- LOOP
    merge('control.loop', { itemsPath: 'input.items', itemVar: 'item', indexVar: 'i' });

    // --- DOC.ENTRADA / DOC.ATTACH / DOC.SEARCH / DOC.LOAD (defaults mínimos)
    merge('doc.entrada', { output: 'biz.case.rootDoc' });
    merge('doc.attach', { docPath: 'biz.case.rootDoc', output: 'biz.case.attachments' });
    merge('doc.search', { output: 'biz.doc.search', criteria: { Numero: 'OC-0001' } });
    merge('doc.load', { path: 'C:\\\\temp\\\\archivo.pdf', mode: 'auto', outputPrefix: 'payload' });

    // --- STATE VARS
    merge('state.vars', { set: { 'biz.demo.flag': true } });

})(window);
