// Scripts/inspectors/json.validator.js
(function () {
    'use strict';

    // Namespace global
    const WF_Json = (window.WF_Json = window.WF_Json || {});

    // ---------- helpers ----------
    function safeJsonParse(str) {
        if (str == null) return { ok: false, error: 'null' };
        const s = String(str).trim();
        if (!s) return { ok: true, value: null }; // vacío = permitido
        try {
            return { ok: true, value: JSON.parse(s) };
        } catch (e) {
            return { ok: false, error: e && e.message ? e.message : String(e) };
        }
    }

    function setInvalid(el, msg) {
        if (!el) return;
        el.dataset.jsonInvalid = '1';
        el.dataset.jsonError = msg || '';
        el.style.borderColor = '#ef4444'; // rojo
        el.style.outline = 'none';
        el.title = msg || 'JSON inválido';
    }

    function setValid(el) {
        if (!el) return;
        delete el.dataset.jsonInvalid;
        delete el.dataset.jsonError;
        el.style.borderColor = ''; // vuelve al css normal
        el.title = '';
    }

    function prettyStringify(value) {
        return JSON.stringify(value, null, 2);
    }

    // ---------- API pública ----------
    // 1) Validación en vivo (borde rojo si falla)
    WF_Json.attachValidator = function (textarea, opts) {
        if (!textarea) return;
        opts = opts || {};
        const debounceMs = typeof opts.debounceMs === 'number' ? opts.debounceMs : 250;

        let t = null;

        function validateNow() {
            const r = safeJsonParse(textarea.value);
            if (!r.ok) setInvalid(textarea, r.error);
            else setValid(textarea);
        }

        function schedule() {
            if (t) clearTimeout(t);
            t = setTimeout(validateNow, debounceMs);
        }

        // Validar al inicio y ante cambios
        validateNow();
        textarea.addEventListener('input', schedule);

        // Retornar una función manual por si querés forzar validar
        return validateNow;
    };

    // 2) Formatear (pretty print) el JSON del textarea
    //    - Si está vacío: no hace nada
    //    - Si es inválido: deja el borde rojo y NO pisa el texto
    WF_Json.formatTextarea = function (textarea) {
        if (!textarea) return { ok: false, error: 'no-textarea' };

        const raw = String(textarea.value || '');
        const trimmed = raw.trim();
        if (!trimmed) {
            setValid(textarea);
            return { ok: true, formatted: '' };
        }

        const r = safeJsonParse(trimmed);
        if (!r.ok) {
            setInvalid(textarea, r.error);
            return { ok: false, error: r.error };
        }

        // Pretty-print manteniendo el scroll lo mejor posible
        const prevScrollTop = textarea.scrollTop;

        const out = prettyStringify(r.value);
        textarea.value = out;

        textarea.scrollTop = prevScrollTop;
        setValid(textarea);

        return { ok: true, formatted: out };
    };

    // 3) Conectar un botón a un textarea para "Formatear JSON"
    WF_Json.attachFormatterButton = function (buttonEl, textarea) {
        if (!buttonEl || !textarea) return;
        buttonEl.addEventListener('click', function () {
            WF_Json.formatTextarea(textarea);
        });
    };

    console.log('[json.validator] WF_Json listo:', Object.keys(WF_Json));
})();
