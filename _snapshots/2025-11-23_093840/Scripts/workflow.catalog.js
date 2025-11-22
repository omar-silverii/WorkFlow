// Scripts/workflow.catalog.js
// Catálogo PROFESIONAL — solo editá este archivo para sumar/ocultar nodos.
(function (global) {
    // ====== Íconos mínimos (SVG inline). Si falta alguno, se usa 'box'.
    var ICONS = {
        play: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>',
        stop: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M6 6h12v12H6z"/></svg>',
        globe: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="10"/></svg>',
        db: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v9c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/></svg>',
        branch: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="6" cy="6" r="3"/><circle cx="18" cy="6" r="3"/><circle cx="18" cy="18" r="3"/><path d="M9 6h6M6 9v6c0 2 1.5 3 3 3h6"/></svg>',
        merge: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="12" r="3"/><path d="M9 6c6 0 6 12 0 12"/></svg>',
        split: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 12L6 6M12 12l6-6M12 12l6 6M12 12L6 18"/></svg>',
        loop: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M7 7h8a4 4 0 010 8h-1M7 7l3-3M7 7l3 3M9 15H7a4 4 0 010-8h1"/></svg>',
        clock: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="12" r="10"/><path d="M12 6v7l4 2"/></svg>',
        retry: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M3 12a9 9 0 0015 6l2 2V14h-6l2 2a7 7 0 11-2-9"/></svg>',
        gauge: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M4 13a8 8 0 1116 0H4z"/><path d="M12 13l4-4"/></svg>',
        file: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12V8z"/><path d="M14 2v6h6"/></svg>',
        mail: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M4 6h16v12H4z"/><path d="M4 7l8 6 8-6"/></svg>',
        cloud: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M6 18h11a4 4 0 000-8 6 6 0 10-11 4"/></svg>',
        chat: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M4 4h16v10H5l-1 1z"/></svg>',
        code: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M8 16l-4-4 4-4M16 8l4 4-4 4M10 20l4-16"/></svg>',
        script: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M4 4h16v4H4zM4 10h10v4H4zM4 16h16v4H4z"/></svg>',
        var: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="7" cy="12" r="3"/><circle cx="17" cy="12" r="3"/><path d="M10 12h4"/></svg>',
        lock: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="5" y="10" width="14" height="10" rx="2"/><path d="M8 10V7a4 4 0 118 0v3"/></svg>',
        bot: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="5" y="8" width="14" height="10" rx="2"/><circle cx="9" cy="13" r="1"/><circle cx="15" cy="13" r="1"/><path d="M12 4v4"/></svg>',
        user: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="12" cy="8" r="4"/><path d="M4 20c0-3 3-6 8-6s8 3 8 6"/></svg>',
        server: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="5" width="16" height="6" rx="2"/><rect x="4" y="13" width="16" height="6" rx="2"/></svg>',
        bell: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M18 16V11a6 6 0 10-12 0v5l-2 2h16z"/></svg>',
        alert: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2L2 22h20L12 2z"/><circle cx="12" cy="17" r="1"/><path d="M12 10v4"/></svg>',
        workflow: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="6" r="2"/><circle cx="12" cy="18" r="2"/><circle cx="19" cy="12" r="2"/><path d="M7 12h3M14 6h3M14 18h3M12 8v8"/></svg>',
        queue: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="3" y="5" width="18" height="4"/><rect x="3" y="10" width="18" height="4"/><rect x="3" y="15" width="18" height="4"/></svg>',
        upload: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 16V4M7 9l5-5 5 5"/><rect x="4" y="16" width="16" height="4"/></svg>',
        download: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 4v12M7 11l5 5 5-5"/><rect x="4" y="18" width="16" height="2"/></svg>',
        key: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><circle cx="8" cy="8" r="3"/><path d="M11 8h11l-3 3 3 3h-6l-2 2"/></svg>',
        bolt: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M13 2L3 14h7l-1 8 10-12h-7z"/></svg>',
        box: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="4" y="4" width="16" height="16" rx="3"/></svg>',
        // NUEVO: icono para control.if
        filter: '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M3 5h18l-7 8v4l-4 2v-6z"/></svg>'
    };

    // ====== Catálogo de nodos (profesional, en español)
    var CATALOG = [
        // Disparadores
        { key: "trigger.webhook", label: "Webhook", tint: "#0ea5e9", icon: "globe" },
        { key: "trigger.cron", label: "Programador (Cron)", tint: "#0ea5e9", icon: "clock" },
        { key: "trigger.queue", label: "Disparador de Cola", tint: "#0ea5e9", icon: "queue" },

        // Control
        { key: "control.if", label: "Condición (If)", tint: "#8b5cf6", icon: "filter" },
        { key: "control.switch", label: "Selector (Switch)", tint: "#8b5cf6", icon: "branch" },
        { key: "control.parallel", label: "Paralelo (Split)", tint: "#8b5cf6", icon: "split" },
        { key: "control.join", label: "Unir (Join)", tint: "#8b5cf6", icon: "merge" },
        { key: "control.loop", label: "Bucle (Loop/ForEach)", tint: "#8b5cf6", icon: "loop" },
        { key: "control.delay", label: "Demora (Delay)", tint: "#8b5cf6", icon: "clock" },
        { key: "control.retry", label: "Reintentar", tint: "#8b5cf6", icon: "retry" },
        { key: "control.ratelimit", label: "Límite de tasa", tint: "#8b5cf6", icon: "gauge" },

        // Datos & Integraciones
        { key: "http.request", label: "Solicitud HTTP", tint: "#0284c7", icon: "globe" },
        { key: "data.sql", label: "Consulta SQL", tint: "#0284c7", icon: "db" },
        { key: "data.redis.get", label: "Cache GET (Redis)", tint: "#0284c7", icon: "db" },
        { key: "data.redis.set", label: "Cache SET (Redis)", tint: "#0284c7", icon: "db" },
        { key: "file.read", label: "Archivo: Leer", tint: "#0284c7", icon: "file" },
        { key: "file.write", label: "Archivo: Escribir", tint: "#0284c7", icon: "file" },
        { key: "email.send", label: "Correo: Enviar", tint: "#0284c7", icon: "mail" },
        { key: "chat.notify", label: "Chat/Slack", tint: "#0284c7", icon: "chat" },
        { key: "cloud.storage", label: "Almacenamiento Cloud", tint: "#0284c7", icon: "cloud" },
        { key: "queue.publish", label: "Cola: Publicar", tint: "#0284c7", icon: "upload" },
        { key: "queue.consume", label: "Cola: Consumir", tint: "#0284c7", icon: "download" },
        { key: "ftp.get", label: "FTP: Descargar", tint: "#0284c7", icon: "download" },
        { key: "ftp.put", label: "FTP: Subir", tint: "#0284c7", icon: "upload" },
        { key: "doc.entrada", label: "Entrada de documento", tint: "#0284c7", icon: "file" },

        // Transformación & Lógica
        { key: "transform.map", label: "Transformar (Mapeo)", tint: "#16a34a", icon: "code" },
        { key: "code.function", label: "Función (C#)", tint: "#16a34a", icon: "code" },
        { key: "code.script", label: "Script (JS)", tint: "#16a34a", icon: "script" },
        { key: "state.vars", label: "Variables", tint: "#16a34a", icon: "var" },
        { key: "config.secrets", label: "Secretos/Claves", tint: "#16a34a", icon: "key" },
        { key: "ai.call", label: "AI / LLM", tint: "#16a34a", icon: "bot" },

        // Utilidad / Operación
        { key: "util.start", label: "Inicio", tint: "#0f766e", icon: "play" },
        { key: "util.end", label: "Fin", tint: "#0f766e", icon: "stop" },
        { key: "util.logger", label: "Logger", tint: "#0f766e", icon: "server" },
        { key: "util.notify", label: "Notificar", tint: "#0f766e", icon: "bell" },
        { key: "util.error", label: "Manejador de Error", tint: "#0f766e", icon: "alert" },
        { key: "util.subflow", label: "Subflujo", tint: "#0f766e", icon: "workflow" },
        // Tareas humanas
        { key: "human.task", label: "Tarea humana", tint: "#f97316", icon: "user" }
    ];

    // ====== Grupos
    var GROUPS = [
        { name: "Disparadores", items: ["trigger.webhook", "trigger.cron", "trigger.queue"] },
        { name: "Control", items: ["control.if", "control.switch", "control.parallel", "control.join", "control.loop", "control.delay", "control.retry", "control.ratelimit"] },
        { name: "Datos e Integraciones", items: ["http.request", "data.sql", "data.redis.get", "data.redis.set", "file.read", "file.write", "email.send", "chat.notify", "cloud.storage", "queue.publish", "queue.consume", "ftp.get", "ftp.put", "doc.entrada"] },
        { name: "Transformación y Lógica", items: ["transform.map", "code.function", "code.script", "state.vars", "config.secrets", "ai.call"] },
        { name: "Utilidad / Operación", items: ["util.start", "util.end", "util.logger", "util.notify", "util.error", "util.subflow", "human.task"] }
    ];

    // ====== Plantillas de parámetros (aparecen en el Inspector → Insertar plantilla)
    var PARAM_TEMPLATES = {
        // Disparadores
        "trigger.webhook": { path: "/hook/orden", secret: "cambiar-esto", method: "POST", auth: false },
        "trigger.cron": { cron: "*/5 * * * *", zonaHoraria: "America/Argentina/Buenos_Aires" },
        "trigger.queue": { broker: "rabbitmq", queue: "entrantes", ack: true },

        // Control
        "control.if": { expression: "${payload.status} == 200" },
        "control.switch": { casos: { ok: "${payload.ok}", error: "${payload.error}" }, default: "ok" },
        "control.parallel": { ramas: 2, maxConcurrencia: 4 },
        "control.join": { tipo: "all", timeoutMs: 30000 },
        "control.loop": { forEach: "${items}", itemVar: "item", max: 100 },
        "control.delay": { ms: 1000 },
        "control.retry": { reintentos: 3, backoffMs: 500 },
        "control.ratelimit": { porSegundos: 60, max: 100 },

        // Datos & Integraciones
        "http.request": {
            url: "https://api.example.com",
            method: "GET",
            headers: {},
            query: {},
            body: null,
            timeoutMs: 10000
        },
        "data.sql": {
            connection: "Server=.;Database=MiDb;Trusted_Connection=True;",
            query: "SELECT TOP 10 * FROM dbo.Usuarios ORDER BY Id DESC",
            parameters: {},
            resultMode: "NonQuery" // NonQuery | Scalar | DataTable
        },
        "data.redis.get": { connection: "localhost:6379", key: "mi-clave" },
        "data.redis.set": { connection: "localhost:6379", key: "mi-clave", value: "${payload}", ttlSeconds: 300 },
        "file.read": { path: "C:/data/entrada.json", encoding: "utf-8" },
        "file.write": { path: "C:/data/salida.json", encoding: "utf-8", overwrite: true },
        "email.send": { smtp: "smtp.miempresa.com", from: "noreply@miempresa.com", to: ["ops@miempresa.com"], subject: "Asunto", html: "<b>Hola</b>" },
        "chat.notify": { provider: "slack", webhookUrl: "https://hooks.slack.com/services/xxx", channel: "#general", message: "Hola", mention: [] },
        "cloud.storage": { provider: "s3", bucket: "mi-bucket", key: "ruta/archivo", region: "us-east-1" },
        "queue.publish": { broker: "rabbitmq", queue: "jobs", payload: {} },
        "queue.consume": { broker: "rabbitmq", queue: "jobs", prefetch: 10 },
        "ftp.get": { host: "ftp.miempresa.com", user: "usuario", password: "***", remotePath: "/in/archivo.csv", localPath: "C:/in/archivo.csv" },
        "ftp.put": { host: "ftp.miempresa.com", user: "usuario", password: "***", localPath: "C:/out/archivo.csv", remotePath: "/out/archivo.csv" },
        "doc.entrada": {
            modo: "simulado",
            extensiones: ["pdf", "docx"],
            maxMB: 10,
            salida: "documento"
        },

        // Transformación & Lógica
        "transform.map": { mapping: { "out.campo": "${in.campo}" } },
        "code.function": { code: "return input;" },
        "code.script": { language: "js", code: "return input;" },
        "state.vars": { set: { clave: "valor" }, get: ["clave"] },
        "config.secrets": { set: { API_KEY: "***" }, get: ["API_KEY"] },
        "ai.call": { provider: "openai", model: "gpt-4o-mini", prompt: "Decí hola", temperature: 0.2, maxTokens: 256 },

        // Utilidad / Operación
        "util.start": {},
        "util.end": {},
        "util.logger": { level: "Info", message: "Comenzó" },
        "util.notify": { tipo: "email", destino: "ops@miempresa.com", mensaje: "Finalizó el flujo" },
        "util.error": { capturar: true, volverAIntentar: false, notificar: true },
        "util.subflow": { workflowId: "reemplazar-con-id" },
        "human.task": {
            titulo: "Tarea para ${wf.instanceId}",
            descripcion: "Completar acción pendiente",
            rol: "RRHH",
            usuarioAsignado: "",
            deadlineMinutes: 1440,
            metadata: {
                origen: "workflow",
                instanciaId: "${wf.instanceId}"
            }
        }
    };

    // Exponer en global
    global.WorkflowData = {
        CATALOG: CATALOG,
        GROUPS: GROUPS,
        PARAM_TEMPLATES: PARAM_TEMPLATES,
        ICONS: ICONS
    };
})(window);
