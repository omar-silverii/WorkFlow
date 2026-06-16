// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// No aplica cambios al canvas en esta primera entrega: solo muestra plan validado.
(function () {
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

    function renderResult(res) {
        var out = $('wfAiResult');
        if (!out) return;

        if (!res || !res.ok) {
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

        html += renderList('Datos faltantes', missing, '');
        html += renderList('Errores de validación', validation.errors || [], 'wf-ai-error-list');
        html += renderList('Advertencias', warnings, 'wf-ai-warning-list');

        html += '<details class="wf-ai-json"><summary>Ver JSON técnico</summary><pre>' + htmlEncode(JSON.stringify(plan, null, 2)) + '</pre></details>';

        out.innerHTML = html;
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
                if (res.ok) setStatus('Propuesta recibida. Todavía no se aplicó al canvas.', 'ok');
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
        var btn = $('wfAiRun');
        if (btn) btn.addEventListener('click', interpretar);

        var clear = $('wfAiClear');
        if (clear) clear.addEventListener('click', function () {
            var t = $('wfAiPrompt');
            var r = $('wfAiResult');
            if (t) t.value = '';
            if (r) r.innerHTML = '';
            setStatus('', '');
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
