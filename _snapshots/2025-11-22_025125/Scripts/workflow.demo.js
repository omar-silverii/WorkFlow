; (() => {
    // Este módulo sólo define funciones de demo.
    // En tiempo de click, el UI principal le pasa window.__WF_UI (API pública mínima).

    function demoEmision(UI) {
        const { findCat, createNode, edges, nodes, clearAll, drawEdges, renderInspector, uid } = UI;

        function P(x, y) { return { x, y }; }

        // Posiciones prolijas
        const pStart = P(80, 160),
            pEntrada = P(260, 160),
            pValida = P(460, 160),
            pScore = P(660, 160),
            pIf = P(860, 160),

            pIns = P(1060, 120),
            pNotifOk = P(1260, 100),
            pQueue = P(1460, 100),
            pEndOk = P(1640, 120),

            pNotifNo = P(1060, 220),
            pEndNo = P(1240, 240),

            pErr = P(860, 260);

        // Limpiar lienzo
        clearAll();

        // Nodos
        createNode(findCat('util.start'), pStart.x, pStart.y, 'Inicio');
        createNode(findCat('doc.entrada'), pEntrada.x, pEntrada.y, 'Solicitud de Póliza');
        createNode(findCat('data.sql'), pValida.x, pValida.y, 'Validar Cliente/Producto');
        createNode(findCat('http.request'), pScore.x, pScore.y, 'Scoring Riesgo');
        createNode(findCat('control.if'), pIf.x, pIf.y, 'Score ≥ 700');

        createNode(findCat('data.sql'), pIns.x, pIns.y, 'Insertar Póliza');
        createNode(findCat('util.notify'), pNotifOk.x, pNotifOk.y, 'Notificar Emisión');
        createNode(findCat('queue.publish'), pQueue.x, pQueue.y, 'Imprimir Póliza');
        createNode(findCat('util.end'), pEndOk.x, pEndOk.y, 'Fin (emitida)');

        createNode(findCat('util.notify'), pNotifNo.x, pNotifNo.y, 'Notificar Rechazo');
        createNode(findCat('util.end'), pEndNo.x, pEndNo.y, 'Fin (rechazo)');

        createNode(findCat('util.error'), pErr.x, pErr.y, 'Manejador Error');

        // Helper para buscar id de nodo por (key,label)
        const idOf = (key, label) => {
            const n = nodes.find(n => n.key === key && n.label === label);
            return n && n.id;
        };

        // doc.entrada
        (function () {
            const n = nodes.find(nn => nn.key === 'doc.entrada' && nn.label === 'Solicitud de Póliza');
            if (n) {
                n.params = Object.assign({}, n.params, {
                    modo: 'simulado',
                    salida: 'solicitud',
                    extensiones: Array.isArray(n.params?.extensiones) ? n.params.extensiones : ['pdf', 'docx'],
                    maxMB: typeof n.params?.maxMB === 'number' ? n.params.maxMB : 10
                });
            }
        })();

        // data.sql Validar
        (function () {
            const n = nodes.find(nn => nn.key === 'data.sql' && nn.label === 'Validar Cliente/Producto');
            if (n) {
                n.params = {
                    connectionStringName: 'DefaultConnection',
                    query: '/* validar */ SELECT 1 AS ok',
                    parameters: {}
                };
            }
        })();

        // http.request Score
        (function () {
            const n = nodes.find(nn => nn.key === 'http.request' && nn.label === 'Scoring Riesgo');
            if (n) {
                n.params = {
                    url: '/Api/Score.ashx',
                    method: 'GET',
                    headers: {},
                    query: { clienteId: '${solicitud.clienteId}' },
                    timeoutMs: 8000
                };
            }
        })();

        // if Score
        (function () {
            const n = nodes.find(nn => nn.key === 'control.if' && nn.label === 'Score ≥ 700');
            if (n) n.params = { expression: '${payload.score} >= 700' };
        })();

        // Insertar Póliza
        (function () {
            const n = nodes.find(nn => nn.key === 'data.sql' && nn.label === 'Insertar Póliza');
            if (n) {
                n.params = {
                    connectionStringName: 'DefaultConnection',
                    query: '/* insertar póliza (demo) */ SELECT 12345 AS polizaId',
                    parameters: {}
                };
            }
        })();

        // Notificar Emisión
        (function () {
            const n = nodes.find(nn => nn.key === 'util.notify' && nn.label === 'Notificar Emisión');
            if (n) n.params = { tipo: 'email', destino: 'ops@miempresa.com', mensaje: 'Póliza emitida OK' };
        })();

        // Publicar impresión
        (function () {
            const n = nodes.find(nn => nn.key === 'queue.publish' && nn.label === 'Imprimir Póliza');
            if (n) n.params = { broker: 'rabbitmq', queue: 'jobs', payload: { job: 'imprimir_poliza', polizaId: '${payload.polizaId}' } };
        })();

        // Notificar Rechazo
        (function () {
            const n = nodes.find(nn => nn.key === 'util.notify' && nn.label === 'Notificar Rechazo');
            if (n) n.params = { tipo: 'email', destino: 'ops@miempresa.com', mensaje: 'Solicitud rechazada por score bajo' };
        })();

        // Aristas
        const nStart = idOf('util.start', 'Inicio'),
            nEntrada = idOf('doc.entrada', 'Solicitud de Póliza'),
            nValida = idOf('data.sql', 'Validar Cliente/Producto'),
            nScore = idOf('http.request', 'Scoring Riesgo'),
            nIf = idOf('control.if', 'Score ≥ 700'),
            nIns = idOf('data.sql', 'Insertar Póliza'),
            nNotifOk = idOf('util.notify', 'Notificar Emisión'),
            nQueue = idOf('queue.publish', 'Imprimir Póliza'),
            nEndOk = idOf('util.end', 'Fin (emitida)'),
            nNotifNo = idOf('util.notify', 'Notificar Rechazo'),
            nEndNo = idOf('util.end', 'Fin (rechazo)'),
            nErr = idOf('util.error', 'Manejador Error');

        edges.push({ id: uid('e'), from: nStart, to: nEntrada, condition: 'always' });
        edges.push({ id: uid('e'), from: nEntrada, to: nValida, condition: 'always' });
        edges.push({ id: uid('e'), from: nValida, to: nScore, condition: 'always' });

        // Errores hacia manejador
        edges.push({ id: uid('e'), from: nValida, to: nErr, condition: 'error' });
        edges.push({ id: uid('e'), from: nScore, to: nErr, condition: 'error' });

        edges.push({ id: uid('e'), from: nScore, to: nIf, condition: 'always' });

        // Rama TRUE (emitida)
        edges.push({ id: uid('e'), from: nIf, to: nIns, condition: 'true' });
        edges.push({ id: uid('e'), from: nIns, to: nNotifOk, condition: 'always' });
        edges.push({ id: uid('e'), from: nNotifOk, to: nQueue, condition: 'always' });
        edges.push({ id: uid('e'), from: nQueue, to: nEndOk, condition: 'always' });

        // Rama FALSE (rechazo)
        edges.push({ id: uid('e'), from: nIf, to: nNotifNo, condition: 'false' });
        edges.push({ id: uid('e'), from: nNotifNo, to: nEndNo, condition: 'always' });

        // Meta de plantilla
        window.__WF_META = { Template: 'EMISION_POLIZA' };

        drawEdges();
        renderInspector();
    }

    function demoSimple(UI) {
        const { findCat, createNode, edges, nodes, clearAll, drawEdges, renderInspector, uid } = UI;

        function P(x, y) { return { x, y }; }

        clearAll();

        const pStart = P(100, 160),
            pHttp = P(320, 160),
            pIf = P(540, 160),
            pChat = P(760, 120),
            pEnd = P(980, 160),
            pLog = P(760, 220);

        createNode(findCat('util.start'), pStart.x, pStart.y, 'Inicio');
        createNode(findCat('http.request'), pHttp.x, pHttp.y, 'Solicitud HTTP');
        createNode(findCat('control.if'), pIf.x, pIf.y, 'If status==200');
        createNode(findCat('chat.notify'), pChat.x, pChat.y, 'Chat OK');
        createNode(findCat('util.logger'), pLog.x, pLog.y, 'Logger Error');
        const log = nodes.find(n => n.label === 'Logger Error');
        if (log) log.params = { level: 'Error', message: 'Falló HTTP' };
        createNode(findCat('util.end'), pEnd.x, pEnd.y, 'Fin');

        const idOf = (key, label) => {
            const n = nodes.find(n => n.key === key && n.label === label);
            return n && n.id;
        };

        const nStart = idOf('util.start', 'Inicio'),
            nHttp = idOf('http.request', 'Solicitud HTTP'),
            nIf = idOf('control.if', 'If status==200'),
            nChat = idOf('chat.notify', 'Chat OK'),
            nLog = idOf('util.logger', 'Logger Error'),
            nEnd = idOf('util.end', 'Fin');

        edges.push({ id: uid('e'), from: nStart, to: nHttp, condition: 'always' });
        edges.push({ id: uid('e'), from: nHttp, to: nIf, condition: 'always' });
        edges.push({ id: uid('e'), from: nIf, to: nChat, condition: 'true' });
        edges.push({ id: uid('e'), from: nIf, to: nEnd, condition: 'false' });
        edges.push({ id: uid('e'), from: nHttp, to: nLog, condition: 'error' });
        edges.push({ id: uid('e'), from: nLog, to: nEnd, condition: 'always' });

        drawEdges();
        renderInspector();
    }

    // Registro público
    window.WF_Demo = {
        Emision: demoEmision,
        Simple: demoSimple
    };
})();
