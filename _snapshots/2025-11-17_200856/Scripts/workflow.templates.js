; (() => {
    // NO reasignamos window.PARAM_TEMPLATES; siempre mutamos el objeto existente.
    const PT = (window.PARAM_TEMPLATES = window.PARAM_TEMPLATES || {});
    const merge = (key, obj) => (PT[key] = Object.assign({}, obj, PT[key] || {}));
    const mergeGroup = (key, obj) => (PT[key] = Object.assign({}, PT[key] || {}, obj));

    // --- HTTP ---
    merge('http.request', {
        url: '/Api/Ping.ashx',
        method: 'GET',
        headers: {},
        query: {},
        body: null,
        contentType: '',
        timeoutMs: 8000,
    });
    mergeGroup('http.request.templates', {
        ping_get: {
            label: 'GET: /Api/Ping.ashx',
            url: '/Api/Ping.ashx',
            method: 'GET',
            headers: {},
            query: {},
            body: null,
            contentType: '',
        },
        get_con_query: {
            label: 'GET con query: ?clienteId',
            url: '/Api/Score.ashx',
            method: 'GET',
            headers: {},
            query: { clienteId: '${solicitud.clienteId}' },
            body: null,
            contentType: '',
        },
        post_json: {
            label: 'POST JSON',
            url: '/api/demo',
            method: 'POST',
            headers: {},
            query: {},
            body: { nro: '${payload.data.nro}', importe: '${payload.data.prima}' },
            contentType: 'application/json',
        },
        put_json: {
            label: 'PUT JSON',
            url: '/api/demo',
            method: 'PUT',
            headers: {},
            query: {},
            body: { id: '${payload.id}', valor: 'actualizado' },
            contentType: 'application/json',
        },
        delete_simple: {
            label: 'DELETE (simple)',
            url: '/api/demo/${payload.id}',
            method: 'DELETE',
            headers: {},
            query: {},
            body: null,
            contentType: '',
        },
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

    merge('util.error', { capturar: true, volverAIntentar: false, notificar: true });
    merge('util.start', {});
    merge('util.end', {});
})();
