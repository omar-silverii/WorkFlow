// Scripts/workflow.ai.assistant.js
// Asistente IA del editor: interpreta intención con ML.NET local/offline.
// fix9: muestra plan validado y permite aplicarlo manualmente al canvas.
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
