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
        status_200: { label: 'status == 200', field: 'payload.status', op: '==', value: '200' },
        sql_rows_gt_0: { label: 'sql.rows > 0', field: 'sql.rows', op: '>', value: '0' },
        has_payload_nro: { label: 'Existe payload.data.nro', field: 'payload.data.nro', op: 'exists' },
        equals_ok: { label: 'payload.result == "OK"', field: 'payload.result', op: '==', value: 'OK' },
        score_ge_700: { label: 'payload.score >= 700', field: 'payload.score', op: '>=', value: '700' },
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
