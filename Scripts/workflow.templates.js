; (() => {
    // NO reasignamos window.PARAM_TEMPLATES; siempre mutamos el objeto existente.
    const PT = (window.PARAM_TEMPLATES = window.PARAM_TEMPLATES || {});
    const merge = (key, obj) => (PT[key] = Object.assign({}, obj, PT[key] || {}));
    const mergeGroup = (key, obj) => (PT[key] = Object.assign({}, PT[key] || {}, obj));

    // --- HTTP ---
    const HTTP_DEFAULTS = {
        timeoutMs: 8000,
        failOnStatus: false,
        failStatusMin: 400
    };

    merge('http.request', Object.assign({
        url: '/Api/Ping.ashx',
        method: 'GET',
        headers: {},
        query: {},
        body: null,
        contentType: ''
    }, HTTP_DEFAULTS));

    mergeGroup('http.request.templates', {
        ping_get: Object.assign({}, HTTP_DEFAULTS, {
            label: 'GET: /Api/Ping.ashx',
            url: '/Api/Ping.ashx',
            method: 'GET',
            headers: {},
            query: {},
            body: null,
            contentType: ''
        }),

        get_con_query: Object.assign({}, HTTP_DEFAULTS, {
            label: 'GET con query: ?clienteId',
            url: '/Api/Score.ashx',
            method: 'GET',
            headers: {},
            query: { clienteId: '${solicitud.clienteId}' },
            body: null,
            contentType: ''
        }),

        post_json: Object.assign({}, HTTP_DEFAULTS, {
            label: 'POST JSON',
            url: '/api/demo',
            method: 'POST',
            headers: {},
            query: {},
            body: { nro: '${payload.data.nro}', importe: '${payload.data.prima}' },
            contentType: 'application/json'
        }),

        put_json: Object.assign({}, HTTP_DEFAULTS, {
            label: 'PUT JSON',
            url: '/api/demo',
            method: 'PUT',
            headers: {},
            query: {},
            body: { id: '${payload.id}', valor: 'actualizado' },
            contentType: 'application/json'
        }),

        delete_simple: Object.assign({}, HTTP_DEFAULTS, {
            label: 'DELETE (simple)',
            url: '/api/demo/${payload.id}',
            method: 'DELETE',
            headers: {},
            query: {},
            body: null,
            contentType: ''
        }),

        // 👉 Estas 2 normalmente querés que fallen por status para integrarse con Retry
        cliente_por_id: Object.assign({}, HTTP_DEFAULTS, {
            label: 'Cliente por id (GET)',
            url: '/Api/Cliente.ashx',
            method: 'GET',
            headers: {},
            query: { id: '${solicitud.clienteId}' },
            body: null,
            contentType: '',
            failOnStatus: true,
            failStatusMin: 400
        }),

        cliente_demo_777: Object.assign({}, HTTP_DEFAULTS, {
            label: 'Cliente DEMO id=777 (GET)',
            url: '/Api/Cliente.ashx',
            method: 'GET',
            headers: {},
            query: { id: 777 },
            body: null,
            contentType: '',
            failOnStatus: true,
            failStatusMin: 400
        })
    });


    // --- IF ---
    merge('control.if', { expression: '${payload.status} == 200' });
    mergeGroup('control.if.templates', {
        status_200: { label: 'status == 200', expression: '${payload.status} == 200' },
        sql_rows_gt_0: { label: 'sql.rows > 0', expression: '${sql.rows} > 0' },
        has_payload_nro: { label: 'Existe payload.data.nro', expression: '${payload.data.nro}' },
        equals_ok: { label: 'payload.result == "OK"', expression: '${payload.result} == "OK"' },
        score_ge_700: { label: 'payload.score >= 700', expression: '${payload.score} >= 700' },
    });

    // --- DELAY ---
    merge('control.delay', { ms: 1000, message: 'Esperando...' });

    // --- RETRY ---
    merge('control.retry', { reintentos: 3, backoffMs: 500, message: '' });
    mergeGroup('control.retry.templates', {
        retry_x3: { label: 'Reintentar x3', reintentos: 3, backoffMs: 500, message: '' },
        retry_x5: { label: 'Reintentar x5', reintentos: 5, backoffMs: 800, message: '' },
        retry_rapido: { label: 'Rápido (x3, 200ms)', reintentos: 3, backoffMs: 200, message: '' }
    });

    // --- LOGGER ---
    merge('util.logger', { level: 'Info', message: 'Mensaje' });
    mergeGroup('util.logger.templates', {
        info: { label: 'Info: Comenzó', level: 'Info', message: 'Comenzó' },
        warning: {
            label: 'Warning: Valor extraño',
            level: 'Warning',
            message: 'Valor inesperado: ${payload.valor}',
        },
        error_http: {
            label: 'Error: Falló HTTP',
            level: 'Error',
            message: 'Falló HTTP: status=${payload.status}',
        },
    });

    // --- DATA.SQL ---
    merge('data.sql', {
        connectionStringName: 'DefaultConnection',
        query: 'INSERT INTO PolizasDemo (Numero, Asegurado) VALUES (@NroPoliza, @Asegurado);',
        parameters: { NroPoliza: '${payload.data.nro}', Asegurado: 'Póliza ${payload.data.nro}' },
    });
    mergeGroup('data.sql.templates', {
        insert_poliza_demo: {
            label: 'INSERT: PolizasDemo',
            query: 'INSERT INTO PolizasDemo (Numero, Asegurado) VALUES (@NroPoliza, @Asegurado);',
            parameters: { NroPoliza: '${payload.data.nro}', Asegurado: 'Póliza ${payload.data.nro}' },
        },
        update_asegurado_por_numero: {
            label: 'UPDATE Asegurado por Numero',
            query: 'UPDATE PolizasDemo SET Asegurado = @Asegurado WHERE Numero = @NroPoliza;',
            parameters: {
                NroPoliza: '${payload.data.nro}',
                Asegurado: 'Póliza ${payload.data.nro} (actualizada)',
            },
        },
        delete_por_numero: {
            label: 'DELETE por Numero',
            query: 'DELETE FROM PolizasDemo WHERE Numero = @NroPoliza;',
            parameters: { NroPoliza: '${payload.data.nro}' },
        },
        select_top10: {
            label: 'SELECT TOP 10',
            query: 'SELECT TOP 10 Numero, Asegurado FROM PolizasDemo;',
            parameters: {},
        },
        merge_upsert_demo: {
            label: 'MERGE Upsert por Numero',
            query:
                'MERGE PolizasDemo AS T USING (SELECT @NroPoliza AS Numero, @Asegurado AS Asegurado) AS S ON (T.Numero = S.Numero) WHEN MATCHED THEN UPDATE SET Asegurado = S.Asegurado WHEN NOT MATCHED THEN INSERT (Numero, Asegurado) VALUES (S.Numero, S.Asegurado);',
            parameters: { NroPoliza: '${payload.data.nro}', Asegurado: 'Póliza ${payload.data.nro}' },
        },
    });

    // --- NOTIFY / CHAT / QUEUE / DOC / ERROR / START-END ---
    merge('util.notify', {
        tipo: 'email',
        destino: 'ops@miempresa.com',
        asunto: 'Asunto',
        mensaje: 'Mensaje',
    });
    mergeGroup('util.notify.templates', {
        email_emision_ok: {
            label: 'Email: Emisión OK',
            tipo: 'email',
            destino: 'ops@miempresa.com',
            asunto: 'Póliza emitida',
            mensaje: 'Póliza ${payload.data.nro} emitida OK',
        },
        email_rechazo: {
            label: 'Email: Rechazo',
            tipo: 'email',
            destino: 'ops@miempresa.com',
            asunto: 'Rechazo de Solicitud',
            mensaje: 'Rechazo por score bajo',
        },
    });

    merge('chat.notify', { canal: '#ops', mensaje: 'Mensaje' });
    mergeGroup('chat.notify.templates', {
        ops_ok: { label: 'Chat: OK a #ops', canal: '#ops', mensaje: 'Póliza ${payload.data.nro} OK' },
        ops_error: {
            label: 'Chat: Error a #ops',
            canal: '#ops',
            mensaje: 'Error en ${payload.step}: ${payload.error}',
        },
    });

    merge('queue.publish', {
        broker: 'rabbitmq',
        queue: 'jobs',
        payload: { job: 'imprimir_poliza', polizaId: '${payload.polizaId}' },
    });
    mergeGroup('queue.publish.templates', {
        imprimir_poliza: {
            label: 'Job: Imprimir póliza',
            broker: 'rabbitmq',
            queue: 'jobs',
            payload: { job: 'imprimir_poliza', polizaId: '${payload.polizaId}' },
        },
    });

    merge('doc.entrada', {
        modo: 'simulado',
        salida: 'solicitud',
        extensiones: ['pdf', 'docx'],
        maxMB: 10,
    });

    merge('util.subflow', { ref: '', input: {} });

    mergeGroup('util.subflow.templates', {
        demo_subflow: {
            label: 'Demo: llamar subflow por Key',
            ref: 'DEMO.SUBFLOW',
            input: { clienteId: '${input.clienteId}', nota: 'llamado desde PADRE' }
        }
    });

    merge('util.error', { capturar: true, volverAIntentar: false, notificar: true });
    merge('util.start', {});
    merge('util.end', {});

    console.log('[WF templates] cargado. Keys=', Object.keys(window.PARAM_TEMPLATES || {}));
    console.log('[WF templates] http.request.templates=', Object.keys((window.PARAM_TEMPLATES || {})['http.request.templates'] || {}));

   
    // === avisar que las plantillas ya están listas ===
    (function () {
        function fire() {
            try { window.dispatchEvent(new Event('wf-templates-ready')); }
            catch (e) { var evt = document.createEvent('Event'); evt.initEvent('wf-templates-ready', true, true); window.dispatchEvent(evt); }
        }
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fire);
        else fire();
    })();
})();
